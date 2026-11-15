using RagEval.Core;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", version = BuildInfo.Version }));

app.Run();

/// <summary>Entry point, public so integration tests can host the app.</summary>
public partial class Program;
