namespace RagEval.Core.Configuration;

/// <summary>How documents are chunked. Section: <c>Chunking</c>.</summary>
public sealed class ChunkingOptions
{
    public const string Section = "Chunking";

    /// <summary>Largest chunk, in characters.</summary>
    public int Size { get; set; } = 1000;

    /// <summary>Characters shared by consecutive chunks.</summary>
    public int Overlap { get; set; } = 200;
}
