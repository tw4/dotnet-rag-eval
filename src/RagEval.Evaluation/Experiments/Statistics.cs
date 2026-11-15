namespace RagEval.Evaluation.Experiments;

/// <summary>Small statistics helpers for reports.</summary>
public static class Statistics
{
    /// <summary>The <paramref name="p"/>-th percentile (0–100), linear interpolation between ranks.</summary>
    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var sorted = values.Order().ToArray();
        var rank = p / 100 * (sorted.Length - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return sorted[low] + ((sorted[high] - sorted[low]) * (rank - low));
    }

    /// <summary>
    /// 95% bootstrap confidence interval of the mean (percentile method, fixed seed so reports are
    /// reproducible). Shows how much a mean over a few hundred queries can move by chance.
    /// </summary>
    public static (double Low, double High) BootstrapCi95(IReadOnlyList<double> values, int resamples = 2000, int seed = 7)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return (double.NaN, double.NaN);
        }

        var random = new Random(seed);
        var means = new double[resamples];
        for (var r = 0; r < resamples; r++)
        {
            var sum = 0.0;
            for (var i = 0; i < values.Count; i++)
            {
                sum += values[random.Next(values.Count)];
            }

            means[r] = sum / values.Count;
        }

        return (Percentile(means, 2.5), Percentile(means, 97.5));
    }
}
