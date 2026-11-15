using System.Net.Sockets;
using Qdrant.Client;

namespace RagEval.Tests.Support;

/// <summary>
/// A Qdrant at localhost:6334 (docker compose up -d qdrant). Tests that need it are skipped when it
/// is not running. Each test gets its own collection, deleted afterwards.
/// </summary>
internal static class QdrantFixture
{
    public static readonly Uri GrpcUrl = new(Environment.GetEnvironmentVariable("RAGEVAL_TEST_QDRANT") ?? "http://localhost:6334");

    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            using var tcp = new TcpClient();
            return tcp.ConnectAsync(GrpcUrl.Host, GrpcUrl.Port).Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    });

    public static QdrantClient ClientOrSkip()
    {
        Assert.SkipUnless(Available.Value, $"Qdrant is not running at {GrpcUrl} (docker compose up -d qdrant).");
        return new QdrantClient(GrpcUrl);
    }

    public static string NewCollection() => "test-" + Guid.NewGuid().ToString("N")[..12];
}
