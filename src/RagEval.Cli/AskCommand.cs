using System.CommandLine;
using System.Globalization;
using OllamaSharp;
using RagEval.Core.Answering;
using RagEval.Core.Configuration;
using RagEval.Core.Embeddings;
using RagEval.Core.Indexing;
using RagEval.Core.Reranking;

namespace RagEval.Cli;

/// <summary><c>rag-eval ask "question"</c>: answer from the indexed documents, with citations.</summary>
internal static class AskCommand
{
    public static Command Create()
    {
        var question = new Argument<string>("question") { Description = "The question" };
        var topK = new Option<int>("--top-k") { Description = "Chunks given to the model", DefaultValueFactory = _ => 5 };
        var minScore = new Option<double>("--min-score") { Description = "Refuse when no chunk scores at least this", DefaultValueFactory = _ => 0.5 };
        var chatModel = new Option<string?>("--chat-model") { Description = "Ollama chat model (default qwen3:4b)" };
        var rerank = new Option<string?>("--rerank")
        {
            Description = "Rerank retrieved chunks with this Ollama model (e.g. qwen3-reranker:0.6b); --min-score then applies to its scores",
        };
        var rerankCandidates = new Option<int>("--rerank-candidates") { Description = "Chunks reranked", DefaultValueFactory = _ => 20 };

        var command = new Command("ask", "Answer a question from the indexed documents, citing the sources.")
        {
            question, topK, minScore, chatModel, rerank, rerankCandidates,
        };
        command.SetAction(async (result, cancellationToken) =>
        {
            var output = result.InvocationConfiguration.Output;
            var settings = Settings.From(result);
            using var qdrant = Clients.Qdrant(settings.Qdrant);
            using var embeddings = Clients.Embeddings(settings.Ollama);
            using var chat = Clients.Chat(settings.Ollama, result.GetValue(chatModel));
            using var ollama = new OllamaApiClient(settings.Ollama.Url);
            IRetriever retriever = new DenseRetriever(new ChunkStore(qdrant, settings.Qdrant.Collection));
            if (result.GetValue(rerank) is { } rerankModel)
            {
                retriever = new RerankingRetriever(retriever, new YesNoReranker(ollama, rerankModel), result.GetValue(rerankCandidates));
            }

            var answerer = new Answerer(
                new Embedder(embeddings, EmbeddingProfile.For(settings.Ollama.EmbeddingModel)),
                retriever,
                chat,
                new AnsweringOptions { TopK = result.GetValue(topK), MinScore = result.GetValue(minScore) },
                Clients.DeterministicChatOptions());

            var answer = await answerer.AskAsync(result.GetValue(question)!, cancellationToken);
            await output.WriteLineAsync(answer.Text);
            if (answer.Refused)
            {
                await output.WriteLineAsync($"(refused: {answer.RefusalReason})");
                return 0;
            }

            await output.WriteLineAsync();
            foreach (var c in answer.Citations)
            {
                var where = c.Page is { } page ? $"page {page}" : c.StartLine == c.EndLine ? $"line {c.StartLine}" : $"lines {c.StartLine}-{c.EndLine}";
                await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                    $"[{c.Number}] {c.DocumentId}, {where}{(c.Section is null ? "" : $" ({c.Section})")}, score {c.Score:F3}"));
            }

            return 0;
        });
        return command;
    }
}
