using System.CommandLine;
using RagEval.Core.Configuration;
using RagEval.Evaluation.Experiments;
using RagEval.Evaluation.Judging;

namespace RagEval.Cli;

/// <summary><c>rag-eval eval run &lt;experiment.json&gt;</c>.</summary>
internal static class EvalCommand
{
    public static Command Create()
    {
        var command = new Command("eval", "Run evaluation experiments.");
        command.Subcommands.Add(Run());
        command.Subcommands.Add(Latency());
        command.Subcommands.Add(Answers());
        command.Subcommands.Add(LabelSample());
        command.Subcommands.Add(AgreementCommand());
        command.Subcommands.Add(CompareCommand());
        return command;
    }

    private static Command CompareCommand()
    {
        var current = new Argument<FileInfo>("results") { Description = "New results.json" };
        var reference = new Argument<FileInfo>("reference") { Description = "Reference results.json of the same experiment" };
        var tolerance = new Option<double>("--tolerance") { Description = "Largest drop allowed per metric", DefaultValueFactory = _ => 0.01 };

        var command = new Command("compare", "Compare results with reference results; exit code 1 when a metric dropped by more than the tolerance.")
        {
            current, reference, tolerance,
        };
        command.SetAction(async (result, cancellationToken) =>
        {
            var writer = result.InvocationConfiguration.Output;
            var changes = Comparison.Compare(ReportWriter.Read(result.GetValue(reference)!.FullName), ReportWriter.Read(result.GetValue(current)!.FullName));
            var limit = result.GetValue(tolerance);
            await writer.WriteLineAsync(Comparison.Markdown(changes, limit));
            var regressions = Comparison.Regressions(changes, limit);
            await writer.WriteLineAsync(regressions.Count == 0
                ? $"No regressions ({changes.Count} metrics within {limit:0.###})."
                : $"{regressions.Count} metric(s) dropped by more than {limit:0.###}.");
            return regressions.Count == 0 ? 0 : 1;
        });
        return command;
    }

    private static Command LabelSample()
    {
        var results = new Argument<FileInfo>("results") { Description = "results.json of an answer experiment" };
        var output = new Option<FileInfo>("--out") { Description = "CSV to write", Required = true };
        var count = new Option<int>("--count") { Description = "Answers in the sample", DefaultValueFactory = _ => 40 };
        var seed = new Option<int>("--seed") { Description = "Random seed", DefaultValueFactory = _ => 17 };

        var command = new Command("label-sample", "Export a random sample of judged answers for hand labelling (without the judge's verdicts).")
        {
            results, output, count, seed,
        };
        command.SetAction(async (result, cancellationToken) =>
        {
            var outcome = AnswerEvaluator.Read(result.GetValue(results)!.FullName);
            var sample = Agreement.Sample(outcome, result.GetValue(count), result.GetValue(seed));
            var path = result.GetValue(output)!.FullName;
            Agreement.WriteSheet(sample, path);
            await result.InvocationConfiguration.Output.WriteLineAsync(
                $"Wrote {sample.Count} answers to {path}. Fill in faithfulness and correctness with yes, partial or no.");
            return 0;
        });
        return command;
    }

    private static Command AgreementCommand()
    {
        var results = new Argument<FileInfo>("results") { Description = "results.json of an answer experiment" };
        var labels = new Argument<FileInfo>("labels") { Description = "Labelled CSV (columns id, faithfulness, correctness)" };
        var output = new Option<FileInfo?>("--out") { Description = "Also write the report to this Markdown file" };

        var command = new Command("agreement", "Measure how well the judge agrees with human labels.") { results, labels, output };
        command.SetAction(async (result, cancellationToken) =>
        {
            var outcome = AnswerEvaluator.Read(result.GetValue(results)!.FullName);
            var agreement = Agreement.Compute(outcome, Agreement.ReadLabels(result.GetValue(labels)!.FullName));
            var markdown = Agreement.Markdown(agreement, outcome.JudgeModel);
            if (result.GetValue(output) is { } file)
            {
                await File.WriteAllTextAsync(file.FullName, markdown, cancellationToken);
            }

            await result.InvocationConfiguration.Output.WriteLineAsync(markdown);
            return 0;
        });
        return command;
    }

    private static Command Answers()
    {
        var config = new Argument<FileInfo>("experiment") { Description = "Answer experiment file with a judge, e.g. experiments/answers.json" };
        var output = new Option<DirectoryInfo?>("--out") { Description = "Results folder (default: runs/<experiment>/<timestamp>)" };
        var limit = new Option<int?>("--limit") { Description = "Only the first N questions per dataset (quick check)" };

        var command = new Command("answers", "Generate answers and judge their faithfulness and correctness.") { config, output, limit };
        command.Options.Add(DatasetsCommand.ManifestOption);
        command.Options.Add(DatasetsCommand.DataOption);
        command.SetAction(async (result, cancellationToken) =>
        {
            var writer = result.InvocationConfiguration.Output;
            var settings = Settings.From(result);
            var experiment = ExperimentConfig.Load(result.GetValue(config)!.FullName);
            using var qdrant = Clients.Qdrant(settings.Qdrant);
            Task<RagEval.Evaluation.Datasets.EvalDataset> Load(string id, CancellationToken ct) => DatasetsCommand.LoadAsync(result, id, ct);
            var runner = new ExperimentRunner(qdrant, settings.Ollama, Load) { Log = writer.WriteLine };
            var evaluator = new AnswerEvaluator(qdrant, settings.Ollama, runner, Load) { Log = writer.WriteLine, QueryLimit = result.GetValue(limit) };
            var outcome = await evaluator.RunAsync(experiment, cancellationToken);
            var folder = result.GetValue(output)?.FullName
                ?? Path.Combine("runs", experiment.Name, outcome.StartedAt.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
            var (json, markdown) = AnswerEvaluator.Write(outcome, folder);
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(await File.ReadAllTextAsync(markdown, cancellationToken));
            await writer.WriteLineAsync($"Wrote {json} and {markdown}.");
            return 0;
        });
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
