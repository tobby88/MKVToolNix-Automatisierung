using System.IO;
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
        Assert.Equal("IDs fehlen", vm.StatusText);
        Assert.Equal("Warning", vm.StatusTone);
        Assert.Contains("TVDB 100 vorgesehen", vm.Note, StringComparison.Ordinal);
        Assert.Contains("NFO: 200", vm.Note, StringComparison.Ordinal);
        Assert.Contains("Emby: 300", vm.Note, StringComparison.Ordinal);
        Assert.Contains("IMDB-ID fehlt", vm.Note, StringComparison.Ordinal);
        Assert.Contains("Emby-ID: emby-1", vm.StatusTooltip, StringComparison.Ordinal);
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
        Assert.Contains("TVDB 100 vorgesehen", vm.Note, StringComparison.Ordinal);
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
        Assert.Equal("IDs vorhanden.", vm.Note);
    }

    [Fact]
    public void ApplyAnalysis_WithOnlyTvdbId_StaysInWarningStateUntilImdbExists()
    {
        var vm = new EmbySyncItemViewModel(@"C:\Videos\Serie - S01E01 - Pilot.mkv", new EmbyProviderIds("200", null));
        var analysis = new EmbyFileAnalysis(
            vm.MediaFilePath,
            @"C:\Videos\Serie - S01E01 - Pilot.nfo",
            MediaFileExists: true,
            NfoExists: true,
            NfoProviderIds: new EmbyProviderIds("200", null),
            EmbyItem: null,
            WarningMessage: null);

        vm.ApplyAnalysis(analysis);

        Assert.Equal("IDs fehlen", vm.StatusText);
        Assert.Equal("Warning", vm.StatusTone);
        Assert.Equal("IMDB-ID fehlt.", vm.Note);
    }

    [Fact]
    public void ApplyAnalysis_WithEmbyItemButWithoutNfo_DisablesProviderIdSync()
    {
        var vm = new EmbySyncItemViewModel(@"C:\Videos\Serie - S00E01 - Trailer.mkv", EmbyProviderIds.Empty);
        var analysis = new EmbyFileAnalysis(
            vm.MediaFilePath,
            @"C:\Videos\Serie - S00E01 - Trailer.nfo",
            MediaFileExists: true,
            NfoExists: false,
            NfoProviderIds: EmbyProviderIds.Empty,
            EmbyItem: new EmbyItem(
                "emby-trailer",
                "Trailer",
                vm.MediaFilePath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            WarningMessage: null);

        vm.ApplyAnalysis(analysis);

        Assert.False(vm.SupportsProviderIdSync);
        Assert.False(vm.CanEditProviderIds);
        Assert.False(vm.CanReviewTvdb);
        Assert.Equal("Ohne NFO-Sync", vm.StatusText);
        Assert.Equal("Neutral", vm.StatusTone);
        Assert.Equal("Emby-Asset ohne Episoden-NFO.", vm.Note);
        Assert.Contains("nicht anwendbar", vm.ProviderIdEditTooltip, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\Videos\Serie\trailers\Serie - S00E01 - Trailer.mkv")]
    [InlineData(@"C:\Videos\Serie\backdrops\Serie - S00E02 - Hintergrundbild.mkv")]
    public void ApplyAnalysis_RecognizesEmbyAssetFoldersWithoutScan(string mediaFilePath)
    {
        var vm = new EmbySyncItemViewModel(mediaFilePath, EmbyProviderIds.Empty);
        var analysis = new EmbyFileAnalysis(
            vm.MediaFilePath,
            Path.ChangeExtension(vm.MediaFilePath, ".nfo"),
            MediaFileExists: true,
            NfoExists: false,
            NfoProviderIds: EmbyProviderIds.Empty,
            EmbyItem: null,
            WarningMessage: null);

        vm.ApplyAnalysis(analysis);

        Assert.False(vm.SupportsProviderIdSync);
        Assert.False(vm.CanEditProviderIds);
        Assert.False(vm.CanReviewTvdb);
        Assert.Equal("Ohne NFO-Sync", vm.StatusText);
        Assert.Equal("Emby-Asset ohne Episoden-NFO.", vm.Note);
        Assert.DoesNotContain("Bitte Emby zuerst scannen", vm.Note, StringComparison.Ordinal);
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
        Assert.Contains("TVDB 555 vorgesehen", vm.Note, StringComparison.Ordinal);
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
        Assert.Equal("IDs fehlen", vm.StatusText);
        Assert.Equal("Warning", vm.StatusTone);
        Assert.Contains("TVDB manuell gewählt", vm.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyImdbSelection_CompletesProviderIdsAndMarksRowReady()
    {
        var vm = new EmbySyncItemViewModel(@"C:\Videos\Serie - S01E01 - Pilot.mkv", new EmbyProviderIds("12345", null));

        vm.ApplyImdbSelection("tt1234567");

        Assert.Equal("tt1234567", vm.ImdbId);
        Assert.Equal("Bereit", vm.StatusText);
        Assert.Equal("Ready", vm.StatusTone);
        Assert.Contains("IMDb manuell gesetzt", vm.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NFO aktuell", "Done")]
    [InlineData("Aktualisiert", "Done")]
    [InlineData("IDs fehlen", "Warning")]
    [InlineData("NFO fehlt", "Warning")]
    [InlineData("NFO prüfen", "Warning")]
    [InlineData("Übersprungen", "Warning")]
    [InlineData("Fehlt", "Error")]
    [InlineData("Noch nicht geprüft", "Neutral")]
    public void SetStatus_MapsStatusTone(string statusText, string expectedTone)
    {
        var vm = new EmbySyncItemViewModel(@"C:\Videos\Serie - S01E01 - Pilot.mkv", EmbyProviderIds.Empty);

        vm.SetStatus(statusText, "Hinweis");

        Assert.Equal(expectedTone, vm.StatusTone);
    }
}
