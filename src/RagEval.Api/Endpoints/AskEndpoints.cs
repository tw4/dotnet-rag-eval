using RagEval.Core.Answering;

namespace RagEval.Api.Endpoints;

/// <summary>Request body of <c>POST /ask</c>.</summary>
public sealed record AskRequest(string Question);

internal static class AskEndpoints
{
    public static void MapAskEndpoints(this IEndpointRouteBuilder app)
    {
        // Retrieve the top-k chunks and answer with citations, or refuse when nothing relevant is found.
        app.MapPost("/ask", async (AskRequest request, Answerer answerer, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.Problem("A question is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var answer = await answerer.AskAsync(request.Question, ct);
            return Results.Ok(new
            {
                answer = answer.Text,
                refused = answer.Refused,
                refusalReason = answer.RefusalReason,
                citations = answer.Citations,
                retrieved = answer.Retrieved.Select(r => new { r.Chunk.Id, r.Chunk.DocumentId, r.Chunk.Section, r.Score }),
            });
        });
    }
}
