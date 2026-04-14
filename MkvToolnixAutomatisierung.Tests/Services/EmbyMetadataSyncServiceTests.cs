using System.IO;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class EmbyMetadataSyncServiceTests
{
    [Fact]
    public void LoadNewOutputReport_RejectsLegacyTextLists()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var reportPath = Path.Combine(directory, "Neu erzeugte Ausgabedateien.txt");
            File.WriteAllLines(reportPath,
            [
                "Neu erzeugte Ausgabedateien",
                "Erstellt am: 13.04.2026 10:00:00",
                @"Z:\Videos\Serien\Serie\Season 01\Serie - S01E01 - Pilot.mkv",
                @"Z:\Videos\Serien\Serie\Season 01\Serie - S01E01 - Pilot.mkv",
                @"Z:\Videos\Serien\Serie\Season 01\Serie - S01E01 - Pilot.nfo"
            ]);

            var service = new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService());

            var ex = Assert.Throws<InvalidDataException>(() => service.LoadNewOutputReport(reportPath));
            Assert.Contains("JSON", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadNewOutputReport_ReadsStructuredMetadataReportProviderIds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var mediaPath = Path.Combine(directory, "Serie - S01E02 - Pilot.mkv");
            var reportPath = Path.Combine(directory, "Neu erzeugte Ausgabedateien.metadata.json");
            File.WriteAllText(
                reportPath,
                BatchOutputMetadataReportJson.Serialize(new BatchOutputMetadataReport
                {
                    CreatedAt = DateTimeOffset.Now,
                    SourceDirectory = directory,
                    OutputDirectory = directory,
                    Items =
                    [
                        new BatchOutputMetadataEntry
                        {
                            OutputPath = mediaPath,
                            TvdbEpisodeId = "100",
                            ProviderIds = new BatchOutputProviderIds
                            {
                                Tvdb = "100",
                                Imdb = "tt1234567"
                            },
                            Tvdb = new BatchOutputTvdbMetadata
                            {
                                SeriesId = 42,
                                SeriesName = "Serie",
                                EpisodeId = 100
                            }
                        }
                    ]
                }));

            var service = new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService());

            var entries = service.LoadNewOutputReport(reportPath);

            var entry = Assert.Single(entries);
            Assert.Equal(mediaPath, entry.MediaFilePath);
            Assert.Equal("100", entry.ProviderIds.TvdbId);
            Assert.Equal("tt1234567", entry.ProviderIds.ImdbId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ThrowingEmbyClient : IEmbyClient
    {
        public Task<EmbyServerInfo> GetSystemInfoAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task TriggerLibraryScanAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<EmbyItem?> FindItemByPathAsync(AppEmbySettings settings, string mediaFilePath, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task RefreshItemMetadataAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }
}
