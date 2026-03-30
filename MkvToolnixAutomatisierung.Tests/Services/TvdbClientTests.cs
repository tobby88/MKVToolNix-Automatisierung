using System.Net;
using System.Net.Http;
using System.Text;
using MkvToolnixAutomatisierung.Services.Metadata;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class TvdbClientTests
{
    [Fact]
    public async Task SearchSeriesAsync_UsesInjectedHttpClient_WithoutBaseAddress()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = request => request.RequestUri!.AbsolutePath switch
            {
                "/v4/login" => JsonResponse("""{"data":{"token":"token-123"}}"""),
                "/v4/search" => JsonResponse("""{"data":[{"tvdb_id":42,"name":"Beispielserie","year":"2024"}]}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            }
        });
        using var client = new TvdbClient(httpClient);

        var results = await client.SearchSeriesAsync("key", pin: null, "Beispielserie");

        var result = Assert.Single(results);
        Assert.Equal(42, result.Id);
        Assert.Equal("Beispielserie", result.Name);
    }

    [Fact]
    public void Dispose_DoesNotDispose_InjectedHttpClient()
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var client = new TvdbClient(httpClient);

        client.Dispose();

        Assert.False(handler.IsDisposed);

        httpClient.Dispose();
        Assert.True(handler.IsDisposed);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; init; }

        public bool IsDisposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = Responder?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK);
            return Task.FromResult(response);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
