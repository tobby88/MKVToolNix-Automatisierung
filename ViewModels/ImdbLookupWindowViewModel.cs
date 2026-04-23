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
    private static readonly Regex BareImdbIdPattern = new(@"^tt\d{7,10}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ImdbTitlePathPattern = new(@"^/title/(?<id>tt\d{7,10})(?:[/?#]|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly ImdbLookupService _lookupService;
    private readonly EpisodeMetadataGuess? _guess;
    private readonly ImdbLookupMode _lookupMode;
    private readonly List<ImdbSeriesSearchResult> _seriesResults = [];
    private readonly List<ImdbEpisodeRecord> _episodes = [];
    private bool _isBusy;
    private bool _isInitialized;
    private bool _suppressSeriesSelectionChanged;
    private bool _browserFallbackActive;
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
    /// Sichtbare Browserhilfen für den manuellen IMDb-Abgleich.
    /// </summary>
    public ObservableCollection<SearchOptionItem> SearchOptions { get; } = [];

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
                ReplaceItems(EpisodeResults, []);
                SelectedEpisodeItem = null;
                UpdateComparisonSummary();
            }
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
        catch
        {
            StatusText = "IMDb-Suche fehlgeschlagen.";
            throw;
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
            return false;
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

        if (TryExtractImdbIdFromUrl(normalized, out imdbId))
        {
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
            SetBusy(true, "Lade Episodenliste...");
            var episodes = await _lookupService.LoadEpisodesAsync(SelectedSeriesItem.Series.Id);
            _episodes.Clear();
            _episodes.AddRange(episodes);
            ApplyEpisodeFilter(autoSelectBest);

            if (SelectedEpisodeItem is null)
            {
                StatusText = _episodes.Count == 0
                    ? "Keine Episoden zu dieser Serie geladen."
                    : $"{_episodes.Count} Episode(n) geladen. Bitte Episode auswählen.";
            }
        }
        catch (Exception ex) when (TryActivateBrowserFallback(ex))
        {
        }
        catch
        {
            StatusText = "IMDb-Episodenliste konnte nicht geladen werden.";
            throw;
        }
        finally
        {
            SetBusy(false, StatusText);
        }
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
            string.Equals(item.Episode.Id, preferredEpisode.Id, StringComparison.OrdinalIgnoreCase));
        if (SelectedEpisodeItem is not null)
        {
            StatusText = $"IMDb-Vorschlag: {SelectedEpisodeItem.DisplayText}";
        }

        UpdateComparisonSummary();
    }

    private void ClearApiResults()
    {
        _seriesResults.Clear();
        _episodes.Clear();
        ReplaceItems(SeriesResults, []);
        ReplaceItems(EpisodeResults, []);
        _suppressSeriesSelectionChanged = true;
        SelectedSeriesItem = null;
        _suppressSeriesSelectionChanged = false;
        SelectedEpisodeItem = null;
        UpdateComparisonSummary();
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

    private ImdbEpisodeRecord? TryFindPreferredEpisodeMatch(IReadOnlyList<ImdbEpisodeRecord> episodes)
    {
        if (episodes.Count == 0)
        {
            return null;
        }

        if (_guess is null)
        {
            return episodes.Count == 1 ? episodes[0] : null;
        }

        var normalizedEpisodeTitle = EpisodeMetadataMatchingHeuristics.NormalizeText(_guess.EpisodeTitle);
        if (string.IsNullOrWhiteSpace(normalizedEpisodeTitle))
        {
            return episodes.Count == 1 ? episodes[0] : null;
        }

        var exactMatches = episodes
            .Where(episode => string.Equals(
                EpisodeMetadataMatchingHeuristics.NormalizeText(episode.Title),
                normalizedEpisodeTitle,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exactMatches.Count == 1)
        {
            return exactMatches[0];
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
                return tieBrokenMatches[0];
            }
        }

        return null;
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

        var reason = string.IsNullOrWhiteSpace(ex.Message)
            ? "imdbapi.dev ist derzeit nicht erreichbar."
            : $"imdbapi.dev ist derzeit nicht erreichbar: {ex.Message}";
        StatusText = $"{reason} Browserhilfe aktiviert.";
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
                EpisodeMetadataMatchingHeuristics.NormalizeText(episode.Title).Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase)
                || BuildEpisodeCode(episode).Contains(filterText.Trim(), StringComparison.OrdinalIgnoreCase))
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
}
