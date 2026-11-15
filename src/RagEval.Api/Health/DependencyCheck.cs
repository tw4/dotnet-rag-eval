using Microsoft.Extensions.Options;
using RagEval.Core.Configuration;

namespace RagEval.Api.Health;

/// <summary>Checks that Qdrant and Ollama answer, for <c>GET /health/ready</c>.</summary>
internal sealed class DependencyCheck(HttpClient http, IOptions<QdrantOptions> qdrant, IOptions<OllamaOptions> ollama)
{
    public async Task<Dictionary<string, string>> CheckAsync(CancellationToken cancellationToken)
    {
        var qdrantTask = ProbeAsync(new Uri(qdrant.Value.Url, "readyz"), cancellationToken);
        var ollamaTask = ProbeAsync(new Uri(ollama.Value.Url, "api/version"), cancellationToken);
        return new()
        {
            ["qdrant"] = await qdrantTask,
            ["ollama"] = await ollamaTask,
        };
    }

    private async Task<string> ProbeAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode ? "ok" : $"HTTP {(int)response.StatusCode}";
        }
        catch (HttpRequestException)
        {
            return "unreachable";
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "timeout";
        }
    }
}
