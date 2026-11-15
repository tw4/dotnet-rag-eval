using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using RagEval.Cli;

namespace RagEval.Tests;

public class SkeletonTests
{
    [Fact]
    public async Task Api_health_reports_ok()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(TestContext.Current.CancellationToken);
        Assert.Equal("ok", body?["status"]);
    }

    [Fact]
    public async Task Cli_prints_help()
    {
        var output = new StringWriter();
        var result = CliApp.Build().Parse(["--help"]);
        var exit = await result.InvokeAsync(new() { Output = output }, TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Contains("rag-eval", output.ToString(), StringComparison.Ordinal);
    }
}
