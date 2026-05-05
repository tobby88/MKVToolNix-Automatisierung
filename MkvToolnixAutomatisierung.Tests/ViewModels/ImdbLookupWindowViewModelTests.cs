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
    public async Task InitializeAsync_AutoSelectsUniqueFuzzyEpisodeTitle()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            return request.RequestUri?.ToString() switch
            {
                "https://api.imdbapi.dev/search/titles?query=Der Alte" => CreateJsonResponse(
                    """
                    {
                      "titles": [
                        { "id": "tt0075474", "type": "tvSeries", "primaryTitle": "The Old Fox", "originalTitle": "Der Alte", "startYear": 1977, "endYear": null }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0075474/seasons" => CreateJsonResponse(
                    """
                    {
                      "seasons": [
                        { "season": "53", "episodeCount": 8 }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0075474/episodes?season=53" => CreateJsonResponse(
                    """
                    {
                      "episodes": [
                        { "id": "tt40075198", "title": "Wunschkind", "season": "53", "episodeNumber": 1 },
                        { "id": "tt40659530", "title": "Die Wahrheit im Dunklen", "season": "53", "episodeNumber": 2 },
                        { "id": "tt40660083", "title": "Mia", "season": "53", "episodeNumber": 3 }
                      ]
                    }
                    """),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected URI: {request.RequestUri}")
            };
        }));
        var vm = new ImdbLookupWindowViewModel(
            new ImdbLookupService(httpClient),
            ImdbLookupMode.Auto,
            new EpisodeMetadataGuess("Der Alte", "Die Wahrheit im Dunkeln", "55", "02"),
            currentImdbId: null);

        await vm.InitializeAsync();

        Assert.Equal("53", vm.EpisodeSeasonText);
        var episode = Assert.Single(vm.EpisodeResults);
        Assert.Equal("tt40659530", episode.Episode.Id);
        Assert.Equal("tt40659530", vm.SelectedEpisodeItem?.Episode.Id);
        Assert.Equal("tt40659530", vm.ImdbInput);
        Assert.Contains("unscharfer Titel", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_DoesNotAutoSelectUnrelatedTitle_ByEpisodeNumberOnly()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            return request.RequestUri?.ToString() switch
            {
                "https://api.imdbapi.dev/search/titles?query=Der Alte" => CreateJsonResponse(
                    """
                    {
                      "titles": [
                        { "id": "tt0075474", "type": "tvSeries", "primaryTitle": "The Old Fox", "originalTitle": "Der Alte", "startYear": 1977, "endYear": null }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0075474/seasons" => CreateJsonResponse(
                    """
                    {
                      "seasons": [
                        { "season": "53", "episodeCount": 8 }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0075474/episodes?season=53" => CreateJsonResponse(
                    """
                    {
                      "episodes": [
                        { "id": "tt40659530", "title": "Völlig anderer Fall", "season": "53", "episodeNumber": 2 }
                      ]
                    }
                    """),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected URI: {request.RequestUri}")
            };
        }));
        var vm = new ImdbLookupWindowViewModel(
            new ImdbLookupService(httpClient),
            ImdbLookupMode.Auto,
            new EpisodeMetadataGuess("Der Alte", "Die Wahrheit im Dunkeln", "55", "02"),
            currentImdbId: null);

        await vm.InitializeAsync();

        Assert.Empty(vm.EpisodeResults);
        Assert.Null(vm.SelectedEpisodeItem);
        Assert.Equal(string.Empty, vm.ImdbInput);
        Assert.Contains("Keine Episode", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadNextEpisodeSeasonAsync_LoadsAdjacentSeasonForLargeSeries()
    {
        var requestedUris = new List<string>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!.ToString());
            return request.RequestUri?.ToString() switch
            {
                "https://api.imdbapi.dev/search/titles?query=Big Series" => CreateJsonResponse(
                    """
                    {
                      "titles": [
                        { "id": "tt0100001", "type": "tvSeries", "primaryTitle": "Big Series", "originalTitle": "Big Series", "startYear": 1980, "endYear": null }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0100001/seasons" => CreateJsonResponse(
                    $$"""
                    {
                      "seasons": [
                        {{string.Join(
                            "," + Environment.NewLine,
                            Enumerable.Range(1, 20).Select(season => $$"""        { "season": "{{season}}", "episodeCount": 1 }"""))}}
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0100001/episodes?season=18" => CreateJsonResponse(
                    """
                    {
                      "episodes": [
                        { "id": "tt0000018", "title": "Target", "season": "18", "episodeNumber": 1 }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0100001/episodes?season=19" => CreateJsonResponse(
                    """
                    {
                      "episodes": [
                        { "id": "tt0000019", "title": "Target", "season": "19", "episodeNumber": 1 }
                      ]
                    }
                    """),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected URI: {request.RequestUri}")
            };
        }));
        var vm = new ImdbLookupWindowViewModel(
            new ImdbLookupService(httpClient),
            ImdbLookupMode.Auto,
            new EpisodeMetadataGuess("Big Series", "Target", "18", "01"),
            currentImdbId: null);

        await vm.InitializeAsync();

        Assert.Equal("18", vm.EpisodeSeasonText);
        Assert.Equal("tt0000018", vm.ImdbInput);

        await vm.LoadNextEpisodeSeasonAsync();

        Assert.Equal("19", vm.EpisodeSeasonText);
        Assert.Equal("tt0000019", vm.ImdbInput);
        Assert.DoesNotContain(requestedUris, uri => string.Equals(
            uri,
            "https://api.imdbapi.dev/titles/tt0100001/episodes?season=1",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadNextEpisodeSeasonAsync_SkipsSeasonsMissingFromApiList()
    {
        var requestedUris = new List<string>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!.ToString());
            return request.RequestUri?.ToString() switch
            {
                "https://api.imdbapi.dev/search/titles?query=Versatzserie" => CreateJsonResponse(
                    """
                    {
                      "titles": [
                        { "id": "tt0100002", "type": "tvSeries", "primaryTitle": "Versatzserie", "originalTitle": "Versatzserie", "startYear": 2000, "endYear": null }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0100002/seasons" => CreateJsonResponse(
                    """
                    {
                      "seasons": [
                        { "season": "1", "episodeCount": 1 },
                        { "season": "3", "episodeCount": 1 }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0100002/episodes?season=1" => CreateJsonResponse(
                    """
                    {
                      "episodes": [
                        { "id": "tt0000101", "title": "Ziel", "season": "1", "episodeNumber": 1 }
                      ]
                    }
                    """),
                "https://api.imdbapi.dev/titles/tt0100002/episodes?season=3" => CreateJsonResponse(
                    """
                    {
                      "episodes": [
                        { "id": "tt0000301", "title": "Ziel", "season": "3", "episodeNumber": 1 }
                      ]
                    }
                    """),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected URI: {request.RequestUri}")
            };
        }));
        var vm = new ImdbLookupWindowViewModel(
            new ImdbLookupService(httpClient),
            ImdbLookupMode.Auto,
            new EpisodeMetadataGuess("Versatzserie", "Ziel", "01", "01"),
            currentImdbId: null);

        await vm.InitializeAsync();

        Assert.Equal(["1", "3"], vm.AvailableEpisodeSeasons);
        Assert.Equal("1", vm.EpisodeSeasonText);
        Assert.True(vm.CanLoadNextEpisodeSeason);

        await vm.LoadNextEpisodeSeasonAsync();

        Assert.Equal("3", vm.EpisodeSeasonText);
        Assert.Equal("tt0000301", vm.ImdbInput);
        Assert.False(vm.CanLoadNextEpisodeSeason);
        Assert.DoesNotContain(
            "https://api.imdbapi.dev/titles/tt0100002/episodes?season=2",
            requestedUris);
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
        Assert.Contains("Netzwerkfehler", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("Browserhilfe", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_ShowsFriendlyStatus_WhenApiUnavailableInApiOnlyMode()
    {
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(
            new ImdbLookupService(httpClient),
            ImdbLookupMode.ApiOnly,
            new EpisodeMetadataGuess("Neues aus Büttenwarder", "Rififi", "05", "06"),
            currentImdbId: null);

        await vm.InitializeAsync();

        Assert.True(vm.IsApiWorkflowVisible);
        Assert.False(vm.IsBrowserWorkflowVisible);
        Assert.Contains("IMDb-Suche über imdbapi.dev nicht möglich", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("Netzwerkfehler", vm.StatusText, StringComparison.Ordinal);
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
    public void TryBuildImdbId_AcceptsLocalizedImdbUrl()
    {
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(new ImdbLookupService(httpClient), ImdbLookupMode.BrowserOnly, null, null)
        {
            ImdbInput = "https://www.imdb.com/de/title/tt7654321/?ref_=fn_al_tt_1"
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

    [Theory]
    [InlineData("IMDb: tt0826760")]
    [InlineData("Episode link: https://www.imdb.com/title/tt0826760/?ref_=ttep_ep1")]
    [InlineData("Episode link: https://www.imdb.com/de/title/tt0826760/?ref_=ttep_ep1")]
    public void TryImportClipboardText_AcceptsIdOrImdbUrlEmbeddedInClipboardText(string clipboardText)
    {
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(new ImdbLookupService(httpClient), ImdbLookupMode.BrowserOnly, null, null);

        var success = vm.TryImportClipboardText(clipboardText);

        Assert.True(success);
        Assert.Equal("tt0826760", vm.ImdbInput);
    }

    [Fact]
    public void TryImportClipboardText_AcceptsAlreadyImportedIdAsSuccessfulNoOp()
    {
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var vm = new ImdbLookupWindowViewModel(new ImdbLookupService(httpClient), ImdbLookupMode.BrowserOnly, null, "tt0826760");

        var success = vm.TryImportClipboardText("https://www.imdb.com/title/tt0826760/?ref_=ttep_ep1");

        Assert.True(success);
        Assert.Equal("tt0826760", vm.ImdbInput);
        Assert.Contains("bereits eingetragen", vm.StatusText, StringComparison.Ordinal);
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
