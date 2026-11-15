using RagEval.Api.Health;
using RagEval.Core;
using RagEval.Core.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<QdrantOptions>(builder.Configuration.GetSection(QdrantOptions.Section));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.Section));
builder.Services.AddHttpClient<DependencyCheck>(c => c.Timeout = TimeSpan.FromSeconds(3));

var app = builder.Build();

// Liveness: the process serves requests.
app.MapGet("/health", () => Results.Ok(new { status = "ok", version = BuildInfo.Version }));

// Readiness: Qdrant and Ollama answer.
app.MapGet("/health/ready", async (DependencyCheck check, CancellationToken ct) =>
{
    var dependencies = await check.CheckAsync(ct);
    var ready = dependencies.Values.All(v => v == "ok");
    return Results.Json(new { status = ready ? "ok" : "unavailable", dependencies },
        statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.Run();

/// <summary>Entry point, public so integration tests can host the app.</summary>
public partial class Program;
