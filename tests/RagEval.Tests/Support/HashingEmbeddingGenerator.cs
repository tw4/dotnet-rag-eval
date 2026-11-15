using Microsoft.Extensions.AI;

namespace RagEval.Tests.Support;

/// <summary>
/// A deterministic stand-in for an embedding model: hashed bag of lower-cased words, normalized,
/// so texts that share words are close. Counts its calls for batching tests.
/// </summary>
internal sealed class HashingEmbeddingGenerator(int dimensions = 64) : IEmbeddingGenerator<string, Embedding<float>>
{
    public List<int> BatchSizes { get; } = [];
    public List<string> Inputs { get; } = [];

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = values.ToList();
        BatchSizes.Add(list.Count);
        Inputs.AddRange(list);
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list.Select(v => new Embedding<float>(Embed(v)))));
    }

    public float[] Embed(string text)
    {
        var v = new float[dimensions];
        foreach (var word in text.ToLowerInvariant().Split((char[])[' ', '\n', '.', ',', ':', '?', '!'], StringSplitOptions.RemoveEmptyEntries))
        {
            v[(int)((uint)StableHash(word) % (uint)dimensions)] += 1;
        }

        var norm = MathF.Sqrt(v.Sum(x => x * x));
        if (norm > 0)
        {
            for (var i = 0; i < v.Length; i++)
            {
                v[i] /= norm;
            }
        }
        else
        {
            v[0] = 1;
        }

        return v;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            var h = (int)2166136261;
            foreach (var c in s)
            {
                h = (h ^ c) * 16777619;
            }

            return h;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
