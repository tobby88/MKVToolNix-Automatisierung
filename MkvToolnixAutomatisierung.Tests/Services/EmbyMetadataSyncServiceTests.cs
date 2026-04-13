using System.IO;
using MkvToolnixAutomatisierung.Services.Emby;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class EmbyMetadataSyncServiceTests
{
    [Fact]
    public void LoadNewOutputReport_ReadsOnlyMkvPaths()
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

            var paths = service.LoadNewOutputReport(reportPath);

            var path = Assert.Single(paths);
            Assert.EndsWith("Pilot.mkv", path, StringComparison.OrdinalIgnoreCase);
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
