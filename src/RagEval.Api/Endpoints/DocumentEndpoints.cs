using RagEval.Core.Indexing;
using RagEval.Ingestion.Loaders;

namespace RagEval.Api.Endpoints;

internal static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        // Upload a Markdown or HTML file: it is chunked, embedded and indexed (replacing an earlier version).
        app.MapPost("/documents", async (IFormFile file, string? id, Indexer indexer, IEnumerable<IDocumentLoader> loaders,
                CancellationToken ct) =>
            {
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                var loader = loaders.FirstOrDefault(l => l.Extensions.Contains(extension));
                if (loader is null)
                {
                    return Results.Problem(
                        $"Unsupported file type '{extension}'. Supported: {string.Join(", ", loaders.SelectMany(l => l.Extensions))}.",
                        statusCode: StatusCodes.Status415UnsupportedMediaType);
                }

                using var reader = new StreamReader(file.OpenReadStream());
                var content = await reader.ReadToEndAsync(ct);
                var documentId = string.IsNullOrWhiteSpace(id) ? Path.GetFileName(file.FileName) : id;
                var document = loader.Load(documentId, file.FileName, content);
                var chunks = await indexer.IndexAsync(document, ct);
                return Results.Ok(new { id = document.Id, title = document.Title, chunks = chunks.Count });
            })
            .DisableAntiforgery();
    }
}
