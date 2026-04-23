using System.Net;
using System.Net.Http;
using System.Text;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class ImdbLookupWindowViewModelTests
{
    [Fact]
    public async Task InitializeAsync_AutoSelectsUniqueSeriesAndEpisode_BySeriesAndTitle()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            return request.RequestUri?.ToString() switch
            {
                "https://api.imdbapi.dev/search/titles?query=Friends" => CreateJsonResponse(
                    """
                    {
                      "titles": [
                        { "id": "tt0108778", "type": "tvSeries", "primaryTitle": "Friends", "originalTitle": "Friends", "startYear": 1994, "endYear": 2004 }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0108778/seasons" => CreateJsonResponse(
                    """
                    {
                      "seasons": [
                        { "season": "2", "episodeCount": 1 }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0108778/episodes?season=2" => CreateJsonResponse(
                    """
                    {
                      "episodes": [
                        { "id": "tt0583655", "title": "The One After the Superbowl: Part 1", "season": "2", "episodeNumber": 12 }
                      ]
                    }
                    """),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected URI: {request.RequestUri}")
            };
        }));
        var vm = new ImdbLookupWindowViewModel(
            new ImdbLookupService(httpClient),
            ImdbLookupMode.Auto,
            new EpisodeMetadataGuess("Friends", "The One After the Superbowl: Part 1", "01", "07"),
            currentImdbId: null);

        await vm.InitializeAsync();

        Assert.True(vm.IsApiWorkflowVisible);
        Assert.Equal("tt0108778", vm.SelectedSeriesItem?.Series.Id);
        Assert.Equal("tt0583655", vm.SelectedEpisodeItem?.Episode.Id);
        Assert.Equal("tt0583655", vm.ImdbInput);
        Assert.Contains("IMDb-Vorschlag", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_UsesSeasonEpisodeOnlyAsTieBreaker()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            return request.RequestUri?.ToString() switch
            {
                "https://api.imdbapi.dev/search/titles?query=Beispielserie" => CreateJsonResponse(
                    """
                    {
                      "titles": [
                        { "id": "tt0100001", "type": "tvSeries", "primaryTitle": "Beispielserie", "originalTitle": "Beispielserie", "startYear": 2020, "endYear": 2024 }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0100001/seasons" => CreateJsonResponse(
                    """
                    {
                      "seasons": [
                        { "season": "1", "episodeCount": 10 },
                        { "season": "2", "episodeCount": 10 }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0100001/episodes?season=1" => CreateJsonResponse(
                    """
                    {
                      "episodes": [
                        { "id": "tt0000001", "title": "Das Duell", "season": "1", "episodeNumber": 2 }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0100001/episodes?season=2" => CreateJsonResponse(
                    """
                    {
                      "episodes": [
                        { "id": "tt0000002", "title": "Das Duell", "season": "2", "episodeNumber": 5 }
                      ]
                    }
                    """),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected URI: {request.RequestUri}")
            };
        }));
        var vm = new ImdbLookupWindowViewModel(
            new ImdbLookupService(httpClient),
            ImdbLookupMode.Auto,
            new EpisodeMetadataGuess("Beispielserie", "Das Duell", "02", "05"),
            currentImdbId: null);

        await vm.InitializeAsync();

        Assert.Equal("tt0000002", vm.SelectedEpisodeItem?.Episode.Id);
        Assert.Equal("tt0000002", vm.ImdbInput);
    }

    [Fact]
    public async Task InitializeAsync_FallsBackToBrowser_WhenApiUnavailableInAutoMode()
    {
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(
            new ImdbLookupService(httpClient),
            ImdbLookupMode.Auto,
            new EpisodeMetadataGuess("Neues aus Büttenwarder", "Rififi", "05", "06"),
            currentImdbId: null);

        await vm.InitializeAsync();

        Assert.False(vm.IsApiWorkflowVisible);
        Assert.True(vm.IsBrowserWorkflowVisible);
        Assert.NotEmpty(vm.SearchOptions);
        Assert.Contains("Browserhilfe", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_WithBrowserMode_BuildsBrowserSearchOptions()
    {
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(
            new ImdbLookupService(httpClient),
            ImdbLookupMode.BrowserOnly,
            new EpisodeMetadataGuess("Neues aus Büttenwarder", "Rififi", "2014", "05-E06"),
            currentImdbId: null);

        Assert.True(vm.IsBrowserWorkflowVisible);
        Assert.NotEmpty(vm.SearchOptions);
        Assert.Equal("Suchtext", vm.SearchOptions[0].DisplayText);
        Assert.Contains("find/?q=", vm.SearchOptions[0].TargetUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchTextFields_PreserveSpacesWhileEditing()
    {
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(
            new ImdbLookupService(httpClient),
            ImdbLookupMode.Auto,
            guess: null,
            currentImdbId: null);

        vm.SeriesSearchText = "New Series";
        vm.EpisodeSearchText = "Episode Title";

        Assert.Equal("New Series", vm.SeriesSearchText);
        Assert.Equal("Episode Title", vm.EpisodeSearchText);
    }

    [Fact]
    public void PrepareBrowserSearchFromCurrentFields_UsesEditedApiSearchTexts()
    {
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(
            new ImdbLookupService(httpClient),
            ImdbLookupMode.Auto,
            guess: null,
            currentImdbId: null)
        {
            SeriesSearchText = "New Series",
            EpisodeSearchText = "Episode Title"
        };

        var prepared = vm.PrepareBrowserSearchFromCurrentFields();

        Assert.True(prepared);
        Assert.Equal("New Series Episode Title", vm.SearchText);
        Assert.Equal("Suchtext", vm.SelectedSearchOption?.DisplayText);
        Assert.Contains("New%20Series%20Episode%20Title", vm.SelectedSearchOption?.TargetUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildImdbId_AcceptsBareId()
    {
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(new ImdbLookupService(httpClient), ImdbLookupMode.BrowserOnly, null, null)
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
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(new ImdbLookupService(httpClient), ImdbLookupMode.BrowserOnly, null, null)
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
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(new ImdbLookupService(httpClient), ImdbLookupMode.BrowserOnly, null, null)
        {
            ImdbInput = "kein imdb treffer"
        };

        var success = vm.TryBuildImdbId(out var imdbId, out var validationMessage);

        Assert.False(success);
        Assert.Null(imdbId);
        Assert.Contains("IMDb-ID", validationMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildImdbId_RejectsEmbeddedSubstringThatIsNotStandaloneIdOrImdbUrl()
    {
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(new ImdbLookupService(httpClient), ImdbLookupMode.BrowserOnly, null, null)
        {
            ImdbInput = "nottt1234567bad"
        };

        var success = vm.TryBuildImdbId(out var imdbId, out _);

        Assert.False(success);
        Assert.Null(imdbId);
    }

    [Fact]
    public void TryBuildImdbId_RejectsTooLongBareId()
    {
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(new ImdbLookupService(httpClient), ImdbLookupMode.BrowserOnly, null, null)
        {
            ImdbInput = "tt12345678901"
        };

        var success = vm.TryBuildImdbId(out var imdbId, out _);

        Assert.False(success);
        Assert.Null(imdbId);
    }

    [Fact]
    public void TryBuildImdbId_RejectsForeignUrlWithEmbeddedImdbId()
    {
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(new ImdbLookupService(httpClient), ImdbLookupMode.BrowserOnly, null, null)
        {
            ImdbInput = "https://example.com/title/tt7654321/"
        };

        var success = vm.TryBuildImdbId(out var imdbId, out _);

        Assert.False(success);
        Assert.Null(imdbId);
    }

    [Fact]
    public void TryImportClipboardText_AcceptsFullImdbUrlAndNormalizesToId()
    {
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(new ImdbLookupService(httpClient), ImdbLookupMode.BrowserOnly, null, null);

        var success = vm.TryImportClipboardText("https://www.imdb.com/title/tt0826760/?ref_=ttep_ep1");

        Assert.True(success);
        Assert.Equal("tt0826760", vm.ImdbInput);
        Assert.Contains("Zwischenablage", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void TryImportClipboardText_IgnoresAlreadyImportedId()
    {
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(new ImdbLookupService(httpClient), ImdbLookupMode.BrowserOnly, null, "tt0826760");

        var success = vm.TryImportClipboardText("https://www.imdb.com/title/tt0826760/?ref_=ttep_ep1");

        Assert.False(success);
        Assert.Equal("tt0826760", vm.ImdbInput);
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    private sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("api down");
        }
    }
}
