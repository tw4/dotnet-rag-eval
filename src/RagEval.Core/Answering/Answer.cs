using RagEval.Core.Indexing;

namespace RagEval.Core.Answering;

/// <summary>A source the answer cites: the chunk behind <c>[Number]</c> in the answer text.</summary>
public sealed record Citation(int Number, string DocumentId, string ChunkId, string? Title, string? Section, string? Source,
    int StartLine, int EndLine, int SourceStart, int SourceEnd, int? Page, double Score, string Text);

/// <summary>The result of a question.</summary>
/// <param name="Text">The answer, with <c>[n]</c> markers referring to <see cref="Citations"/>.</param>
/// <param name="Refused">True when the documents do not answer the question.</param>
/// <param name="RefusalReason">Why: nothing relevant was retrieved, or the model found no answer in the context.</param>
/// <param name="Citations">Sources cited in the answer, in order of first citation.</param>
/// <param name="Retrieved">Every chunk the model was given, best first.</param>
public sealed record Answer(string Text, bool Refused, string? RefusalReason, IReadOnlyList<Citation> Citations,
    IReadOnlyList<RetrievedChunk> Retrieved);
