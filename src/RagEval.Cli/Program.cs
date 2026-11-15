namespace RagEval.Cli;

/// <summary>Entry point of the rag-eval command-line tool.</summary>
internal static class EntryPoint
{
    public static Task<int> Main(string[] args) => CliApp.Build().Parse(args).InvokeAsync();
}
