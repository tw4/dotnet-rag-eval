using System.Globalization;
using System.Text;

namespace RagEval.Evaluation.Experiments;

/// <summary>A metric of one run on one dataset, now and in the reference results.</summary>
public sealed record MetricChange(string Dataset, string Run, string Metric, double Reference, double Current)
{
    public double Delta => Current - Reference;
}

/// <summary>
/// Compares new results with reference results of the same experiment, run by run, to catch
/// regressions: the pipeline is deterministic (temperature 0, fixed models, reused indexes),
/// so any drop beyond a small tolerance means a change in behaviour.
/// </summary>
public static class Comparison
{
    /// <summary>Metrics checked for regressions; latency is not (it depends on the machine and its load).</summary>
    public static readonly IReadOnlyList<string> Checked = ["ndcg@10", "recall@10", "recall@100", "mrr@10", "answer_hit@5", "answer_hit@10"];

    public static IReadOnlyList<MetricChange> Compare(ExperimentResult reference, ExperimentResult current)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(current);
        var changes = new List<MetricChange>();
        foreach (var run in current.Runs)
        {
            var before = reference.Runs.FirstOrDefault(r => r.Dataset == run.Dataset && r.Run.Name == run.Run.Name)
                ?? throw new InvalidDataException($"The reference has no run '{run.Run.Name}' on '{run.Dataset}'.");
            if (before.Queries != run.Queries)
            {
                throw new InvalidDataException($"'{run.Run.Name}' on '{run.Dataset}': {run.Queries} queries now, {before.Queries} in the reference.");
            }

            changes.AddRange(Checked
                .Where(m => run.Metrics.ContainsKey(m) && before.Metrics.ContainsKey(m))
                .Select(m => new MetricChange(run.Dataset, run.Run.Name, m, before.Metrics[m], run.Metrics[m])));
        }

        return changes;
    }

    public static IReadOnlyList<MetricChange> Regressions(IReadOnlyList<MetricChange> changes, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return [.. changes.Where(c => c.Delta < -tolerance)];
    }

    public static string Markdown(IReadOnlyList<MetricChange> changes, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("| Dataset | Run | Metric | Reference | Now | Change |\n|---|---|---|---:|---:|---:|\n");
        foreach (var c in changes)
        {
            var flag = c.Delta < -tolerance ? " ⚠ regression" : "";
            sb.Append(inv, $"| {c.Dataset} | {c.Run} | {c.Metric} | {c.Reference:F3} | {c.Current:F3} | {c.Delta:+0.000;-0.000;0.000}{flag} |\n");
        }

        return sb.ToString();
    }
}
