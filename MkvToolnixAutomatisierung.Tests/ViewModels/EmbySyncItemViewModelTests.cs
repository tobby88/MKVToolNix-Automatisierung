using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class EmbySyncItemViewModelTests
{
    [Fact]
    public void ApplyAnalysis_PrefersReportedTvdbId_OverDifferentNfoAndEmbyIds()
    {
        var vm = new EmbySyncItemViewModel(
            @"C:\Videos\Serie - S01E01 - Pilot.mkv",
            new EmbyProviderIds("100", null));
        var analysis = new EmbyFileAnalysis(
            vm.MediaFilePath,
            @"C:\Videos\Serie - S01E01 - Pilot.nfo",
            MediaFileExists: true,
            NfoExists: true,
            NfoProviderIds: new EmbyProviderIds("200", null),
            EmbyItem: new EmbyItem(
                "emby-1",
                "Pilot",
                vm.MediaFilePath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Tvdb"] = "300"
                }),
            WarningMessage: null);

        vm.ApplyAnalysis(analysis);

        Assert.Equal("100", vm.TvdbId);
        Assert.Equal("Bereit", vm.StatusText);
        Assert.Contains("JSON-Report liefert TVDB-ID 100", vm.Note, StringComparison.Ordinal);
        Assert.Contains("NFO: 200", vm.Note, StringComparison.Ordinal);
        Assert.Contains("Emby: 300", vm.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyEmbyItem_DoesNotOverwriteReportedTvdbId()
    {
        var vm = new EmbySyncItemViewModel(
            @"C:\Videos\Serie - S01E01 - Pilot.mkv",
            new EmbyProviderIds("100", null));
        var item = new EmbyItem(
            "emby-1",
            "Pilot",
            vm.MediaFilePath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tvdb"] = "300",
                ["Imdb"] = "tt9876543"
            });

        vm.ApplyEmbyItem(item);

        Assert.Equal("100", vm.TvdbId);
        Assert.Equal("tt9876543", vm.ImdbId);
        Assert.Equal("Bereit", vm.StatusText);
        Assert.Contains("JSON-Report liefert TVDB-ID 100", vm.Note, StringComparison.Ordinal);
        Assert.Contains("Emby: 300", vm.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyAnalysis_UsesNfoAndEmbyIds_WhenReportContainsNoProviderIds()
    {
        var vm = new EmbySyncItemViewModel(@"C:\Videos\Serie - S01E01 - Pilot.mkv", EmbyProviderIds.Empty);
        var analysis = new EmbyFileAnalysis(
            vm.MediaFilePath,
            @"C:\Videos\Serie - S01E01 - Pilot.nfo",
            MediaFileExists: true,
            NfoExists: true,
            NfoProviderIds: new EmbyProviderIds("200", null),
            EmbyItem: new EmbyItem(
                "emby-1",
                "Pilot",
                vm.MediaFilePath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Imdb"] = "tt9876543"
                }),
            WarningMessage: null);

        vm.ApplyAnalysis(analysis);

        Assert.Equal("200", vm.TvdbId);
        Assert.Equal("tt9876543", vm.ImdbId);
        Assert.Equal("Bereit", vm.StatusText);
        Assert.Equal("Emby-Item gefunden: Pilot", vm.Note);
    }
}
