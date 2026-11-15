using System.Net.Http.Json;
using System.Text.Json;

namespace RagEval.Tests.Support;

/// <summary>
/// An Ollama at localhost:11434 with a given model pulled. Tests that need it are skipped
/// otherwise, so the suite stays fast and runs without models.
/// </summary>
internal static class OllamaFixture
{
    public static readonly Uri Url = new(Environment.GetEnvironmentVariable("RAGEVAL_TEST_OLLAMA") ?? "http://localhost:11434");

    private static readonly Lazy<HashSet<string>> Installed = new(() =>
    {
        try
        {
            using var http = new HttpClient { BaseAddress = Url, Timeout = TimeSpan.FromSeconds(2) };
            using var tags = http.GetFromJsonAsync<JsonDocument>("api/tags").GetAwaiter().GetResult();
            return [.. tags!.RootElement.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString()!)];
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    });

    public static void SkipUnlessModel(string model) =>
        Assert.SkipUnless(Installed.Value.Contains(model) || Installed.Value.Contains(model + ":latest"),
            $"Ollama with '{model}' is not available at {Url}.");
}
