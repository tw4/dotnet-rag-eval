using System.Text.Json;
using System.Text.Json.Serialization;

namespace RagEval.Evaluation.Datasets;

/// <summary>One file of a dataset, pinned by checksum.</summary>
public sealed record DatasetFile(string Url, string Path, string Sha256);

/// <summary>Counts from the manifest, checked after loading.</summary>
public sealed record DatasetSize(int Documents, int Queries, int Judgements);

/// <summary>A dataset entry of <c>datasets/datasets.json</c>.</summary>
public sealed record DatasetInfo(string Id, string Name, string Language, string Task, string License, string LicenseSource,
    string Citation, string Format, string Split, IReadOnlyList<DatasetFile> Files, DatasetSize Size, string? Revision = null);

/// <summary>The dataset manifest (<c>datasets/datasets.json</c>).</summary>
public sealed class DatasetManifest
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [JsonPropertyName("datasets")]
    public IReadOnlyList<DatasetInfo> Datasets { get; init; } = [];

    public DatasetInfo Get(string id) => Datasets.FirstOrDefault(d => d.Id == id)
        ?? throw new KeyNotFoundException($"Unknown dataset '{id}'. Known: {string.Join(", ", Datasets.Select(d => d.Id))}.");

    public static DatasetManifest Load(string path) =>
        JsonSerializer.Deserialize<DatasetManifest>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"Cannot read the dataset manifest {path}.");

    /// <summary>Finds <c>datasets/datasets.json</c> in the current directory or a parent.</summary>
    public static string FindDefault(string? start = null)
    {
        for (var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "datasets", "datasets.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("datasets/datasets.json not found here or in a parent folder; run from the repository or pass --manifest.");
    }
}
