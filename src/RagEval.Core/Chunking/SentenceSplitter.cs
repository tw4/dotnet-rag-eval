namespace RagEval.Core.Chunking;

/// <summary>
/// Splits text into sentences for English and Turkish prose: a sentence ends at . ! ? or … followed
/// by whitespace and a capital letter, digit or quote, unless the period follows a known abbreviation
/// or a single letter (initials). Paragraph breaks always end a sentence.
/// </summary>
public static class SentenceSplitter
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        // English
        "e.g", "i.e", "etc", "vs", "et al", "al", "fig", "figs", "eq", "ref", "refs", "no", "vol", "approx", "dr", "mr", "mrs", "ms",
        "prof", "st", "jr", "sr", "inc", "ltd", "co", "cf", "ca", "min", "max", "resp",
        // Turkish
        "vb", "vs", "örn", "bkz", "yy", "mö", "ms", "sn", "dr", "doç", "prof", "yrd", "av", "çev", "ed", "s", "c", "no", "tel",
    };

    /// <summary>[start, end) ranges of the sentences in <paramref name="text"/>, trimmed, in order.</summary>
    public static IReadOnlyList<(int Start, int End)> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sentences = new List<(int, int)>();
        var start = Skip(text, 0);
        var i = start;
        while (i < text.Length)
        {
            var c = text[i];
            var end = -1;
            if (c == '\n' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                end = i;   // paragraph break
            }
            else if (c is '.' or '!' or '?' or '…' && IsBoundary(text, i))
            {
                end = i + 1;
                while (end < text.Length && text[end] is '.' or '!' or '?' or '"' or '\'' or '”' or '’' or ')')
                {
                    end++;
                }
            }

            if (end > start)
            {
                Add(sentences, text, start, end);
                start = Skip(text, end);
                i = start;
                continue;
            }

            i++;
        }

        if (start < text.Length)
        {
            Add(sentences, text, start, text.Length);
        }

        return sentences;
    }

    private static bool IsBoundary(string text, int i)
    {
        // The next non-space character must start a new sentence.
        var j = i + 1;
        while (j < text.Length && text[j] is '.' or '!' or '?' or '"' or '\'' or '”' or '’' or ')')
        {
            j++;
        }

        if (j >= text.Length)
        {
            return true;
        }

        if (!char.IsWhiteSpace(text[j]))
        {
            return false;   // 3.14, e.g. inside a word, URLs
        }

        while (j < text.Length && char.IsWhiteSpace(text[j]))
        {
            j++;
        }

        if (j >= text.Length)
        {
            return true;
        }

        var next = text[j];
        if (!(char.IsUpper(next) || char.IsDigit(next) || next is '"' or '“' or '\'' or '(' or '['))
        {
            return false;
        }

        if (text[i] != '.')
        {
            return true;
        }

        // A period after an abbreviation or an initial does not end the sentence.
        var w = i - 1;
        while (w >= 0 && (char.IsLetter(text[w]) || text[w] == '.'))
        {
            w--;
        }

        var word = text[(w + 1)..i].TrimEnd('.');
        if (word.Length == 1 && char.IsLetter(word[0]) && char.IsUpper(word[0]))
        {
            return false;   // J. Smith
        }

        return !Abbreviations.Contains(word);
    }

    private static int Skip(string text, int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        return i;
    }

    private static void Add(List<(int, int)> sentences, string text, int start, int end)
    {
        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        if (end > start)
        {
            sentences.Add((start, end));
        }
    }
}
