using System.Globalization;
using System.Text;

namespace RagEval.Core.Indexing;

/// <summary>
/// Turns text into the terms a sparse index matches on: runs of letters and digits, lower-cased
/// with Turkish dotted and dotless i folded together, and optionally cut to their first
/// <see cref="PrefixLength"/> letters, a crude stemmer that works well for Turkish suffixes.
/// </summary>
public sealed class TextAnalyzer
{
    private TextAnalyzer(int? prefixLength) => PrefixLength = prefixLength;

    /// <summary>Terms as they are, only folded.</summary>
    public static TextAnalyzer Plain { get; } = new(null);

    /// <summary>Keep only the first <paramref name="length"/> letters of each word (digits are kept whole).</summary>
    public static TextAnalyzer Prefix(int length) =>
        length >= 2 ? new TextAnalyzer(length) : throw new ArgumentOutOfRangeException(nameof(length), "A prefix needs at least 2 letters.");

    /// <summary><c>plain</c> or <c>prefix-N</c>.</summary>
    public static TextAnalyzer Parse(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name == "plain")
        {
            return Plain;
        }

        return name.StartsWith("prefix-", StringComparison.Ordinal) &&
               int.TryParse(name.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? Prefix(n)
            : throw new NotSupportedException($"Unknown analyzer '{name}': use 'plain' or 'prefix-N'.");
    }

    public int? PrefixLength { get; }

    public string Name => PrefixLength is { } n ? $"prefix-{n}" : "plain";

    public List<string> Terms(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var terms = new List<string>();
        var current = new StringBuilder();
        var letters = true;

        void Flush()
        {
            if (current.Length > 0)
            {
                var term = current.ToString();
                terms.Add(letters && PrefixLength is { } n && term.Length > n ? term[..n] : term);
                current.Clear();
            }

            letters = true;
        }

        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c switch
                {
                    'I' or 'İ' or 'ı' => 'i',
                    _ => char.ToLowerInvariant(c),
                });
                letters &= char.IsLetter(c);
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                Flush();   // marks (such as the dot of a decomposed İ) neither split nor join words
            }
        }

        Flush();
        return terms;
    }
}

/// <summary>A sparse vector: term ids and their weights, ids ascending and unique.</summary>
public sealed record SparseVector(uint[] Indices, float[] Values)
{
    public int Count => Indices.Length;
}

/// <summary>
/// BM25 as sparse vectors. Documents carry the saturated term frequency
/// tf·(k1+1) / (tf + k1·(1 − b + b·dl/avgdl)); queries carry each term's count. Qdrant multiplies
/// the two and the IDF it keeps per term (the <c>Idf</c> modifier), which gives BM25 exactly.
/// Terms are hashed to 32-bit ids (FNV-1a), so there is no vocabulary to store.
/// </summary>
public sealed class Bm25Encoder(TextAnalyzer analyzer, double averageLength, double k1 = 1.2, double b = 0.75)
{
    public TextAnalyzer Analyzer { get; } = analyzer ?? throw new ArgumentNullException(nameof(analyzer));

    public double AverageLength { get; } = averageLength > 0 ? averageLength : 1;

    public double K1 { get; } = k1;

    public double B { get; } = b;

    /// <summary>Average number of terms per text, for the length normalisation.</summary>
    public static double AverageTerms(IEnumerable<string> texts, TextAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(analyzer);
        long terms = 0, count = 0;
        foreach (var text in texts)
        {
            terms += analyzer.Terms(text).Count;
            count++;
        }

        return count == 0 ? 1 : (double)terms / count;
    }

    public SparseVector EncodeDocument(string text)
    {
        var terms = Analyzer.Terms(text);
        var norm = K1 * (1 - B + (B * terms.Count / AverageLength));
        return Build(terms, tf => (float)(tf * (K1 + 1) / (tf + norm)));
    }

    public SparseVector EncodeQuery(string text) => Build(Analyzer.Terms(text), tf => tf);

    public static uint TermId(string term)
    {
        ArgumentNullException.ThrowIfNull(term);
        var hash = 2166136261u;
        foreach (var c in term)
        {
            hash = (hash ^ (c & 0xFFu)) * 16777619u;
            hash = (hash ^ (uint)(c >> 8)) * 16777619u;
        }

        return hash;
    }

    private static SparseVector Build(List<string> terms, Func<int, float> weight)
    {
        var counts = new SortedDictionary<uint, int>();
        foreach (var term in terms)
        {
            var id = TermId(term);
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }

        return new SparseVector([.. counts.Keys], [.. counts.Values.Select(weight)]);
    }
}
