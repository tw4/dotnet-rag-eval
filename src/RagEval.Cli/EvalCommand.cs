using System.CommandLine;
using RagEval.Core.Configuration;
using RagEval.Evaluation.Experiments;

namespace RagEval.Cli;

/// <summary><c>rag-eval eval run &lt;experiment.json&gt;</c>.</summary>
internal static class EvalCommand
{
    public static Command Create()
    {
        var command = new Command("eval", "Run evaluation experiments.");
        command.Subcommands.Add(Run());
        command.Subcommands.Add(Latency());
        return command;
    }

    private static Command Latency()
    {
        var config = new Argument<FileInfo>("benchmark") { Description = "Latency benchmark file, e.g. experiments/latency.json" };
        var output = new Option<DirectoryInfo?>("--out") { Description = "Results folder (default: runs/<benchmark>/<timestamp>)" };
        var queries = new Option<int?>("--queries") { Description = "Questions per run (overrides the file)" };

        var command = new Command("latency", "Time every pipeline stage, from indexing to the answer, and write p50/p95.") { config, output, queries };
        command.Options.Add(DatasetsCommand.ManifestOption);
        command.Options.Add(DatasetsCommand.DataOption);
        command.SetAction(async (result, cancellationToken) =>
        {
            var writer = result.InvocationConfiguration.Output;
            var settings = Settings.From(result);
            var benchmark = LatencyConfig.Load(result.GetValue(config)!.FullName);
            if (result.GetValue(queries) is { } n)
            {
                benchmark = benchmark with { Queries = n };
            }

            using var qdrant = Clients.Qdrant(settings.Qdrant);
            Task<RagEval.Evaluation.Datasets.EvalDataset> Load(string id, CancellationToken ct) => DatasetsCommand.LoadAsync(result, id, ct);
            var runner = new ExperimentRunner(qdrant, settings.Ollama, Load) { Log = writer.WriteLine };
            var bench = new LatencyBench(qdrant, settings.Ollama, runner, Load) { Log = writer.WriteLine };
            var outcome = await bench.RunAsync(benchmark, cancellationToken);
            var folder = result.GetValue(output)?.FullName
                ?? Path.Combine("runs", benchmark.Name, outcome.StartedAt.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
            var (json, markdown) = LatencyBench.Write(outcome, folder);
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(await File.ReadAllTextAsync(markdown, cancellationToken));
            await writer.WriteLineAsync($"Wrote {json} and {markdown}.");
            return 0;
        });
        return command;
    }

    private static Command Run()
    {
        var config = new Argument<FileInfo>("experiment") { Description = "Experiment file, e.g. experiments/baseline.json" };
        var output = new Option<DirectoryInfo?>("--out") { Description = "Results folder (default: runs/<experiment>/<timestamp>)" };
        var limit = new Option<int?>("--limit") { Description = "Only the first N queries per dataset (quick check)" };
        var datasets = new Option<string[]>("--dataset") { Description = "Only these datasets (repeatable)", AllowMultipleArgumentsPerToken = true };

        var command = new Command("run", "Run an experiment and write results.json and summary.md.") { config, output, limit, datasets };
        command.Options.Add(DatasetsCommand.ManifestOption);
        command.Options.Add(DatasetsCommand.DataOption);
        command.SetAction(async (result, cancellationToken) =>
        {
            var writer = result.InvocationConfiguration.Output;
            var settings = Settings.From(result);
            var experiment = ExperimentConfig.Load(result.GetValue(config)!.FullName);
            if (result.GetValue(datasets) is { Length: > 0 } only)
            {
                experiment = experiment with { Datasets = [.. experiment.Datasets.Where(only.Contains)] };
            }

            using var qdrant = Clients.Qdrant(settings.Qdrant);
            var runner = new ExperimentRunner(qdrant, settings.Ollama, (id, ct) => DatasetsCommand.LoadAsync(result, id, ct))
            {
                QueryLimit = result.GetValue(limit),
                Log = writer.WriteLine,
            };
            var outcome = await runner.RunAsync(experiment, cancellationToken);
            var folder = result.GetValue(output)?.FullName
                ?? Path.Combine("runs", experiment.Name, outcome.StartedAt.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
            var (json, markdown) = ReportWriter.Write(outcome, folder);
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(await File.ReadAllTextAsync(markdown, cancellationToken));
            await writer.WriteLineAsync($"Wrote {json} and {markdown}.");
            return 0;
        });
        return command;
    }
}
