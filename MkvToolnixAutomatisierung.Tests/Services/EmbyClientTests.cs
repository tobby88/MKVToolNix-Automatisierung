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
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = request =>
            {
                capturedRequest = request;
                return JsonResponse(
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
                """);
            }
        });
        using var client = new EmbyClient(httpClient);

        var item = await client.FindItemByPathAsync(CreateSettings(), @"Z:\Videos\Serien\Pilot.mkv");

        Assert.NotNull(item);
        Assert.Equal("item-1", item!.Id);
        Assert.Equal("12345", item.GetProviderId("tvdb"));
        Assert.Equal("tt9876543", item.GetProviderId("IMDB"));
        var request = capturedRequest!;
        Assert.Equal("true", GetQueryParameter(request, "Recursive"));
        Assert.Equal(@"Z:\Videos\Serien\Pilot.mkv", GetQueryParameter(request, "Path"));
        Assert.Equal("Path,ProviderIds", GetQueryParameter(request, "Fields"));
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
    public async Task FindItemByPathAsync_ReturnsNull_WhenSingleReturnedItemPathDiffers()
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
                      "Path": "Z:\\Videos\\Serien\\Andere Folge.mkv"
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
    public async Task FindItemByPathAsync_AcceptsSlashOnlyDifferences()
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
                      "Path": "/mnt/raid/Videos/Serien/Pilot.mkv"
                    }
                  ]
                }
                """)
        });
        using var client = new EmbyClient(httpClient);

        var item = await client.FindItemByPathAsync(CreateSettings(), "/mnt/raid/Videos/Serien/Pilot.mkv");

        Assert.NotNull(item);
        Assert.Equal("item-1", item!.Id);
    }

    [Fact]
    public async Task FindItemByPathAsync_AcceptsEquivalentSmbPathReturnedForTranslatedPath()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = _ => JsonResponse(
                """
                {
                  "Items": [
                    {
                      "Id": "item-1",
                      "Name": "Wunschkind",
                      "Path": "smb://t-samba/raid/Videos/Serien/Der Alte/Season 55/Der Alte - S55E01 - Wunschkind.mkv"
                    }
                  ]
                }
                """)
        });
        using var client = new EmbyClient(httpClient);

        var item = await client.FindItemByPathAsync(
            CreateSettings(),
            "/mnt/raid/Videos/Serien/Der Alte/Season 55/Der Alte - S55E01 - Wunschkind.mkv");

        Assert.NotNull(item);
        Assert.Equal("item-1", item!.Id);
    }

    [Fact]
    public async Task FindItemByPathAsync_ReturnsNull_WhenOnlyFileNameMatches()
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
                      "Path": "smb://t-samba/other/Library/Pilot.mkv"
                    }
                  ]
                }
                """)
        });
        using var client = new EmbyClient(httpClient);

        var item = await client.FindItemByPathAsync(CreateSettings(), "/mnt/raid/Videos/Serien/Pilot.mkv");

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
    public async Task TriggerItemFileScanAsync_PostsRecursiveDefaultRefreshRequest()
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

        await client.TriggerItemFileScanAsync(CreateSettings(), "folder/1");

        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("/Items/folder%2F1/Refresh", capturedRequest.RequestUri!.AbsolutePath);
        Assert.Equal("true", GetQueryParameter(capturedRequest, "Recursive"));
        Assert.Equal("Default", GetQueryParameter(capturedRequest, "MetadataRefreshMode"));
        Assert.Equal("Default", GetQueryParameter(capturedRequest, "ImageRefreshMode"));
        Assert.Equal("false", GetQueryParameter(capturedRequest, "ReplaceAllMetadata"));
        Assert.Equal("false", GetQueryParameter(capturedRequest, "ReplaceAllImages"));
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
        Assert.Equal("false", GetQueryParameter(capturedRequest, "Recursive"));
        Assert.Equal("FullRefresh", GetQueryParameter(capturedRequest, "MetadataRefreshMode"));
    }

    [Fact]
    public async Task GetSystemInfoAsync_ThrowsFriendlyError_WhenApiKeyIsRejected()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("Access denied", Encoding.UTF8, "text/plain")
            }
        });
        using var client = new EmbyClient(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetSystemInfoAsync(CreateSettings()));

        Assert.Contains("Emby lehnt den API-Key ab.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Access denied", exception.Message, StringComparison.Ordinal);
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

    private static string? GetQueryParameter(HttpRequestMessage request, string name)
    {
        Assert.NotNull(request.RequestUri);
        var query = request.RequestUri!.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = part.IndexOf('=', StringComparison.Ordinal);
            var key = separatorIndex >= 0 ? part[..separatorIndex] : part;
            if (!string.Equals(Uri.UnescapeDataString(key), name, StringComparison.Ordinal))
            {
                continue;
            }

            var value = separatorIndex >= 0 ? part[(separatorIndex + 1)..] : string.Empty;
            return Uri.UnescapeDataString(value);
        }

        return null;
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
