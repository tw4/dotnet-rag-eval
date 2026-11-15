using System.CommandLine;
using RagEval.Core;

namespace RagEval.Cli;

/// <summary>The rag-eval command tree. Commands are added by later stages (ingest, eval).</summary>
internal static class CliApp
{
    public static RootCommand Build()
    {
        var root = new RootCommand($"rag-eval {BuildInfo.Version}: on-premises RAG with every stage measured.");
        return root;
    }
}
