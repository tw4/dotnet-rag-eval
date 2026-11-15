using System.IO.Compression;
using System.Security.Cryptography;

namespace RagEval.Evaluation.Datasets;

/// <summary>Downloads a dataset's files into a data folder, verifying each SHA-256, and unpacks archives.</summary>
public sealed class DatasetDownloader(HttpClient http, string dataDirectory)
{
    public string DataDirectory { get; } = dataDirectory;

    /// <summary>Downloads missing or changed files; returns the number downloaded.</summary>
    public async Task<int> EnsureAsync(DatasetInfo dataset, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var downloaded = 0;
        foreach (var file in dataset.Files)
        {
            var target = Path.Combine(DataDirectory, file.Path);
            if (File.Exists(target) && await Sha256Async(target, cancellationToken) == file.Sha256)
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temporary = target + ".download";
            log?.Invoke($"downloading {file.Url}");
            using (var response = await http.GetAsync(new Uri(file.Url), HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var output = File.Create(temporary);
                await response.Content.CopyToAsync(output, cancellationToken);
            }

            var actual = await Sha256Async(temporary, cancellationToken);
            if (actual != file.Sha256)
            {
                File.Delete(temporary);
                throw new InvalidDataException(
                    $"Checksum mismatch for {file.Url}: expected {file.Sha256}, got {actual}. The source changed; refusing to use it.");
            }

            File.Move(temporary, target, overwrite: true);
            downloaded++;
        }

        if (dataset.Format == "beir-zip")
        {
            var folder = Path.Combine(DataDirectory, dataset.Id);
            if (downloaded > 0 || !Directory.Exists(folder))
            {
                // BEIR archives contain a top-level folder named after the dataset.
                await ZipFile.ExtractToDirectoryAsync(Path.Combine(DataDirectory, dataset.Files[0].Path), DataDirectory,
                    overwriteFiles: true, cancellationToken);
            }
        }

        return downloaded;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
