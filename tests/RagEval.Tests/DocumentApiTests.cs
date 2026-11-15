using System.Net;
using System.Net.Http.Json;
using System.Text;
using RagEval.Tests.Support;

namespace RagEval.Tests;

public class DocumentApiTests
{
    private static MultipartFormDataContent Upload(string fileName, string content)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(content, Encoding.UTF8), "file", fileName);
        return form;
    }

    [Fact]
    public async Task Uploaded_documents_are_indexed()
    {
        using var qdrant = QdrantFixture.ClientOrSkip();
        var collection = QdrantFixture.NewCollection();
        try
        {
            await using var factory = new ApiFactory(collection);
            using var client = factory.CreateClient();
            var ct = TestContext.Current.CancellationToken;

            using var md = Upload("guide.md", "# Guide\n\nInstall with docker compose up.\n");
            var response = await client.PostAsync(new Uri("/documents?id=docs/guide.md", UriKind.Relative), md, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(ct);
            Assert.Equal("docs/guide.md", body?["id"]?.ToString());
            Assert.Equal("Guide", body?["title"]?.ToString());
            Assert.Equal(1UL, await qdrant.CountAsync(collection, cancellationToken: ct));

            using var bin = Upload("data.bin", "nope");
            var rejected = await client.PostAsync(new Uri("/documents", UriKind.Relative), bin, ct);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, rejected.StatusCode);
        }
        finally
        {
            await qdrant.DeleteCollectionAsync(collection, cancellationToken: CancellationToken.None);
        }
    }

    [Fact]
    public async Task Ask_answers_with_citations_or_refuses()
    {
        using var qdrant = QdrantFixture.ClientOrSkip();
        var collection = QdrantFixture.NewCollection();
        try
        {
            await using var factory = new ApiFactory(collection);
            using var client = factory.CreateClient();
            var ct = TestContext.Current.CancellationToken;
            using var md = Upload("guide.md", "# Guide\n\nInstall the gateway with docker compose up.\n");
            (await client.PostAsync(new Uri("/documents", UriKind.Relative), md, ct)).EnsureSuccessStatusCode();

            var answered = await client.PostAsJsonAsync(new Uri("/ask", UriKind.Relative), new { question = "How do I install the gateway?" }, ct);
            var body = await answered.Content.ReadAsStringAsync(ct);
            Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
            Assert.Contains("\"refused\":false", body, StringComparison.Ordinal);
            Assert.Contains("\"documentId\":\"guide.md\"", body, StringComparison.Ordinal);
            Assert.Contains("\"startLine\":1,\"endLine\":3", body, StringComparison.Ordinal);

            var refused = await client.PostAsJsonAsync(new Uri("/ask", UriKind.Relative), new { question = "Peru capital city" }, ct);
            Assert.Contains("\"refused\":true", await refused.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);

            var empty = await client.PostAsJsonAsync(new Uri("/ask", UriKind.Relative), new { question = " " }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        }
        finally
        {
            await qdrant.DeleteCollectionAsync(collection, cancellationToken: CancellationToken.None);
        }
    }
}
