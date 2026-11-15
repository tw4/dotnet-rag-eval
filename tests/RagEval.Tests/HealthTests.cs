using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RagEval.Tests;

public class HealthTests
{
    [Fact]
    public async Task Ready_is_503_when_dependencies_are_unreachable()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // Port 1 refuses connections immediately.
            b.UseSetting("Qdrant:Url", "http://127.0.0.1:1");
            b.UseSetting("Ollama:Url", "http://127.0.0.1:1");
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("\"qdrant\":\"unreachable\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ollama\":\"unreachable\"", body, StringComparison.Ordinal);
    }
}
