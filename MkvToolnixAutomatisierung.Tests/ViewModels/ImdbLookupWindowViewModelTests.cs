using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class ImdbLookupWindowViewModelTests
{
    [Fact]
    public void Constructor_BuildsBrowserSearchesFromDetectedEpisode()
    {
        var vm = new ImdbLookupWindowViewModel(
            new EpisodeMetadataGuess("Der Alte", "Die Wahrheit im Dunkeln", "55", "02"),
            currentImdbId: null);

        Assert.Contains(vm.SearchOptions, option =>
            option.DisplayText == "Serie + Episodentitel"
            && option.TargetUrl.Contains("Der%20Alte%20Die%20Wahrheit%20im%20Dunkeln", StringComparison.Ordinal));
        Assert.Contains(vm.SearchOptions, option =>
            option.DisplayText == "Serie + Episodencode"
            && option.TargetUrl.Contains("S55E02", StringComparison.Ordinal));
        Assert.True(vm.CanOpenSelectedSearch);
    }

    [Fact]
    public void SearchFields_RebuildSearchTargetWithoutLosingSpaces()
    {
        var vm = new ImdbLookupWindowViewModel(guess: null, currentImdbId: null)
        {
            SeriesSearchText = "New Series",
            EpisodeSearchText = "Episode Title"
        };

        var option = Assert.Single(vm.SearchOptions, item => item.DisplayText == "Serie + Episodentitel");
        Assert.Contains("New%20Series%20Episode%20Title", option.TargetUrl, StringComparison.Ordinal);
        Assert.Equal("New Series Episode Title", vm.SearchText);
    }

    [Fact]
    public void SearchFields_DoNotOverwriteManuallyChangedFreeSearchText()
    {
        var vm = new ImdbLookupWindowViewModel(
            new EpisodeMetadataGuess("Old Series", "Old Episode", "01", "01"),
            currentImdbId: null)
        {
            SearchText = "independent query",
            SeriesSearchText = "New Series"
        };

        Assert.Equal("independent query", vm.SearchText);
        Assert.Contains(vm.SearchOptions, option =>
            option.DisplayText == "Serie + Episodentitel"
            && option.TargetUrl.Contains("New%20Series%20Old%20Episode", StringComparison.Ordinal));
    }

    [Fact]
    public void Constructor_WithExistingId_OffersDirectTitleLink()
    {
        var vm = new ImdbLookupWindowViewModel(guess: null, currentImdbId: "TT0826760");

        var option = Assert.Single(vm.SearchOptions, item => item.DisplayText == "Aktuellen IMDb-Eintrag öffnen");
        Assert.Equal("https://www.imdb.com/title/tt0826760/", option.TargetUrl);
        Assert.Equal("tt0826760", vm.ImdbInput);
    }

    [Theory]
    [InlineData("tt1234567", "tt1234567")]
    [InlineData("TT12345678", "tt12345678")]
    [InlineData("https://www.imdb.com/title/tt7654321/?ref_=fn_al_tt_1", "tt7654321")]
    [InlineData("https://www.imdb.com/de/title/tt7654321/", "tt7654321")]
    public void TryBuildImdbId_AcceptsSupportedIdAndTitleUrls(string input, string expected)
    {
        var vm = new ImdbLookupWindowViewModel(guess: null, currentImdbId: null) { ImdbInput = input };

        var success = vm.TryBuildImdbId(out var imdbId, out var validationMessage);

        Assert.True(success);
        Assert.Equal(expected, imdbId);
        Assert.Null(validationMessage);
    }

    [Theory]
    [InlineData("kein imdb treffer")]
    [InlineData("nottt1234567bad")]
    [InlineData("tt12345678901")]
    [InlineData("https://example.com/title/tt7654321/")]
    public void TryBuildImdbId_RejectsUnsupportedInput(string input)
    {
        var vm = new ImdbLookupWindowViewModel(guess: null, currentImdbId: null) { ImdbInput = input };

        var success = vm.TryBuildImdbId(out var imdbId, out var validationMessage);

        Assert.False(success);
        Assert.Null(imdbId);
        Assert.Contains("IMDb-ID", validationMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IMDb: tt0826760")]
    [InlineData("Episode link: https://www.imdb.com/title/tt0826760/?ref_=ttep_ep1")]
    [InlineData("Episode link: https://www.imdb.com/de/title/tt0826760/")]
    public void TryImportClipboardText_AcceptsIdOrImdbUrlEmbeddedInText(string clipboardText)
    {
        var vm = new ImdbLookupWindowViewModel(guess: null, currentImdbId: null);

        var success = vm.TryImportClipboardText(clipboardText);

        Assert.True(success);
        Assert.Equal("tt0826760", vm.ImdbInput);
        Assert.Contains("Zwischenablage", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void TryImportClipboardText_WithExistingId_IsSuccessfulNoOp()
    {
        var vm = new ImdbLookupWindowViewModel(guess: null, currentImdbId: "tt0826760");

        var success = vm.TryImportClipboardText("https://www.imdb.com/title/tt0826760/");

        Assert.True(success);
        Assert.Equal("tt0826760", vm.ImdbInput);
        Assert.Contains("bereits eingetragen", vm.StatusText, StringComparison.Ordinal);
    }
}
