using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels;

/// <summary>
/// Kapselt den manuellen IMDb-Abgleich für eine einzelne Emby-Zeile.
/// </summary>
/// <remarks>
/// Der Dialog nutzt bevorzugt <c>imdbapi.dev</c>, kann aber je nach Settings oder Provider-Ausfall
/// auf die bestehende Browserhilfe zurückfallen. Die automatische Vorwahl ist bewusst konservativ:
/// Für Serien- und Episodenauswahl zählt primär der normalisierte Serien- bzw. Episodentitel,
/// Staffel/Folge nur als Tie-Breaker.
/// </remarks>
internal sealed class ImdbLookupWindowViewModel : INotifyPropertyChanged
{
    private const int StrongFuzzyTitleScore = 88;
    private const int EpisodeNumberAssistedFuzzyTitleScore = 78;
    private const int FuzzyAutoSelectGap = 4;
    private static readonly Regex BareImdbIdPattern = new(@"^tt\d{7,10}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StandaloneImdbIdPattern = new(@"(?<![A-Za-z0-9])(?<id>tt\d{7,10})(?![A-Za-z0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AbsoluteUrlPattern = new(@"https?://[^\s<>""]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ImdbTitlePathPattern = new(@"^/(?:[a-z]{2}(?:-[a-z]{2})?/)?title/(?<id>tt\d{7,10})(?:[/?#]|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly ImdbLookupService _lookupService;
    private readonly EpisodeMetadataGuess? _guess;
    private readonly ImdbLookupMode _lookupMode;
    private readonly List<ImdbSeriesSearchResult> _seriesResults = [];
    private readonly List<ImdbEpisodeRecord> _episodes = [];
    private readonly List<string> _availableEpisodeSeasons = [];
    private string _episodeSeasonText;
    private bool _isBusy;
    private bool _isInitialized;
    private bool _suppressSeriesSelectionChanged;
    private bool _browserFallbackActive;
    private string? _loadedSeasonSeriesId;
    private string _seriesSearchText;
    private string _episodeSearchText;
    private string _searchText;
    private string _imdbInput;
    private string _comparisonSummaryText;
    private string _statusText;
    private SelectableSeriesItem? _selectedSeriesItem;
    private SelectableEpisodeItem? _selectedEpisodeItem;
    private SearchOptionItem? _selectedSearchOption;

    internal ImdbLookupWindowViewModel(
        ImdbLookupService lookupService,
        ImdbLookupMode lookupMode,
        EpisodeMetadataGuess? guess,
        string? currentImdbId)
    {
        _lookupService = lookupService;
        _lookupMode = lookupMode;
        _guess = guess;
        _seriesSearchText = guess?.SeriesName ?? string.Empty;
        _episodeSearchText = guess?.EpisodeTitle ?? string.Empty;
        _episodeSeasonText = NormalizeSeasonText(guess?.SeasonNumber) ?? string.Empty;
        _searchText = BuildDefaultSearchText(guess);
        _imdbInput = TryNormalizeImdbId(currentImdbId, out var normalizedImdbId)
            ? normalizedImdbId!
            : (currentImdbId ?? string.Empty).Trim();
        _comparisonSummaryText = "Noch kein IMDb-Eintrag ausgewählt.";
        _statusText = BuildInitialStatusText(guess, _imdbInput, lookupMode);
        GuessSummaryText = BuildGuessSummaryText(guess, _imdbInput);
        RebuildSearchOptions();
        UpdateComparisonSummary();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Zusammenfassung der lokalen Ausgangsdaten und einer bereits gesetzten IMDb-ID.
    /// </summary>
    public string GuessSummaryText { get; }

    /// <summary>
    /// Seriensuchtext für den API-basierten Modus.
    /// </summary>
    public string SeriesSearchText
    {
        get => _seriesSearchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (_seriesSearchText == normalized)
            {
                return;
            }

            _seriesSearchText = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanOpenBrowserSearch));
        }
    }

    /// <summary>
    /// Filtertext für die geladene Episodenliste.
    /// </summary>
    public string EpisodeSearchText
    {
        get => _episodeSearchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (_episodeSearchText == normalized)
            {
                return;
            }

            _episodeSearchText = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanOpenBrowserSearch));
            ApplyEpisodeFilter(autoSelectBest: false);
        }
    }

    /// <summary>
    /// Suchtext für die Browserhilfe.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (_searchText == normalized)
            {
                return;
            }

            _searchText = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanOpenBrowserSearch));
            RebuildSearchOptions();
        }
    }

    /// <summary>
    /// Sichtbare API-Serientreffer.
    /// </summary>
    public ObservableCollection<SelectableSeriesItem> SeriesResults { get; } = [];

    /// <summary>
    /// Sichtbare API-Episodentreffer der gewählten Serie.
    /// </summary>
    public ObservableCollection<SelectableEpisodeItem> EpisodeResults { get; } = [];

    /// <summary>
    /// Von IMDb gemeldete Staffeln der aktuell gewählten Serie.
    /// </summary>
    public ObservableCollection<string> AvailableEpisodeSeasons { get; } = [];

    /// <summary>
    /// Sichtbare Browserhilfen für den manuellen IMDb-Abgleich.
    /// </summary>
    public ObservableCollection<SearchOptionItem> SearchOptions { get; } = [];

    /// <summary>
    /// IMDb-Staffel, deren Episoden im API-Modus gezielt geladen werden.
    /// </summary>
    public string EpisodeSeasonText
    {
        get => _episodeSeasonText;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_episodeSeasonText == normalized)
            {
                return;
            }

            _episodeSeasonText = normalized;
            OnPropertyChanged();
            NotifyEpisodeSeasonCommandPropertiesChanged();
        }
    }

    public SelectableSeriesItem? SelectedSeriesItem
    {
        get => _selectedSeriesItem;
        set
        {
            if (ReferenceEquals(_selectedSeriesItem, value))
            {
                return;
            }

            _selectedSeriesItem = value;
            OnPropertyChanged();

            if (!_suppressSeriesSelectionChanged)
            {
                ClearEpisodeSeasonResults();
                ReplaceItems(EpisodeResults, []);
                SelectedEpisodeItem = null;
                UpdateComparisonSummary();
            }

            NotifyEpisodeSeasonCommandPropertiesChanged();
        }
    }

    public SelectableEpisodeItem? SelectedEpisodeItem
    {
        get => _selectedEpisodeItem;
        set
        {
            if (ReferenceEquals(_selectedEpisodeItem, value))
            {
                return;
            }

            _selectedEpisodeItem = value;
            OnPropertyChanged();

            if (_selectedEpisodeItem is not null)
            {
                ImdbInput = _selectedEpisodeItem.Episode.Id;
            }

            UpdateComparisonSummary();
        }
    }

    public SearchOptionItem? SelectedSearchOption
    {
        get => _selectedSearchOption;
        set
        {
            if (ReferenceEquals(_selectedSearchOption, value))
            {
                return;
            }

            _selectedSearchOption = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanOpenSelectedSearch));
            OnPropertyChanged(nameof(CanOpenBrowserSearch));
        }
    }

    /// <summary>
    /// Manuell bestätigte IMDb-ID oder komplette IMDb-URL.
    /// </summary>
    public string ImdbInput
    {
        get => _imdbInput;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_imdbInput == normalized)
            {
                return;
            }

            _imdbInput = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanApply));
            RebuildSearchOptions();
            UpdateComparisonSummary();
        }
    }

    /// <summary>
    /// Gegenüberstellung zwischen lokaler Erkennung und aktueller IMDb-Auswahl.
    /// </summary>
    public string ComparisonSummaryText
    {
        get => _comparisonSummaryText;
        private set
        {
            if (_comparisonSummaryText == value)
            {
                return;
            }

            _comparisonSummaryText = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInteractive));
        }
    }

    public bool IsInteractive => !IsBusy;

    public bool IsApiWorkflowVisible => _lookupMode != ImdbLookupMode.BrowserOnly && !_browserFallbackActive;

    public bool IsBrowserWorkflowVisible => _lookupMode == ImdbLookupMode.BrowserOnly || _browserFallbackActive;

    public bool CanOpenSelectedSearch => SelectedSearchOption is not null;

    /// <summary>
    /// Aktiviert die direkte Browserhilfe auch dann, wenn im API-Modus gerade nur die Suchfelder gefüllt sind.
    /// </summary>
    public bool CanOpenBrowserSearch => CanOpenSelectedSearch
        || !string.IsNullOrWhiteSpace(SeriesSearchText)
        || !string.IsNullOrWhiteSpace(EpisodeSearchText)
        || !string.IsNullOrWhiteSpace(SearchText);

    public bool CanLoadEpisodeSeason => SelectedSeriesItem is not null
        && ResolveAvailableEpisodeSeason(EpisodeSeasonText) is not null;

    public bool CanLoadPreviousEpisodeSeason => SelectedSeriesItem is not null
        && TryFindAdjacentEpisodeSeason(-1, out _);

    public bool CanLoadNextEpisodeSeason => SelectedSeriesItem is not null
        && TryFindAdjacentEpisodeSeason(1, out _);

    public bool CanApply => TryNormalizeImdbId(ImdbInput, out _);

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        if (_lookupMode == ImdbLookupMode.BrowserOnly)
        {
            StatusText = "Browserhilfe für IMDb vorbereitet.";
            return;
        }

        await SearchSeriesAsync(autoLoadEpisodes: true);
    }

    public async Task SearchSeriesAsync(bool autoLoadEpisodes)
    {
        if (!IsApiWorkflowVisible)
        {
            return;
        }

        try
        {
            SetBusy(true, "Suche Serie bei imdbapi.dev...");
            var query = SeriesSearchText.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                ClearApiResults();
                StatusText = "Bitte zuerst einen Seriennamen eingeben.";
                return;
            }

            var results = await _lookupService.SearchSeriesAsync(query);
            _seriesResults.Clear();
            _seriesResults.AddRange(results);
            ReplaceItems(SeriesResults, _seriesResults.Select(result => new SelectableSeriesItem(result)));

            _episodes.Clear();
            ClearEpisodeSeasonResults();
            ReplaceItems(EpisodeResults, []);
            SelectedEpisodeItem = null;

            if (_seriesResults.Count == 0)
            {
                SelectedSeriesItem = null;
                StatusText = "Keine passende IMDb-Serie gefunden.";
                UpdateComparisonSummary();
                return;
            }

            var preferredSeries = TryFindPreferredSeriesResult(_seriesResults);
            if (preferredSeries is null)
            {
                SelectedSeriesItem = null;
                StatusText = $"{_seriesResults.Count} Serie(n) gefunden. Bitte Serie auswählen.";
                UpdateComparisonSummary();
                return;
            }

            _suppressSeriesSelectionChanged = true;
            SelectedSeriesItem = SeriesResults.FirstOrDefault(item => item.Series.Id == preferredSeries.Id);
            _suppressSeriesSelectionChanged = false;
            StatusText = $"{_seriesResults.Count} Serie(n) gefunden.";

            if (autoLoadEpisodes)
            {
                await LoadEpisodesForSelectedSeriesAsync(autoSelectBest: true);
            }
            else
            {
                UpdateComparisonSummary();
            }
        }
        catch (Exception ex) when (TryActivateBrowserFallback(ex))
        {
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText = ProviderLookupErrorFormatter.FormatImdbSearchFailure(ex);
            ClearApiResults();
            UpdateComparisonSummary();
        }
        finally
        {
            SetBusy(false, StatusText);
        }
    }

    public async Task HandleSelectedSeriesSelectionChangedAsync()
    {
        if (_suppressSeriesSelectionChanged || IsBusy || SelectedSeriesItem is null)
        {
            return;
        }

        await LoadEpisodesForSelectedSeriesAsync(autoSelectBest: true);
    }

    public Task LoadSelectedEpisodeSeasonAsync()
    {
        return LoadEpisodeSeasonAsync(offset: 0, autoSelectBest: true);
    }

    public Task LoadPreviousEpisodeSeasonAsync()
    {
        return LoadEpisodeSeasonAsync(offset: -1, autoSelectBest: true);
    }

    public Task LoadNextEpisodeSeasonAsync()
    {
        return LoadEpisodeSeasonAsync(offset: 1, autoSelectBest: true);
    }

    public void MarkSelectedSearchOpened()
    {
        if (SelectedSearchOption is null)
        {
            return;
        }

        StatusText = $"IMDb-Suche geöffnet: {SelectedSearchOption.DisplayText}";
    }

    /// <summary>
    /// Aktualisiert die Browserhilfe aus den gerade sichtbaren API-Suchfeldern.
    /// </summary>
    /// <returns><see langword="true"/>, wenn danach ein Browserziel vorhanden ist.</returns>
    public bool PrepareBrowserSearchFromCurrentFields()
    {
        var queryParts = new[]
            {
                SeriesSearchText,
                EpisodeSearchText
            }
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var query = string.Join(" ", queryParts);
        if (!string.IsNullOrWhiteSpace(query))
        {
            SearchText = query;
        }

        SelectedSearchOption = SearchOptions.FirstOrDefault(option =>
                                   string.Equals(option.DisplayText, "Suchtext", StringComparison.OrdinalIgnoreCase))
                               ?? SearchOptions.FirstOrDefault();
        return SelectedSearchOption is not null;
    }

    public bool TryBuildImdbId(out string? imdbId, out string? validationMessage)
    {
        if (TryNormalizeImdbId(ImdbInput, out imdbId))
        {
            validationMessage = null;
            return true;
        }

        validationMessage = "Bitte eine IMDb-ID im Format tt1234567 oder eine komplette IMDb-URL eintragen.";
        return false;
    }

    public bool TryImportClipboardText(string? clipboardText)
    {
        if (!TryNormalizeImdbId(clipboardText, out var imdbId))
        {
            return false;
        }

        if (TryNormalizeImdbId(ImdbInput, out var currentImdbId)
            && string.Equals(currentImdbId, imdbId, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = $"IMDb-ID aus Zwischenablage ist bereits eingetragen: {imdbId}";
            return true;
        }

        ImdbInput = imdbId!;
        StatusText = $"IMDb-ID aus Zwischenablage erkannt: {imdbId}";
        return true;
    }

    internal static bool TryNormalizeImdbId(string? input, out string? imdbId)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            imdbId = null;
            return false;
        }

        if (BareImdbIdPattern.IsMatch(normalized))
        {
            imdbId = normalized.ToLowerInvariant();
            return true;
        }

        if (TryExtractImdbIdFromUrl(normalized, out imdbId)
            || TryExtractImdbIdFromSupportedUrlInText(normalized, out imdbId))
        {
            return true;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var unsupportedUri)
            && (string.Equals(unsupportedUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(unsupportedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            imdbId = null;
            return false;
        }

        var standaloneMatch = StandaloneImdbIdPattern.Match(normalized);
        if (standaloneMatch.Success)
        {
            imdbId = standaloneMatch.Groups["id"].Value.ToLowerInvariant();
            return true;
        }

        imdbId = null;
        return false;
    }

    private async Task LoadEpisodesForSelectedSeriesAsync(bool autoSelectBest)
    {
        if (SelectedSeriesItem is null)
        {
            return;
        }

        try
        {
            SetBusy(true, "Lade IMDb-Staffelliste...");
            await EnsureEpisodeSeasonsLoadedForSelectedSeriesAsync();
            if (!NormalizeEpisodeSeasonSelection())
            {
                ReplaceItems(EpisodeResults, []);
                SelectedEpisodeItem = null;
                UpdateComparisonSummary();
                return;
            }

            SetBusy(true, BuildEpisodeLoadStatus(EpisodeSeasonText));
            var episodes = await _lookupService.LoadEpisodesAsync(SelectedSeriesItem.Series.Id, EpisodeSeasonText);
            _episodes.Clear();
            _episodes.AddRange(FilterEpisodesBySelectedSeason(episodes, EpisodeSeasonText));
            ApplyEpisodeFilter(autoSelectBest);

            if (SelectedEpisodeItem is null)
            {
                StatusText = _episodes.Count == 0
                    ? BuildNoEpisodesLoadedStatus(EpisodeSeasonText)
                    : EpisodeResults.Count == 0
                        ? "Keine Episode zum aktuellen Filter gefunden."
                        : $"{EpisodeResults.Count} Episode(n) geladen. Bitte Episode auswählen.";
            }
        }
        catch (Exception ex) when (TryActivateBrowserFallback(ex))
        {
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText = ProviderLookupErrorFormatter.FormatImdbEpisodeFailure(ex);
            _episodes.Clear();
            ReplaceItems(EpisodeResults, []);
            SelectedEpisodeItem = null;
            UpdateComparisonSummary();
        }
        finally
        {
            SetBusy(false, StatusText);
        }
    }

    private async Task LoadEpisodeSeasonAsync(int offset, bool autoSelectBest)
    {
        if (SelectedSeriesItem is null)
        {
            return;
        }

        if (offset != 0)
        {
            if (!TryFindAdjacentEpisodeSeason(offset, out var adjacentSeason))
            {
                StatusText = offset < 0
                    ? "Vor der aktuell gewählten IMDb-Staffel gibt es laut API keine weitere Staffel."
                    : "Nach der aktuell gewählten IMDb-Staffel gibt es laut API keine weitere Staffel.";
                return;
            }

            EpisodeSeasonText = adjacentSeason;
        }
        else if (ResolveAvailableEpisodeSeason(EpisodeSeasonText) is string selectedSeason)
        {
            EpisodeSeasonText = selectedSeason;
        }
        else
        {
            StatusText = "Bitte zuerst eine von IMDb gemeldete Staffel auswählen.";
            return;
        }

        await LoadEpisodesForSelectedSeriesAsync(autoSelectBest);
    }

    private async Task EnsureEpisodeSeasonsLoadedForSelectedSeriesAsync()
    {
        if (SelectedSeriesItem is null)
        {
            ClearEpisodeSeasonResults();
            return;
        }

        var seriesId = SelectedSeriesItem.Series.Id;
        if (string.Equals(_loadedSeasonSeriesId, seriesId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var seasons = await _lookupService.LoadSeasonsAsync(seriesId);
        _loadedSeasonSeriesId = seriesId;
        _availableEpisodeSeasons.Clear();
        _availableEpisodeSeasons.AddRange(seasons
            .Select(season => NormalizeSeasonText(season.Season))
            .Where(season => !string.IsNullOrWhiteSpace(season))
            .Select(season => season!)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        ReplaceItems(AvailableEpisodeSeasons, _availableEpisodeSeasons);
        NotifyEpisodeSeasonCommandPropertiesChanged();
    }

    private bool NormalizeEpisodeSeasonSelection()
    {
        var selectedSeason = ResolveAvailableEpisodeSeason(EpisodeSeasonText)
                             ?? SelectClosestAvailableEpisodeSeason(EpisodeSeasonText)
                             ?? _availableEpisodeSeasons.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(selectedSeason))
        {
            StatusText = "IMDb meldet für diese Serie keine Staffeln.";
            NotifyEpisodeSeasonCommandPropertiesChanged();
            return false;
        }

        EpisodeSeasonText = selectedSeason;
        return true;
    }

    private void ApplyEpisodeFilter(bool autoSelectBest)
    {
        var filteredEpisodes = FilterEpisodes(_episodes, EpisodeSearchText);
        var items = filteredEpisodes
            .OrderBy(item => ParseSortableSeason(item.Season))
            .ThenBy(item => item.EpisodeNumber ?? int.MaxValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Select(item => new SelectableEpisodeItem(item))
            .ToList();
        var previouslySelectedEpisodeId = SelectedEpisodeItem?.Episode.Id;

        ReplaceItems(EpisodeResults, items);
        SelectedEpisodeItem = null;

        if (!autoSelectBest)
        {
            if (!string.IsNullOrWhiteSpace(previouslySelectedEpisodeId))
            {
                SelectedEpisodeItem = EpisodeResults.FirstOrDefault(item =>
                    string.Equals(item.Episode.Id, previouslySelectedEpisodeId, StringComparison.OrdinalIgnoreCase));
            }

            UpdateComparisonSummary();
            return;
        }

        var preferredEpisode = TryFindPreferredEpisodeMatch(filteredEpisodes);
        if (preferredEpisode is null)
        {
            StatusText = filteredEpisodes.Count == 0
                ? "Keine Episode zum aktuellen Filter gefunden."
                : "Keine Episode automatisch sicher vorgewählt.";
            UpdateComparisonSummary();
            return;
        }

        SelectedEpisodeItem = EpisodeResults.FirstOrDefault(item =>
            string.Equals(item.Episode.Id, preferredEpisode.Episode.Id, StringComparison.OrdinalIgnoreCase));
        if (SelectedEpisodeItem is not null)
        {
            StatusText = preferredEpisode.IsFuzzy
                ? $"IMDb-Vorschlag (unscharfer Titel): {SelectedEpisodeItem.DisplayText}"
                : $"IMDb-Vorschlag: {SelectedEpisodeItem.DisplayText}";
        }

        UpdateComparisonSummary();
    }

    private void ClearApiResults()
    {
        _seriesResults.Clear();
        _episodes.Clear();
        ClearEpisodeSeasonResults();
        ReplaceItems(SeriesResults, []);
        ReplaceItems(EpisodeResults, []);
        _suppressSeriesSelectionChanged = true;
        SelectedSeriesItem = null;
        _suppressSeriesSelectionChanged = false;
        SelectedEpisodeItem = null;
        UpdateComparisonSummary();
    }

    private void ClearEpisodeSeasonResults()
    {
        _loadedSeasonSeriesId = null;
        _availableEpisodeSeasons.Clear();
        ReplaceItems(AvailableEpisodeSeasons, []);
        NotifyEpisodeSeasonCommandPropertiesChanged();
    }

    private void UpdateComparisonSummary()
    {
        if (SelectedEpisodeItem is not null)
        {
            ComparisonSummaryText = $"IMDb-Auswahl: {SelectedEpisodeItem.DisplayText}";
            return;
        }

        if (SelectedSeriesItem is not null)
        {
            ComparisonSummaryText = $"IMDb-Serie gewählt: {SelectedSeriesItem.DisplayText}";
            return;
        }

        if (TryNormalizeImdbId(ImdbInput, out var imdbId))
        {
            ComparisonSummaryText = $"Aktuelle IMDb-ID: {imdbId}";
            return;
        }

        ComparisonSummaryText = "Noch kein IMDb-Eintrag ausgewählt.";
    }

    private void RebuildSearchOptions()
    {
        var items = BuildSearchOptions(_guess, SearchText, ImdbInput);
        ReplaceItems(SearchOptions, items);
        SelectedSearchOption = SearchOptions.FirstOrDefault();
    }

    private ImdbSeriesSearchResult? TryFindPreferredSeriesResult(IReadOnlyList<ImdbSeriesSearchResult> results)
    {
        if (results.Count == 1)
        {
            return results[0];
        }

        if (_guess is null)
        {
            return null;
        }

        var normalizedSeriesName = EpisodeMetadataMatchingHeuristics.NormalizeText(_guess.SeriesName);
        if (string.IsNullOrWhiteSpace(normalizedSeriesName))
        {
            return null;
        }

        var exactMatches = results
            .Where(result =>
                string.Equals(EpisodeMetadataMatchingHeuristics.NormalizeText(result.PrimaryTitle), normalizedSeriesName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(EpisodeMetadataMatchingHeuristics.NormalizeText(result.OriginalTitle), normalizedSeriesName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return exactMatches.Count == 1
            ? exactMatches[0]
            : null;
    }

    private PreferredImdbEpisodeMatch? TryFindPreferredEpisodeMatch(IReadOnlyList<ImdbEpisodeRecord> episodes)
    {
        if (episodes.Count == 0)
        {
            return null;
        }

        if (_guess is null)
        {
            return episodes.Count == 1 ? new PreferredImdbEpisodeMatch(episodes[0], IsFuzzy: false) : null;
        }

        var normalizedEpisodeTitle = EpisodeMetadataMatchingHeuristics.NormalizeText(_guess.EpisodeTitle);
        if (string.IsNullOrWhiteSpace(normalizedEpisodeTitle))
        {
            return episodes.Count == 1 ? new PreferredImdbEpisodeMatch(episodes[0], IsFuzzy: false) : null;
        }

        var exactMatches = episodes
            .Where(episode => string.Equals(
                EpisodeMetadataMatchingHeuristics.NormalizeText(episode.Title),
                normalizedEpisodeTitle,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exactMatches.Count == 1)
        {
            return new PreferredImdbEpisodeMatch(exactMatches[0], IsFuzzy: false);
        }

        if (exactMatches.Count > 1
            && int.TryParse(_guess.SeasonNumber, out var guessSeason)
            && int.TryParse(_guess.EpisodeNumber, out var guessEpisode))
        {
            var tieBrokenMatches = exactMatches
                .Where(episode => ParseSortableSeason(episode.Season) == guessSeason && episode.EpisodeNumber == guessEpisode)
                .ToList();
            if (tieBrokenMatches.Count == 1)
            {
                return new PreferredImdbEpisodeMatch(tieBrokenMatches[0], IsFuzzy: false);
            }
        }

        return TryFindPreferredFuzzyEpisodeMatch(episodes);
    }

    private PreferredImdbEpisodeMatch? TryFindPreferredFuzzyEpisodeMatch(IReadOnlyList<ImdbEpisodeRecord> episodes)
    {
        if (_guess is null)
        {
            return null;
        }

        var scoredMatches = episodes
            .Select(episode =>
            {
                var titleScore = CalculateFuzzyTitleScore(_guess.EpisodeTitle, episode.Title);
                var episodeNumberMatches = int.TryParse(_guess.EpisodeNumber, out var guessEpisode)
                                           && episode.EpisodeNumber == guessEpisode;
                return new
                {
                    Episode = episode,
                    TitleScore = titleScore,
                    EpisodeNumberMatches = episodeNumberMatches,
                    IsCandidate = titleScore >= StrongFuzzyTitleScore
                                  || (episodeNumberMatches && titleScore >= EpisodeNumberAssistedFuzzyTitleScore)
                };
            })
            .Where(match => match.IsCandidate)
            .OrderByDescending(match => match.TitleScore)
            .ThenByDescending(match => match.EpisodeNumberMatches)
            .ThenBy(match => ParseSortableSeason(match.Episode.Season))
            .ThenBy(match => match.Episode.EpisodeNumber ?? int.MaxValue)
            .ToList();
        if (scoredMatches.Count == 0)
        {
            return null;
        }

        var bestMatch = scoredMatches[0];
        if (bestMatch.EpisodeNumberMatches)
        {
            var sameEpisodeNumberMatches = scoredMatches
                .Where(match => match.EpisodeNumberMatches)
                .Where(match => bestMatch.TitleScore - match.TitleScore <= FuzzyAutoSelectGap)
                .ToList();
            if (sameEpisodeNumberMatches.Count == 1)
            {
                return new PreferredImdbEpisodeMatch(bestMatch.Episode, IsFuzzy: true);
            }
        }

        var closeBestMatches = scoredMatches
            .Where(match => bestMatch.TitleScore - match.TitleScore <= FuzzyAutoSelectGap)
            .ToList();
        return closeBestMatches.Count == 1 && bestMatch.TitleScore >= StrongFuzzyTitleScore
            ? new PreferredImdbEpisodeMatch(bestMatch.Episode, IsFuzzy: true)
            : null;
    }

    private bool TryActivateBrowserFallback(Exception ex)
    {
        if (_lookupMode != ImdbLookupMode.Auto)
        {
            return false;
        }

        _browserFallbackActive = true;
        ClearApiResults();
        OnPropertyChanged(nameof(IsApiWorkflowVisible));
        OnPropertyChanged(nameof(IsBrowserWorkflowVisible));
        RebuildSearchOptions();

        StatusText = $"{ProviderLookupErrorFormatter.FormatImdbFallbackReason(ex)} Browserhilfe aktiviert.";
        return true;
    }

    private static IReadOnlyList<SearchOptionItem> BuildSearchOptions(
        EpisodeMetadataGuess? guess,
        string searchText,
        string currentImdbInput)
    {
        var options = new List<SearchOptionItem>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (TryNormalizeImdbId(currentImdbInput, out var currentImdbId))
        {
            AddOption(
                options,
                seenTargets,
                "Aktuellen IMDb-Eintrag öffnen",
                BuildImdbTitleUrl(currentImdbId!),
                "Öffnet direkt den derzeit eingetragenen IMDb-Titel.");
        }

        var queries = new List<(string DisplayText, string Query, string Description)>();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            queries.Add(("Suchtext", searchText, "Öffnet die allgemeine IMDb-Titelsuche mit dem aktuellen Suchtext."));
        }

        if (guess is not null)
        {
            queries.Add((
                "Serie + Episodentitel",
                $"{guess.SeriesName} {guess.EpisodeTitle}",
                "Typischer Suchlauf für Serienepisoden anhand von Serie und Titel."));

            var episodeCode = EpisodeFileNameHelper.BuildEpisodeCode(guess.SeasonNumber, guess.EpisodeNumber);
            if (!episodeCode.Contains("xx", StringComparison.OrdinalIgnoreCase))
            {
                queries.Add((
                    "Serie + Episodencode",
                    $"{guess.SeriesName} {episodeCode} {guess.EpisodeTitle}",
                    "Hilfreich, wenn IMDb den Eintrag eher über Staffel/Folge als über den Titel findet."));
            }
        }

        foreach (var query in queries
                     .Where(entry => !string.IsNullOrWhiteSpace(entry.Query))
                     .DistinctBy(entry => entry.Query, StringComparer.OrdinalIgnoreCase))
        {
            AddOption(
                options,
                seenTargets,
                query.DisplayText,
                BuildImdbSearchUrl(query.Query),
                query.Description);
        }

        return options;
    }

    private static void AddOption(
        ICollection<SearchOptionItem> options,
        ISet<string> seenTargets,
        string displayText,
        string targetUrl,
        string description)
    {
        if (!seenTargets.Add(targetUrl))
        {
            return;
        }

        options.Add(new SearchOptionItem(displayText, targetUrl, description));
    }

    private static IReadOnlyList<ImdbEpisodeRecord> FilterEpisodes(
        IEnumerable<ImdbEpisodeRecord> episodes,
        string filterText)
    {
        var normalizedFilter = EpisodeMetadataMatchingHeuristics.NormalizeText(filterText);
        if (string.IsNullOrWhiteSpace(normalizedFilter))
        {
            return episodes.ToList();
        }

        return episodes
            .Where(episode =>
                IsEpisodeTitleFilterMatch(normalizedFilter, episode.Title)
                || BuildEpisodeCode(episode).Contains(filterText.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool IsEpisodeTitleFilterMatch(string normalizedFilter, string episodeTitle)
    {
        var normalizedTitle = EpisodeMetadataMatchingHeuristics.NormalizeText(episodeTitle);
        return normalizedTitle.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase)
               || CalculateFuzzyTitleScore(normalizedFilter, normalizedTitle, inputsAreNormalized: true) >= EpisodeNumberAssistedFuzzyTitleScore;
    }

    private static int CalculateFuzzyTitleScore(string left, string right, bool inputsAreNormalized = false)
    {
        var normalizedLeft = inputsAreNormalized ? left : EpisodeMetadataMatchingHeuristics.NormalizeText(left);
        var normalizedRight = inputsAreNormalized ? right : EpisodeMetadataMatchingHeuristics.NormalizeText(right);
        if (string.IsNullOrWhiteSpace(normalizedLeft) || string.IsNullOrWhiteSpace(normalizedRight))
        {
            return 0;
        }

        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (normalizedLeft.Contains(normalizedRight, StringComparison.OrdinalIgnoreCase)
            || normalizedRight.Contains(normalizedLeft, StringComparison.OrdinalIgnoreCase))
        {
            return 92;
        }

        var maximumLength = Math.Max(normalizedLeft.Length, normalizedRight.Length);
        if (maximumLength < 6)
        {
            return 0;
        }

        var editDistance = CalculateLevenshteinDistance(normalizedLeft, normalizedRight);
        return Math.Max(0, (int)Math.Round((1d - editDistance / (double)maximumLength) * 100));
    }

    private static int CalculateLevenshteinDistance(string left, string right)
    {
        if (left.Length == 0)
        {
            return right.Length;
        }

        if (right.Length == 0)
        {
            return left.Length;
        }

        var previousRow = new int[right.Length + 1];
        var currentRow = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previousRow[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            currentRow[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                currentRow[column] = Math.Min(
                    Math.Min(currentRow[column - 1] + 1, previousRow[column] + 1),
                    previousRow[column - 1] + substitutionCost);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[right.Length];
    }

    private static IReadOnlyList<ImdbEpisodeRecord> FilterEpisodesBySelectedSeason(
        IEnumerable<ImdbEpisodeRecord> episodes,
        string selectedSeason)
    {
        var normalizedSelectedSeason = NormalizeSeasonText(selectedSeason);
        if (string.IsNullOrWhiteSpace(normalizedSelectedSeason))
        {
            return episodes.ToList();
        }

        return episodes
            .Where(episode => string.Equals(
                NormalizeSeasonText(episode.Season),
                normalizedSelectedSeason,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string BuildDefaultSearchText(EpisodeMetadataGuess? guess)
    {
        return guess is null
            ? string.Empty
            : $"{guess.SeriesName} {guess.EpisodeTitle}".Trim();
    }

    private static string BuildInitialStatusText(
        EpisodeMetadataGuess? guess,
        string currentImdbId,
        ImdbLookupMode lookupMode)
    {
        if (TryNormalizeImdbId(currentImdbId, out var normalizedImdbId))
        {
            return $"Bereits eingetragen: {normalizedImdbId}";
        }

        return lookupMode switch
        {
            ImdbLookupMode.BrowserOnly => guess is null
                ? "Browserhilfe ohne automatische Vorbelegung vorbereitet."
                : "Browserhilfe aus der lokalen Erkennung vorbereitet.",
            _ => guess is null
                ? "IMDb-Dialog bereit. Für eine API-Vorbelegung fehlt die lokale Dateinamenschätzung."
                : "IMDb-Dialog bereit. Serie und Episode werden beim Öffnen vorbefüllt gesucht."
        };
    }

    private static string BuildGuessSummaryText(EpisodeMetadataGuess? guess, string? currentImdbId)
    {
        var currentImdbInfo = TryNormalizeImdbId(currentImdbId, out var normalizedImdbId)
            ? $"Aktuell eingetragen: {normalizedImdbId}"
            : "Aktuell eingetragen: keine IMDb-ID";
        if (guess is null)
        {
            return $"{currentImdbInfo}{Environment.NewLine}Die Datei liefert keine automatische Serien-/Episodenvorbelegung. Die IMDb-Suche muss manuell angepasst werden.";
        }

        return $"{currentImdbInfo}{Environment.NewLine}Lokal erkannt: {guess.SeriesName} - {EpisodeFileNameHelper.BuildEpisodeCode(guess.SeasonNumber, guess.EpisodeNumber)} - {guess.EpisodeTitle}";
    }

    private static string BuildEpisodeLoadStatus(string seasonText)
    {
        return NormalizeSeasonText(seasonText) is string season
            ? $"Lade IMDb-Episodenliste für Staffel {season}..."
            : "Lade IMDb-Episodenliste...";
    }

    private static string BuildNoEpisodesLoadedStatus(string seasonText)
    {
        return NormalizeSeasonText(seasonText) is string season
            ? $"Keine Episoden zu IMDb-Staffel {season} geladen."
            : "Keine Episoden zu dieser Serie geladen.";
    }

    private static bool TryExtractImdbIdFromUrl(string input, out string? imdbId)
    {
        imdbId = null;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || !IsSupportedImdbHost(uri.Host))
        {
            return false;
        }

        var match = ImdbTitlePathPattern.Match(uri.AbsolutePath);
        if (!match.Success)
        {
            return false;
        }

        imdbId = match.Groups["id"].Value.ToLowerInvariant();
        return true;
    }

    private static bool TryExtractImdbIdFromSupportedUrlInText(string input, out string? imdbId)
    {
        foreach (Match match in AbsoluteUrlPattern.Matches(input))
        {
            if (TryExtractImdbIdFromUrl(match.Value, out imdbId))
            {
                return true;
            }
        }

        imdbId = null;
        return false;
    }

    private static bool IsSupportedImdbHost(string host)
    {
        return string.Equals(host, "imdb.com", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".imdb.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildImdbSearchUrl(string query)
    {
        return $"https://www.imdb.com/find/?q={Uri.EscapeDataString(query)}&s=tt&ttype=ep&ref_=fn_tt_ex";
    }

    private static string BuildImdbTitleUrl(string imdbId)
    {
        return $"https://www.imdb.com/title/{imdbId}/";
    }

    private static string BuildEpisodeCode(ImdbEpisodeRecord episode)
    {
        return int.TryParse(episode.Season, out var seasonNumber) && episode.EpisodeNumber is int episodeNumber
            ? $"S{seasonNumber:00}E{episodeNumber:00}"
            : string.IsNullOrWhiteSpace(episode.Season)
                ? episode.EpisodeNumber?.ToString() ?? string.Empty
                : $"S{episode.Season}E{episode.EpisodeNumber?.ToString() ?? "?"}";
    }

    private static int ParseSortableSeason(string? season)
    {
        return int.TryParse(season, out var parsed)
            ? parsed
            : int.MaxValue;
    }

    private static bool TryParseEpisodeSeason(string seasonText, out int season)
    {
        return int.TryParse(NormalizeSeasonText(seasonText), out season);
    }

    private string? ResolveAvailableEpisodeSeason(string? seasonText)
    {
        var normalizedSeason = NormalizeSeasonText(seasonText);
        if (string.IsNullOrWhiteSpace(normalizedSeason))
        {
            return null;
        }

        return _availableEpisodeSeasons.Count == 0
            ? null
            : _availableEpisodeSeasons.FirstOrDefault(season =>
                string.Equals(season, normalizedSeason, StringComparison.OrdinalIgnoreCase));
    }

    private string? SelectClosestAvailableEpisodeSeason(string? seasonText)
    {
        if (_availableEpisodeSeasons.Count == 0 || !TryParseEpisodeSeason(seasonText ?? string.Empty, out var preferredSeason))
        {
            return null;
        }

        return _availableEpisodeSeasons
            .Select((season, index) => new
            {
                Season = season,
                Index = index,
                SortValue = ParseSortableSeason(season)
            })
            .Where(item => item.SortValue != int.MaxValue)
            .OrderBy(item => Math.Abs(item.SortValue - preferredSeason))
            .ThenBy(item => item.SortValue)
            .ThenBy(item => item.Index)
            .Select(item => item.Season)
            .FirstOrDefault();
    }

    private bool TryFindAdjacentEpisodeSeason(int offset, out string season)
    {
        season = string.Empty;
        if (_availableEpisodeSeasons.Count == 0 || offset == 0)
        {
            return false;
        }

        var currentSeason = ResolveAvailableEpisodeSeason(EpisodeSeasonText)
                            ?? SelectClosestAvailableEpisodeSeason(EpisodeSeasonText);
        var currentIndex = string.IsNullOrWhiteSpace(currentSeason)
            ? -1
            : _availableEpisodeSeasons.FindIndex(candidate =>
                string.Equals(candidate, currentSeason, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            return false;
        }

        var adjacentIndex = currentIndex + offset;
        if (adjacentIndex < 0 || adjacentIndex >= _availableEpisodeSeasons.Count)
        {
            return false;
        }

        season = _availableEpisodeSeasons[adjacentIndex];
        return true;
    }

    private static string? NormalizeSeasonText(string? seasonText)
    {
        var normalized = (seasonText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains("xx", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(normalized, out var season)
            ? season.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : normalized;
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private void SetBusy(bool isBusy, string statusText)
    {
        IsBusy = isBusy;
        StatusText = statusText;
        NotifyEpisodeSeasonCommandPropertiesChanged();
    }

    private void NotifyEpisodeSeasonCommandPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanLoadEpisodeSeason));
        OnPropertyChanged(nameof(CanLoadPreviousEpisodeSeason));
        OnPropertyChanged(nameof(CanLoadNextEpisodeSeason));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class SelectableSeriesItem
    {
        public SelectableSeriesItem(ImdbSeriesSearchResult series)
        {
            Series = series;
        }

        public ImdbSeriesSearchResult Series { get; }

        public string DisplayText => string.IsNullOrWhiteSpace(Series.OriginalTitle)
                                     || string.Equals(Series.PrimaryTitle, Series.OriginalTitle, StringComparison.OrdinalIgnoreCase)
            ? BuildSeriesLabel(Series.PrimaryTitle, Series.StartYear, Series.EndYear)
            : $"{BuildSeriesLabel(Series.PrimaryTitle, Series.StartYear, Series.EndYear)} | Original: {Series.OriginalTitle}";

        private static string BuildSeriesLabel(string title, int? startYear, int? endYear)
        {
            if (startYear is null)
            {
                return title;
            }

            return endYear is null || endYear == startYear
                ? $"{title} ({startYear})"
                : $"{title} ({startYear}-{endYear})";
        }
    }

    public sealed class SelectableEpisodeItem
    {
        public SelectableEpisodeItem(ImdbEpisodeRecord episode)
        {
            Episode = episode;
        }

        public ImdbEpisodeRecord Episode { get; }

        public string DisplayText
        {
            get
            {
                var code = BuildEpisodeCode(Episode);
                return string.IsNullOrWhiteSpace(code)
                    ? $"{Episode.Title} | {Episode.Id}"
                    : $"{code} - {Episode.Title} | {Episode.Id}";
            }
        }
    }

    public sealed record SearchOptionItem(string DisplayText, string TargetUrl, string Description);

    private sealed record PreferredImdbEpisodeMatch(ImdbEpisodeRecord Episode, bool IsFuzzy);
}
