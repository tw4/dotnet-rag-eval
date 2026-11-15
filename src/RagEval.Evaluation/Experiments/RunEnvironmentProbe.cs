using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using Qdrant.Client;
using RagEval.Core.Configuration;

namespace RagEval.Evaluation.Experiments;

/// <summary>Records the code, machine and model versions behind a result.</summary>
internal static class RunEnvironmentProbe
{
    public static async Task<RunEnvironment> CollectAsync(QdrantClient qdrant, OllamaOptions ollama, IEnumerable<string> models,
        CancellationToken cancellationToken)
    {
        string? qdrantVersion = null, ollamaVersion = null;
        var digests = new Dictionary<string, string>();
        try
        {
            qdrantVersion = (await qdrant.HealthAsync(cancellationToken)).Version;
        }
        catch (Grpc.Core.RpcException)
        {
        }

        using var http = new HttpClient { BaseAddress = ollama.Url, Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var version = await http.GetFromJsonAsync<JsonDocument>("api/version", cancellationToken);
            ollamaVersion = version?.RootElement.GetProperty("version").GetString();
            using var tags = await http.GetFromJsonAsync<JsonDocument>("api/tags", cancellationToken);
            var installed = tags!.RootElement.GetProperty("models").EnumerateArray()
                .ToDictionary(m => m.GetProperty("name").GetString()!, m => m.GetProperty("digest").GetString()!);
            foreach (var model in models)
            {
                var key = model.Contains(':', StringComparison.Ordinal) ? model : model + ":latest";
                if (installed.TryGetValue(key, out var digest))
                {
                    digests[model] = digest;
                }
            }
        }
        catch (HttpRequestException)
        {
        }

        var (commit, dirty) = Git();
        return new RunEnvironment(commit, dirty, RuntimeInformation.OSDescription, Cpu(), RuntimeInformation.FrameworkDescription,
            ollamaVersion, qdrantVersion, digests);
    }

    private static (string? Commit, bool? Dirty) Git()
    {
        var commit = Run("git", "rev-parse HEAD");
        var status = Run("git", "status --porcelain --untracked-files=no");
        return (commit, commit is null ? null : status?.Length > 0);
    }

    private static string Cpu() =>
        (OperatingSystem.IsMacOS() ? Run("sysctl", "-n machdep.cpu.brand_string") : null)
        ?? $"{RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} cores";

    private static string? Run(string file, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
