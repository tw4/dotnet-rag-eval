using RagEval.Api;
using RagEval.Api.Endpoints;
using RagEval.Api.Health;
using RagEval.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRagPipeline(builder.Configuration);
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

app.MapDocumentEndpoints();

app.Run();

/// <summary>Entry point, public so integration tests can host the app.</summary>
public partial class Program;
