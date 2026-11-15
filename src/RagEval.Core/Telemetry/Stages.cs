using System.Collections.Concurrent;
using System.Diagnostics;

namespace RagEval.Core.Telemetry;

/// <summary>
/// The pipeline's stages as <see cref="Activity"/> spans of the <c>RagEval</c> source, so they can
/// be exported with OpenTelemetry or timed in-process with <see cref="StageRecorder"/>. Starting a
/// stage costs nothing when nobody listens.
/// </summary>
public static class Stages
{
    public const string SourceName = "RagEval";

    public const string Load = "ingest.load";
    public const string Document = "ingest.document";
    public const string Chunk = "ingest.chunk";
    public const string EmbedDocuments = "ingest.embed";
    public const string Store = "ingest.store";

    public const string EmbedQuery = "query.embed";
    public const string Retrieve = "query.retrieve";
    public const string Rerank = "query.rerank";
    public const string Generate = "answer.generate";
    public const string Answer = "answer";

    private static readonly ActivitySource Source = new(SourceName);

    /// <summary>Starts a stage; dispose the result to end it. Null when nothing listens.</summary>
    public static Activity? Start(string stage) => Source.StartActivity(stage, ActivityKind.Internal);
}

/// <summary>
/// Records the duration of every stage that ends while it is alive, grouped by the root span
/// (one answer, one indexed document), so per-request breakdowns and percentiles can be computed.
/// </summary>
public sealed class StageRecorder : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<(ActivityTraceId Trace, string Stage, double Value)> _ends = new();

    /// <summary>Suffix of the pseudo-stage that holds a generation's output token count.</summary>
    public const string TokensSuffix = ".tokens";

    public StageRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Stages.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                _ends.Enqueue((activity.TraceId, activity.OperationName, activity.Duration.TotalMilliseconds));
                if (activity.GetTagItem("output_tokens") is { } tokens)
                {
                    _ends.Enqueue((activity.TraceId, activity.OperationName + TokensSuffix, Convert.ToDouble(tokens, System.Globalization.CultureInfo.InvariantCulture)));
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Forgets everything recorded so far (after a warm-up).</summary>
    public void Clear() => _ends.Clear();

    /// <summary>
    /// One dictionary per root span, stage → total milliseconds within it (stages that run several
    /// times within one request add up), and generation stages' output tokens under
    /// <c>stage + </c><see cref="TokensSuffix"/>.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, double>> Requests(string rootStage)
    {
        var byTrace = _ends.GroupBy(e => e.Trace).ToList();
        return [.. byTrace
            .Where(g => g.Any(e => e.Stage == rootStage))
            .Select(g => (IReadOnlyDictionary<string, double>)g.GroupBy(e => e.Stage).ToDictionary(s => s.Key, s => s.Sum(e => e.Value), StringComparer.Ordinal))];
    }

    public void Dispose() => _listener.Dispose();
}
