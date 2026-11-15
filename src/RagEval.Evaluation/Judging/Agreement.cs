using System.Globalization;
using System.Text;

namespace RagEval.Evaluation.Judging;

/// <summary>A human label for one judged answer: <c>yes</c>, <c>partial</c> or <c>no</c> per criterion (null: not labelled).</summary>
public sealed record HumanLabel(string Id, Verdict? Faithfulness, Verdict? Correctness, string Note);

/// <summary>How well the judge agrees with people on one criterion.</summary>
/// <param name="Criterion">faithfulness or correctness.</param>
/// <param name="Items">Answers labelled by both.</param>
/// <param name="Exact">Share of identical verdicts.</param>
/// <param name="Kappa">Cohen's kappa: agreement beyond chance (1 perfect, 0 chance level).</param>
/// <param name="WeightedKappa">Kappa with linear weights: partial vs yes is half a disagreement.</param>
/// <param name="Binary">Agreement on "yes" versus anything else.</param>
/// <param name="Confusion">Counts by [human, judge] verdict (no, partial, yes).</param>
/// <param name="JudgeMean">Mean score the judge gave these answers.</param>
/// <param name="HumanMean">Mean score people gave them.</param>
public sealed record CriterionAgreement(string Criterion, int Items, double Exact, double Kappa, double WeightedKappa, double Binary,
    int[][] Confusion, double JudgeMean, double HumanMean);

