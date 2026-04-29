using System.Net;
using System.Net.Http;
using System.Text;
using MkvToolnixAutomatisierung.Services.Metadata;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class ImdbLookupServiceTests
{
    [Fact]
    public async Task SearchSeriesAsync_FiltersToSeriesAndMiniSeries()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.Equal("https://api.imdbapi.dev/search/titles?query=Friends", request.RequestUri?.ToString());
            return CreateJsonResponse(
                """
                {
                  "titles": [
                    { "id": "tt0108778", "type": "tvSeries", "primaryTitle": "Friends", "originalTitle": "Friends", "startYear": 1994, "endYear": 2004 },
                    { "id": "tt1234567", "type": "movie", "primaryTitle": "Friends: The Movie", "originalTitle": "Friends: The Movie", "startYear": 2025, "endYear": null },
                    { "id": "tt7654321", "type": "tvMiniSeries", "primaryTitle": "Friends Revisited", "originalTitle": "Friends Revisited", "startYear": 2020, "endYear": 2020 }
                  ]
                }
                """);
        }));
        var service = new ImdbLookupService(httpClient);

        var results = await service.SearchSeriesAsync("Friends");

        Assert.Collection(
            results,
            first =>
            {
                Assert.Equal("tt0108778", first.Id);
                Assert.Equal("Friends", first.PrimaryTitle);
            },
            second =>
            {
                Assert.Equal("tt7654321", second.Id);
                Assert.Equal("Friends Revisited", second.PrimaryTitle);
            });
    }

    [Fact]
    public async Task LoadEpisodesAsync_LoadsAllSeasonsAndPages()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            return request.RequestUri?.ToString() switch
            {
                "https://api.imdbapi.dev/titles/tt0108778/seasons" => CreateJsonResponse(
                    """
                    {
                      "seasons": [
                        { "season": "2", "episodeCount": 1 },
                        { "season": "1", "episodeCount": 2 }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0108778/episodes?season=1" => CreateJsonResponse(
                    """
                    {
                      "episodes": [
                        { "id": "tt0000002", "title": "Episode 2", "season": "1", "episodeNumber": 2 }
                      ],
                      "nextPageToken": "page-2"
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0108778/episodes?season=1&pageToken=page-2" => CreateJsonResponse(
                    """
                    {
                      "episodes": [
                        { "id": "tt0000001", "title": "Episode 1", "season": "1", "episodeNumber": 1 }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0108778/episodes?season=2" => CreateJsonResponse(
                    """
                    {
                      "episodes": [
                        { "id": "tt0000010", "title": "Episode 10", "season": "2", "episodeNumber": 10 }
                      ]
                    }
                    """),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected URI: {request.RequestUri}")
            };
        }));
        var service = new ImdbLookupService(httpClient);

        var episodes = await service.LoadEpisodesAsync("tt0108778");

        Assert.Collection(
            episodes,
            first =>
            {
                Assert.Equal("tt0000001", first.Id);
                Assert.Equal("Episode 1", first.Title);
            },
            second =>
            {
                Assert.Equal("tt0000002", second.Id);
                Assert.Equal("Episode 2", second.Title);
            },
            third =>
            {
                Assert.Equal("tt0000010", third.Id);
                Assert.Equal("Episode 10", third.Title);
            });
    }

    [Fact]
    public async Task LoadEpisodesAsync_Stops_WhenPageTokenRepeats()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            return request.RequestUri?.ToString() switch
            {
                "https://api.imdbapi.dev/titles/tt0108778/seasons" => CreateJsonResponse(
                    """
                    {
                      "seasons": [
                        { "season": "1", "episodeCount": 2 }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0108778/episodes?season=1" => CreateJsonResponse(
                    """
                    {
                      "episodes": [
                        { "id": "tt0000001", "title": "Episode 1", "season": "1", "episodeNumber": 1 }
                      ],
                      "nextPageToken": "loop"
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0108778/episodes?season=1&pageToken=loop" => CreateJsonResponse(
                    """
                    {
                      "episodes": [
                        { "id": "tt0000002", "title": "Episode 2", "season": "1", "episodeNumber": 2 }
                      ],
                      "nextPageToken": "loop"
                    }
                    """),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected URI: {request.RequestUri}")
            };
        }));
        var service = new ImdbLookupService(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoadEpisodesAsync("tt0108778"));

        Assert.Contains("IMDb-Pagination", exception.Message, StringComparison.Ordinal);
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
