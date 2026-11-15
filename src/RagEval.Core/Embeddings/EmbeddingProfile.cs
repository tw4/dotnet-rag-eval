namespace RagEval.Core.Embeddings;

/// <summary>
/// Text an embedding model expects before documents and queries. Several retrieval models are
/// trained with such prefixes and lose quality without them.
/// </summary>
/// <param name="Model">Model name as Ollama knows it.</param>
/// <param name="DocumentPrefix">Prepended to every chunk before embedding.</param>
/// <param name="QueryPrefix">Prepended to every question before embedding.</param>
public sealed record EmbeddingProfile(string Model, string DocumentPrefix, string QueryPrefix)
{
    private static readonly (string Family, string Document, string Query)[] Known =
    [
        ("nomic-embed-text", "search_document: ", "search_query: "),
        ("mxbai-embed-large", "", "Represent this sentence for searching relevant passages: "),
        ("snowflake-arctic-embed", "", "Represent this sentence for searching relevant passages: "),
        ("e5", "passage: ", "query: "),
        ("multilingual-e5", "passage: ", "query: "),
    ];

    /// <summary>The profile for a model: known prefixes for its family, otherwise none.</summary>
    public static EmbeddingProfile For(string model)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);
        var name = model.Split(':')[0];
        name = name[(name.LastIndexOf('/') + 1)..];
        foreach (var (family, document, query) in Known)
        {
            // The family as a whole hyphen-separated part of the name: "nomic-embed-text-v2",
            // "intfloat-multilingual-e5-large", "snowflake-arctic-embed2".
            var padded = "-" + name.ToLowerInvariant() + "-";
            if (padded.Contains("-" + family + "-", StringComparison.Ordinal) ||
                padded.Contains("-" + family + "2-", StringComparison.Ordinal))
            {
                return new EmbeddingProfile(model, document, query);
            }
        }

        return new EmbeddingProfile(model, "", "");
    }
}