/// <summary>
/// The hand-labelling loop for the judge: draw a random sample of judged answers, export it for
/// people to label without seeing the judge's verdicts, and measure agreement on the labels.
/// </summary>
public static class Agreement
{
    /// <summary>The id of a judged answer: dataset, run and query.</summary>
    public static string Id(AnswerRunResult run, AnswerQueryResult query)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(query);
        return $"{run.Dataset}/{run.Run.Name}/{query.QueryId}";
    }

    /// <summary>A reproducible random sample of answered (not refused) questions across all runs.</summary>
    public static IReadOnlyList<(AnswerRunResult Run, AnswerQueryResult Query)> Sample(AnswerExperimentResult result, int count, int seed = 17)
    {
        ArgumentNullException.ThrowIfNull(result);
        var answered = result.Runs.SelectMany(r => r.PerQuery.Where(q => !q.Refused).Select(q => (Run: r, Query: q)))
            .OrderBy(x => Id(x.Run, x.Query), StringComparer.Ordinal)
            .ToList();
        var random = new Random(seed);
        return [.. answered.OrderBy(_ => random.Next()).Take(count).OrderBy(x => Id(x.Run, x.Query), StringComparer.Ordinal)];
    }

    /// <summary>Writes a sample as CSV for labelling: the judge's verdicts are left out on purpose.</summary>
    public static void WriteSheet(IReadOnlyList<(AnswerRunResult Run, AnswerQueryResult Query)> sample, string path)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var sb = new StringBuilder();
        Row(sb, ["id", "question", "reference", "answer", "passages", "faithfulness", "correctness", "note"]);
        foreach (var (run, query) in sample)
        {
            var passages = string.Join("\n\n", query.Context.Select((p, i) => $"[{i + 1}] {p}"));
            Row(sb, [Id(run, query), query.Question, query.Reference, query.Answer, passages, "", "", ""]);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));   // the BOM tells spreadsheets it is UTF-8
    }

    /// <summary>Reads labels from a CSV with at least the columns id, faithfulness and correctness.</summary>
    public static IReadOnlyList<HumanLabel> ReadLabels(string path)
    {
        var rows = Csv.Parse(File.ReadAllText(path));
        if (rows.Count == 0)
        {
            return [];
        }

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Column(string name) => header.IndexOf(name) is var i and >= 0 ? i : throw new InvalidDataException($"{path} has no '{name}' column.");
        var (id, faithful, correct, note) = (Column("id"), Column("faithfulness"), Column("correctness"), header.IndexOf("note"));
        return [.. rows.Skip(1)
            .Where(r => r.Count > id && r[id].Trim().Length > 0)
            .Select(r => new HumanLabel(r[id].Trim(), ParseLabel(r, faithful, path), ParseLabel(r, correct, path),
                note >= 0 && r.Count > note ? r[note] : ""))];
    }

    private static Verdict? ParseLabel(IReadOnlyList<string> row, int column, string path)
    {
        var value = row.Count > column ? row[column].Trim().ToLowerInvariant() : "";
        return value switch
        {
            "" => null,
            "yes" or "y" or "1" => Verdict.Yes,
            "partial" or "p" or "0.5" => Verdict.Partial,
            "no" or "n" or "0" => Verdict.No,
            _ => throw new InvalidDataException($"{path}: '{value}' is not a label; use yes, partial or no."),
        };
    }

    /// <summary>Agreement between people and the judge on faithfulness and on correctness.</summary>
    public static IReadOnlyList<CriterionAgreement> Compute(AnswerExperimentResult result, IReadOnlyList<HumanLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(labels);
        var judged = result.Runs.SelectMany(r => r.PerQuery.Select(q => (Id: Id(r, q), q)))
            .ToDictionary(x => x.Id, x => x.q, StringComparer.Ordinal);
        var unknown = labels.FirstOrDefault(l => !judged.ContainsKey(l.Id));
        if (unknown is not null)
        {
            throw new InvalidDataException($"Label '{unknown.Id}' matches no judged answer in these results.");
        }

        return
        [
            Criterion("faithfulness", labels.Where(l => l.Faithfulness is not null && judged[l.Id].Faithfulness is not null)
                .Select(l => (l.Faithfulness!.Value, judged[l.Id].Faithfulness!.Verdict)).ToList()),
            Criterion("correctness", labels.Where(l => l.Correctness is not null && judged[l.Id].Correctness is not null)
                .Select(l => (l.Correctness!.Value, judged[l.Id].Correctness!.Verdict)).ToList()),
        ];
    }

    internal static CriterionAgreement Criterion(string name, IReadOnlyList<(Verdict Human, Verdict Judge)> pairs)
    {
        var confusion = new int[3][];
        for (var i = 0; i < 3; i++)
        {
            confusion[i] = new int[3];
        }

        foreach (var (human, judge) in pairs)
        {
            confusion[(int)human][(int)judge]++;
        }

        var n = pairs.Count;
        if (n == 0)
        {
            return new CriterionAgreement(name, 0, double.NaN, double.NaN, double.NaN, double.NaN, confusion, double.NaN, double.NaN);
        }

        return new CriterionAgreement(name, n,
            pairs.Count(p => p.Human == p.Judge) / (double)n,
            Kappa(confusion, (i, j) => i == j ? 1 : 0),
            Kappa(confusion, (i, j) => 1 - (Math.Abs(i - j) / 2.0)),
            pairs.Count(p => (p.Human == Verdict.Yes) == (p.Judge == Verdict.Yes)) / (double)n,
            confusion,
            pairs.Average(p => (int)p.Judge / 2.0),
            pairs.Average(p => (int)p.Human / 2.0));
    }

    /// <summary>Weighted Cohen's kappa from a confusion matrix and agreement weights (1 on the diagonal).</summary>
    internal static double Kappa(int[][] confusion, Func<int, int, double> weight)
    {
        var k = confusion.Length;
        double n = confusion.Sum(r => r.Sum());
        if (n == 0)
        {
            return double.NaN;
        }

        var rows = confusion.Select(r => r.Sum() / n).ToArray();
        var cols = Enumerable.Range(0, k).Select(j => confusion.Sum(r => r[j]) / n).ToArray();
        double observed = 0, expected = 0;
        for (var i = 0; i < k; i++)
        {
            for (var j = 0; j < k; j++)
            {
                observed += weight(i, j) * confusion[i][j] / n;
                expected += weight(i, j) * rows[i] * cols[j];
            }
        }

        return expected >= 1 ? (observed >= 1 ? 1 : double.NaN) : (observed - expected) / (1 - expected);
    }

    public static string Markdown(IReadOnlyList<CriterionAgreement> agreements, string judgeModel)
    {
        ArgumentNullException.ThrowIfNull(agreements);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(inv, $"# Judge agreement with human labels\n\nJudge: `{judgeModel}`\n\n");
        sb.Append("| Criterion | Labelled | Exact agreement | Cohen's kappa | Weighted kappa | Yes vs not | Judge mean | Human mean |\n");
        sb.Append("|---|---:|---:|---:|---:|---:|---:|---:|\n");
        foreach (var a in agreements)
        {
            sb.Append(inv, $"| {a.Criterion} | {a.Items} | {a.Exact:F2} | {a.Kappa:F2} | {a.WeightedKappa:F2} | {a.Binary:F2} | {a.JudgeMean:F2} | {a.HumanMean:F2} |\n");
        }

        foreach (var a in agreements)
        {
            sb.Append(inv, $"\n**{a.Criterion}** (rows: human, columns: judge)\n\n| | no | partial | yes |\n|---|---:|---:|---:|\n");
            string[] names = ["no", "partial", "yes"];
            for (var i = 0; i < 3; i++)
            {
                sb.Append(inv, $"| {names[i]} | {a.Confusion[i][0]} | {a.Confusion[i][1]} | {a.Confusion[i][2]} |\n");
            }
        }

        sb.Append("\nKappa corrects agreement for chance: above 0.6 is usually read as substantial, above 0.8 as almost perfect. ")
          .Append("Weighted kappa counts yes against partial as half a disagreement.\n");
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, IEnumerable<string> cells)
    {
        sb.AppendJoin(',', cells.Select(c => c.Contains('"', StringComparison.Ordinal) || c.Contains(',', StringComparison.Ordinal) ||
            c.Contains('\n', StringComparison.Ordinal) || c.Contains('\r', StringComparison.Ordinal)
            ? "\"" + c.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : c));
        sb.Append("\r\n");
    }
}

/// <summary>A small RFC 4180 CSV reader: quoted fields may hold commas, quotes and line breaks.</summary>
public static class Csv
{
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
