using System.Net;
using System.Net.Http;
using System.Text;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class ManagedToolPackageSourceTests
{
    [Fact]
    public void ParseLatestPackageFromDownloadsPage_PicksHighestPortableMkvToolNixVersionAndChecksum()
    {
        const string downloadsHtml = """
            <html><body>
            <tr>
              <td>Portable (64-bit)</td>
              <td><a href="windows/releases/97.0/mkvtoolnix-64-bit-97.0.7z">old</a></td>
              <td><img data-checksum="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"></td>
            </tr>
            <tr>
              <td>Portable (64-bit)</td>
              <td><a href="windows/releases/98.0/mkvtoolnix-64-bit-98.0.7z">new</a></td>
              <td><img data-checksum="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"></td>
            </tr>
            </body></html>
            """;

        var package = MkvToolNixPackageSource.ParseLatestPackageFromDownloadsPage(downloadsHtml);

        Assert.Equal(ManagedToolKind.MkvToolNix, package.Kind);
        Assert.Equal("98.0", package.VersionToken);
        Assert.Equal("98.0", package.DisplayVersion);
        Assert.Equal("mkvtoolnix-64-bit-98.0.7z", package.ArchiveFileName);
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", package.ExpectedSha256);
        Assert.Equal("https://mkvtoolnix.download/windows/releases/98.0/mkvtoolnix-64-bit-98.0.7z", package.DownloadUri.ToString());
    }

    [Fact]
    public void ParseLatestPackageFromDownloadsPage_FallsBackToChecksumListing_WhenInlineChecksumIsMissing()
    {
        const string downloadsHtml = """
            <html><body>
            <a href="windows/releases/98.0/mkvtoolnix-64-bit-98.0.7z">new</a>
            </body></html>
            """;
        const string checksumText = """
            cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  mkvtoolnix-64-bit-98.0.7z
            """;

        var package = MkvToolNixPackageSource.ParseLatestPackageFromDownloadsPage(downloadsHtml, checksumText);

        Assert.Equal("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", package.ExpectedSha256);
        Assert.Equal("https://mkvtoolnix.download/windows/releases/98.0/mkvtoolnix-64-bit-98.0.7z", package.DownloadUri.ToString());
    }

    [Fact]
    public void ParseLatestPackageFromDownloadsPage_ReadsInlineChecksumFromRelaxedMarkup()
    {
        const string downloadsHtml = """
            <html><body>
            <section class="download-card">
              <div class="label"><strong>Portable</strong> (64-bit)</div>
              <div class="actions">
                <a class="download-link" href="windows/releases/98.0/mkvtoolnix-64-bit-98.0.7z">download</a>
                <span class="checksum" data-checksum="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"></span>
              </div>
            </section>
            </body></html>
            """;

        var package = MkvToolNixPackageSource.ParseLatestPackageFromDownloadsPage(downloadsHtml);

        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", package.ExpectedSha256);
        Assert.Equal("98.0", package.VersionToken);
    }

    [Fact]
    public void ParseLatestPackageFromReleaseJson_PrefersDigestForFfprobe()
    {
        const string releaseJson = """
            {
              "name": "Latest Auto-Build (2026-04-18 13:04)",
              "published_at": "2026-04-18T13:04:00Z",
              "assets": [
                {
                  "name": "ffmpeg-master-latest-win64-gpl-shared.zip",
                  "browser_download_url": "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip",
                  "digest": "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                  "updated_at": "2026-04-18T13:04:00Z"
                },
                {
                  "name": "checksums.sha256",
                  "browser_download_url": "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/checksums.sha256"
                }
              ]
            }
            """;

        var package = FfprobePackageSource.ParseLatestPackageFromReleaseJson(releaseJson);

        Assert.Equal(ManagedToolKind.Ffprobe, package.Kind);
        Assert.Equal("Latest Auto-Build (2026-04-18 13:04)", package.DisplayVersion);
        Assert.Equal("2026-04-18T13-04-00Z", package.VersionToken);
        Assert.Equal("ffmpeg-master-latest-win64-gpl-shared.zip", package.ArchiveFileName);
        Assert.Equal("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", package.ExpectedSha256);
    }

    [Fact]
    public void ParseLatestPackageFromDownloadsPage_PicksHighestMediathekViewWindowsZipAndSha512()
    {
        const string downloadsHtml = """
            <html><body>
            <a href="/stabil/MediathekView-14.4.2-win.zip">old</a>
            <a href="/stabil/MediathekView-14.5.0-win-arm64.zip">arm</a>
            <a href="/stabil/MediathekView-14.5.0-win.zip">new</a>
            <a href="/stabil/MediathekView-latest-win.zip">latest</a>
            </body></html>
            """;
        var sha512 = new string('A', 128);

        var package = MediathekViewPackageSource.ParseLatestPackageFromDownloadsPage(downloadsHtml, sha512);

        Assert.Equal(ManagedToolKind.MediathekView, package.Kind);
        Assert.Equal("14.5.0", package.VersionToken);
        Assert.Equal("14.5.0", package.DisplayVersion);
        Assert.Equal("MediathekView-14.5.0-win.zip", package.ArchiveFileName);
        Assert.Equal(sha512, package.ExpectedSha512);
        Assert.Equal("https://download.mediathekview.de/stabil/MediathekView-14.5.0-win.zip", package.DownloadUri.ToString());
    }

    [Fact]
    public void ParseLatestPackageFromReleaseJson_FallsBackToChecksumFile()
    {
        const string releaseJson = """
            {
              "published_at": "2026-04-18T13:04:00Z",
              "assets": [
                {
                  "name": "ffmpeg-master-latest-win64-gpl-shared.zip",
                  "browser_download_url": "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip",
                  "updated_at": "2026-04-18T13:04:00Z"
                },
                {
                  "name": "checksums.sha256",
                  "browser_download_url": "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/checksums.sha256"
                }
              ]
            }
            """;
        const string checksumText = """
            dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd *ffmpeg-master-latest-win64-gpl-shared.zip
            """;

        var package = FfprobePackageSource.ParseLatestPackageFromReleaseJson(releaseJson, checksumText);

        Assert.Equal("dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd", package.ExpectedSha256);
    }

    [Fact]
    public void TryReadSha256FromChecksumText_IgnoresInvalidHashTokens()
    {
        const string checksumText = """
            xyz *ffmpeg-master-latest-win64-gpl-shared.zip
            """;

        var checksum = ManagedToolParsing.TryReadSha256FromChecksumText(
            checksumText,
            "ffmpeg-master-latest-win64-gpl-shared.zip");

        Assert.Null(checksum);
        Assert.True(ManagedToolParsing.IsValidSha256(new string('a', 64)));
        Assert.False(ManagedToolParsing.IsValidSha256("xyz"));
        Assert.True(ManagedToolParsing.IsValidSha512(new string('a', 128)));
        Assert.False(ManagedToolParsing.IsValidSha512("xyz"));
    }

    [Fact]
    public async Task PackageSources_SendAcceptHeadersForEndpointType()
    {
        var requests = new List<(Uri Uri, string Accept)>();
        using var httpClient = new HttpClient(new RecordingHttpMessageHandler(request =>
        {
            requests.Add((request.RequestUri!, string.Join(", ", request.Headers.Accept.Select(header => header.ToString()))));
            return request.RequestUri!.ToString() switch
            {
                "https://mkvtoolnix.download/downloads.html" => TextResponse(
                    """
                    <html><body>
                    <a href="windows/releases/98.0/mkvtoolnix-64-bit-98.0.7z">new</a>
                    </body></html>
                    """,
                    "text/html"),
                "https://mkvtoolnix.download/windows/releases/98.0/sha256sums.txt" => TextResponse(
                    "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee  mkvtoolnix-64-bit-98.0.7z",
                    "text/plain"),
                "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest" => TextResponse(
                    """
                    {
                      "published_at": "2026-04-18T13:04:00Z",
                      "assets": [
                        {
                          "name": "ffmpeg-master-latest-win64-gpl-shared.zip",
                          "browser_download_url": "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip",
                          "digest": "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                          "updated_at": "2026-04-18T13:04:00Z"
                        }
                      ]
                    }
                    """,
                    "application/json"),
                "https://download.mediathekview.de/stabil/" => TextResponse(
                    """
                    <html><body>
                    <a href="/stabil/MediathekView-14.5.0-win.zip">new</a>
                    </body></html>
                    """,
                    "text/html"),
                "https://download.mediathekview.de/stabil/MediathekView-14.5.0-win.zip.SHA-512" => TextResponse(new string('f', 128), "text/plain"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }));

        await new MkvToolNixPackageSource(httpClient).GetLatestPackageAsync();
        await new FfprobePackageSource(httpClient).GetLatestPackageAsync();
        await new MediathekViewPackageSource(httpClient).GetLatestPackageAsync();

        Assert.Contains(requests, request => request.Uri == new Uri("https://mkvtoolnix.download/downloads.html")
                                            && request.Accept.Contains("text/html", StringComparison.Ordinal));
        Assert.Contains(requests, request => request.Uri == new Uri("https://mkvtoolnix.download/windows/releases/98.0/sha256sums.txt")
                                            && request.Accept.Contains("text/plain", StringComparison.Ordinal));
        Assert.Contains(requests, request => request.Uri == new Uri("https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest")
                                            && request.Accept.Contains("application/vnd.github+json", StringComparison.Ordinal));
        Assert.Contains(requests, request => request.Uri == new Uri("https://download.mediathekview.de/stabil/")
                                            && request.Accept.Contains("text/html", StringComparison.Ordinal));
        Assert.Contains(requests, request => request.Uri == new Uri("https://download.mediathekview.de/stabil/MediathekView-14.5.0-win.zip.SHA-512")
                                            && request.Accept.Contains("text/plain", StringComparison.Ordinal));
    }

    private static HttpResponseMessage TextResponse(string text, string mediaType)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(text, Encoding.UTF8, mediaType)
        };
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
