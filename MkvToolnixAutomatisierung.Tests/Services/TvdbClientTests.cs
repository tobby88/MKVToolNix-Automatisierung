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
    public async Task SearchSeriesAsync_ReauthenticatesOnce_WhenSearchRequestReturnsUnauthorized()
    {
        var requests = new List<(string Path, string? Token)>();
        var loginCount = 0;
        var searchCount = 0;

        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = request =>
            {
                requests.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter));

                return request.RequestUri!.AbsolutePath switch
                {
                    "/v4/login" => JsonResponse($@"{{""data"":{{""token"":""token-{++loginCount}""}}}}"),
                    "/v4/search" when ++searchCount == 1 => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                    "/v4/search" => JsonResponse("""{"data":[{"tvdb_id":42,"name":"Beispielserie"}]}"""),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                };
            }
        });
        using var client = new TvdbClient(httpClient);

        var results = await client.SearchSeriesAsync("key", pin: null, "Beispielserie");

        Assert.Single(results);
        Assert.Equal(2, loginCount);
        Assert.Equal(2, searchCount);
        Assert.Collection(
            requests,
            entry =>
            {
                Assert.Equal("/v4/login", entry.Path);
                Assert.Null(entry.Token);
            },
            entry =>
            {
                Assert.Equal("/v4/search", entry.Path);
                Assert.Equal("token-1", entry.Token);
            },
            entry =>
            {
                Assert.Equal("/v4/login", entry.Path);
                Assert.Null(entry.Token);
            },
            entry =>
            {
                Assert.Equal("/v4/search", entry.Path);
                Assert.Equal("token-2", entry.Token);
            });
    }

    [Fact]
    public async Task GetSeriesEpisodesAsync_LoadsAllPages_InOrder()
    {
        var requests = new List<string>();

        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = request =>
            {
                requests.Add(request.RequestUri!.PathAndQuery);

                return request.RequestUri!.PathAndQuery switch
                {
                    "/v4/login" => JsonResponse("""{"data":{"token":"token-123"}}"""),
                    "/v4/series/42/episodes/default?page=0" => JsonResponse(
                        """{"data":{"episodes":[{"id":100,"name":"Pilot","seasonNumber":1,"number":1,"aired":"2024-01-01"}]},"links":{"next":"page-1"}}"""),
                    "/v4/series/42/episodes/default?page=1" => JsonResponse(
                        """{"data":{"episodes":[{"id":101,"name":"Finale","seasonNumber":"1","episodeNumber":"2","aired":"2024-01-08"}]},"links":{"next":null}}"""),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                };
            }
        });
        using var client = new TvdbClient(httpClient);

        var episodes = await client.GetSeriesEpisodesAsync("key", pin: null, 42);

        Assert.Collection(
            episodes,
            episode =>
            {
                Assert.Equal(100, episode.Id);
                Assert.Equal("Pilot", episode.Name);
                Assert.Equal(1, episode.SeasonNumber);
                Assert.Equal(1, episode.EpisodeNumber);
            },
            episode =>
            {
                Assert.Equal(101, episode.Id);
                Assert.Equal("Finale", episode.Name);
                Assert.Equal(1, episode.SeasonNumber);
                Assert.Equal(2, episode.EpisodeNumber);
            });
        Assert.Equal(
            ["/v4/login", "/v4/series/42/episodes/default?page=0", "/v4/series/42/episodes/default?page=1"],
            requests);
    }

    [Fact]
    public async Task GetSeriesEpisodesAsync_Stops_WhenTvdbKeepsReturningNextPages()
    {
        var requests = new List<string>();

        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = request =>
            {
                requests.Add(request.RequestUri!.PathAndQuery);

                return request.RequestUri!.PathAndQuery switch
                {
                    "/v4/login" => JsonResponse("""{"data":{"token":"token-123"}}"""),
                    var path when path.StartsWith("/v4/series/42/episodes/default?page=", StringComparison.Ordinal) => JsonResponse(
                        """{"data":{"episodes":[]},"links":{"next":"still-more"}}"""),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                };
            }
        });
        using var client = new TvdbClient(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetSeriesEpisodesAsync("key", pin: null, 42));

        Assert.Contains("TVDB-Pagination", exception.Message, StringComparison.Ordinal);
        Assert.Equal(101, requests.Count);
    }

    [Fact]
    public async Task GetSeriesEpisodesAsync_ReauthenticatesOnce_WhenEpisodeRequestReturnsUnauthorized()
    {
        var requests = new List<(string PathAndQuery, string? Token)>();
        var loginCount = 0;
        var episodeRequestCount = 0;

        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = request =>
            {
                requests.Add((request.RequestUri!.PathAndQuery, request.Headers.Authorization?.Parameter));

                return request.RequestUri!.PathAndQuery switch
                {
                    "/v4/login" => JsonResponse($@"{{""data"":{{""token"":""token-{++loginCount}""}}}}"),
                    "/v4/series/42/episodes/default?page=0" when ++episodeRequestCount == 1 => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                    "/v4/series/42/episodes/default?page=0" => JsonResponse(
                        """{"data":{"episodes":[{"id":100,"name":"Pilot","seasonNumber":1,"number":1,"aired":"2024-01-01"}]},"links":{"next":null}}"""),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                };
            }
        });
        using var client = new TvdbClient(httpClient);

        var episodes = await client.GetSeriesEpisodesAsync("key", pin: null, 42);

        Assert.Single(episodes);
        Assert.Equal(2, loginCount);
        Assert.Equal(2, episodeRequestCount);
        Assert.Collection(
            requests,
            entry =>
            {
                Assert.Equal("/v4/login", entry.PathAndQuery);
                Assert.Null(entry.Token);
            },
            entry =>
            {
                Assert.Equal("/v4/series/42/episodes/default?page=0", entry.PathAndQuery);
                Assert.Equal("token-1", entry.Token);
            },
            entry =>
            {
                Assert.Equal("/v4/login", entry.PathAndQuery);
                Assert.Null(entry.Token);
            },
            entry =>
            {
                Assert.Equal("/v4/series/42/episodes/default?page=0", entry.PathAndQuery);
                Assert.Equal("token-2", entry.Token);
            });
    }

    [Fact]
    public async Task GetSeriesEpisodesAsync_MergesNeutralFallbackEpisodes_WhenLocalizedResponseIsIncomplete()
    {
        var requests = new List<string>();

        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = request =>
            {
                requests.Add(request.RequestUri!.PathAndQuery);

                return request.RequestUri!.PathAndQuery switch
                {
                    "/v4/login" => JsonResponse("""{"data":{"token":"token-123"}}"""),
                    "/v4/series/42/episodes/default/deu?page=0" => JsonResponse(
                        """{"data":{"episodes":[{"id":100,"name":"Pilot (DE)","seasonNumber":1,"number":1,"aired":"2024-01-01"}]},"links":{"next":null}}"""),
                    "/v4/series/42/episodes/default?page=0" => JsonResponse(
                        """{"data":{"episodes":[{"id":100,"name":"Pilot","seasonNumber":1,"number":1,"aired":"2024-01-01"},{"id":101,"name":"Finale","seasonNumber":1,"number":2,"aired":"2024-01-08"}]},"links":{"next":null}}"""),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                };
            }
        });
        using var client = new TvdbClient(httpClient);

        var episodes = await client.GetSeriesEpisodesAsync("key", pin: null, 42, language: "deu");

        Assert.Collection(
            episodes,
            episode =>
            {
                Assert.Equal(100, episode.Id);
                Assert.Equal("Pilot (DE)", episode.Name);
            },
            episode =>
            {
                Assert.Equal(101, episode.Id);
                Assert.Equal("Finale", episode.Name);
            });
        Assert.Equal(
            ["/v4/login", "/v4/series/42/episodes/default/deu?page=0", "/v4/series/42/episodes/default?page=0"],
            requests);
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
