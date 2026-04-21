using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class ImdbLookupWindowViewModelTests
{
    [Fact]
    public void Constructor_WithGuess_BuildsBrowserSearchOptions()
    {
        var vm = new ImdbLookupWindowViewModel(
            new EpisodeMetadataGuess("Neues aus Büttenwarder", "Rififi", "2014", "05-E06"),
            currentImdbId: null);

        Assert.NotEmpty(vm.SearchOptions);
        Assert.Equal("Suchtext", vm.SearchOptions[0].DisplayText);
        Assert.Contains("find/?q=", vm.SearchOptions[0].TargetUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildImdbId_AcceptsBareId()
    {
        var vm = new ImdbLookupWindowViewModel(null, null)
        {
            ImdbInput = "tt1234567"
        };

        var success = vm.TryBuildImdbId(out var imdbId, out var validationMessage);

        Assert.True(success);
        Assert.Equal("tt1234567", imdbId);
        Assert.Null(validationMessage);
    }

    [Fact]
    public void TryBuildImdbId_AcceptsFullImdbUrl()
    {
        var vm = new ImdbLookupWindowViewModel(null, null)
        {
            ImdbInput = "https://www.imdb.com/title/tt7654321/?ref_=fn_al_tt_1"
        };

        var success = vm.TryBuildImdbId(out var imdbId, out var validationMessage);

        Assert.True(success);
        Assert.Equal("tt7654321", imdbId);
        Assert.Null(validationMessage);
    }

    [Fact]
    public void TryBuildImdbId_RejectsInvalidInput()
    {
        var vm = new ImdbLookupWindowViewModel(null, null)
        {
            ImdbInput = "kein imdb treffer"
        };

        var success = vm.TryBuildImdbId(out var imdbId, out var validationMessage);

        Assert.False(success);
        Assert.Null(imdbId);
        Assert.Contains("IMDb-ID", validationMessage, StringComparison.Ordinal);
    }
}
