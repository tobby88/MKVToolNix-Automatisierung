using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;
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
        Assert.Contains("TVDB-ID 100 vorgemerkt", vm.Note, StringComparison.Ordinal);
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
        Assert.Contains("TVDB-ID 100 vorgemerkt", vm.Note, StringComparison.Ordinal);
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

    [Fact]
    public void ApplyAnalysis_KeepsManualTvdbOverride_OverLaterNfoAndEmbyData()
    {
        var vm = new EmbySyncItemViewModel(
            @"C:\Videos\Serie - S01E01 - Pilot.mkv",
            new EmbyProviderIds("100", null));
        vm.ApplyTvdbSelection(new TvdbEpisodeSelection(1, "Serie", 555, "Pilot", "01", "01"));
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

        Assert.Equal("555", vm.TvdbId);
        Assert.Contains("TVDB-ID 555 vorgemerkt", vm.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildMetadataGuess_ReadsStandardizedEpisodeFileName()
    {
        var vm = new EmbySyncItemViewModel(@"C:\Videos\Serie - S2014E05-E06 - Rififi.mkv", EmbyProviderIds.Empty);

        var success = vm.TryBuildMetadataGuess(out var guess);

        Assert.True(success);
        Assert.NotNull(guess);
        Assert.Equal("Serie", guess!.SeriesName);
        Assert.Equal("Rififi", guess.EpisodeTitle);
        Assert.Equal("2014", guess.SeasonNumber);
        Assert.Equal("05-E06", guess.EpisodeNumber);
    }

    [Fact]
    public void ApplyTvdbSelection_UpdatesTvdbIdAndNote()
    {
        var vm = new EmbySyncItemViewModel(@"C:\Videos\Serie - S01E01 - Pilot.mkv", EmbyProviderIds.Empty);

        vm.ApplyTvdbSelection(new TvdbEpisodeSelection(1, "Serie", 12345, "Pilot", "01", "01"));

        Assert.Equal("12345", vm.TvdbId);
        Assert.Equal("TVDB gewählt", vm.StatusText);
        Assert.Contains("TVDB manuell gewählt", vm.Note, StringComparison.Ordinal);
    }
}
