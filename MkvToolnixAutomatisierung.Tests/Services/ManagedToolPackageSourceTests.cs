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
            <a href="https://mkvtoolnix.download/windows/releases/97.0/mkvtoolnix-64-bit-97.0.7z">old</a>
            <a href="https://mkvtoolnix.download/windows/releases/98.0/mkvtoolnix-64-bit-98.0.7z">new</a>
            </body></html>
            """;
        const string checksumText = """
            aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  mkvtoolnix-64-bit-97.0.7z
            bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  mkvtoolnix-64-bit-98.0.7z
            """;

        var package = MkvToolNixPackageSource.ParseLatestPackageFromDownloadsPage(downloadsHtml, checksumText);

        Assert.Equal(ManagedToolKind.MkvToolNix, package.Kind);
        Assert.Equal("98.0", package.VersionToken);
        Assert.Equal("98.0", package.DisplayVersion);
        Assert.Equal("mkvtoolnix-64-bit-98.0.7z", package.ArchiveFileName);
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", package.ExpectedSha256);
        Assert.Equal("https://mkvtoolnix.download/windows/releases/98.0/mkvtoolnix-64-bit-98.0.7z", package.DownloadUri.ToString());
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
}

