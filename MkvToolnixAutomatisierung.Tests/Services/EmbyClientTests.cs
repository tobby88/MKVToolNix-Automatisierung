using System.Net;
using System.Net.Http;
using System.Text;
using MkvToolnixAutomatisierung.Services.Emby;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class EmbyClientTests
{
    [Fact]
    public async Task GetSystemInfoAsync_UsesEmbyTokenHeader()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = request =>
            {
                capturedRequest = request;
                return JsonResponse("""{"ServerName":"Test-Emby","Version":"4.8.0","Id":"server-1"}""");
            }
        });
        using var client = new EmbyClient(httpClient);

        var result = await client.GetSystemInfoAsync(CreateSettings());

        Assert.Equal("Test-Emby", result.ServerName);
        Assert.Equal("4.8.0", result.Version);
        Assert.Equal("token-123", capturedRequest!.Headers.GetValues("X-Emby-Token").Single());
        Assert.Equal("http://emby.local:8096/System/Info", capturedRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task FindItemByPathAsync_ParsesProviderIds()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = _ => JsonResponse(
                """
                {
                  "Items": [
                    {
                      "Id": "item-1",
                      "Name": "Pilot",
                      "Path": "Z:\\Videos\\Serien\\Pilot.mkv",
                      "ProviderIds": {
                        "Tvdb": "12345",
                        "Imdb": "tt9876543"
                      }
                    }
                  ],
                  "TotalRecordCount": 1
                }
                """)
        });
        using var client = new EmbyClient(httpClient);

        var item = await client.FindItemByPathAsync(CreateSettings(), @"Z:\Videos\Serien\Pilot.mkv");

        Assert.NotNull(item);
        Assert.Equal("item-1", item!.Id);
        Assert.Equal("12345", item.GetProviderId("tvdb"));
        Assert.Equal("tt9876543", item.GetProviderId("IMDB"));
    }

    [Fact]
    public async Task FindItemByPathAsync_ReturnsNull_WhenMultipleNonExactMatchesAreReturned()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = _ => JsonResponse(
                """
                {
                  "Items": [
                    {
                      "Id": "item-1",
                      "Name": "Pilot",
                      "Path": "Z:\\Videos\\Serien\\Staffel 1\\Pilot.mkv"
                    },
                    {
                      "Id": "item-2",
                      "Name": "Pilot",
                      "Path": "Z:\\Videos\\Serien\\Staffel 2\\Pilot.mkv"
                    }
                  ]
                }
                """)
        });
        using var client = new EmbyClient(httpClient);

        var item = await client.FindItemByPathAsync(CreateSettings(), @"Z:\Videos\Serien\Pilot.mkv");

        Assert.Null(item);
    }

    [Fact]
    public async Task GetLibrariesAsync_ParsesLocationsAndRefreshStatus()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = _ => JsonResponse(
                """
                {
                  "Items": [
                    {
                      "ItemId": "library-1",
                      "Name": "Serien",
                      "Locations": [
                        "Z:\\Videos\\Serien"
                      ],
                      "RefreshProgress": 42.4,
                      "RefreshStatus": "Running"
                    }
                  ]
                }
                """)
        });
        using var client = new EmbyClient(httpClient);

        var libraries = await client.GetLibrariesAsync(CreateSettings());

        var library = Assert.Single(libraries);
        Assert.Equal("library-1", library.Id);
        Assert.Equal("Serien", library.Name);
        Assert.Equal(@"Z:\Videos\Serien", Assert.Single(library.Locations));
        Assert.Equal(42.4, library.RefreshProgress);
        Assert.Equal("Running", library.RefreshStatus);
    }

    [Fact]
    public async Task TriggerLibraryScanAsync_PostsRefreshEndpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = request =>
            {
                capturedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        });
        using var client = new EmbyClient(httpClient);

        await client.TriggerLibraryScanAsync(CreateSettings());

        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("http://emby.local:8096/Library/Refresh", capturedRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task RefreshItemMetadataAsync_PostsFullRefreshRequest()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = request =>
            {
                capturedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        });
        using var client = new EmbyClient(httpClient);

        await client.RefreshItemMetadataAsync(CreateSettings(), "item-1");

        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Contains("/Items/item-1/Refresh?", capturedRequest.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Contains("MetadataRefreshMode=FullRefresh", capturedRequest.RequestUri!.Query, StringComparison.Ordinal);
    }

    private static AppEmbySettings CreateSettings()
    {
        return new AppEmbySettings
        {
            ServerUrl = "http://emby.local:8096",
            ApiKey = "token-123"
        };
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Responder?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
