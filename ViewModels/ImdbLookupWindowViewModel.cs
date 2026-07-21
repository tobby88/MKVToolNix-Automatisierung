using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels;

/// <summary>
/// Bereitet den lokalen und browsergestützten IMDb-Abgleich für eine einzelne Episode vor.
/// </summary>
/// <remarks>
/// Der Dialog zeigt zuerst Treffer aus dem optionalen lokalen Index der offiziellen IMDb-Datensätze.
/// Wenn daraus keine eindeutige Auswahl möglich ist, öffnet er gezielte IMDb-Browsersuchen und übernimmt
/// anschließend eine kopierte IMDb-ID oder Titel-URL. Der vorgelagerte Emby-Workflow versucht zusätzlich,
/// die IMDb-Remote-ID direkt über die bereits bekannte TVDB-Episode aufzulösen.
/// </remarks>
internal sealed class ImdbLookupWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan LocalSearchDebounceDelay = TimeSpan.FromMilliseconds(250);
    private static readonly Regex BareImdbIdPattern = new(@"^tt\d{7,10}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StandaloneImdbIdPattern = new(@"(?<![A-Za-z0-9])(?<id>tt\d{7,10})(?![A-Za-z0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AbsoluteUrlPattern = new(@"https?://[^\s<>""]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ImdbTitlePathPattern = new(@"^/(?:[a-z]{2}(?:-[a-z]{2})?/)?title/(?<id>tt\d{7,10})(?:[/?#]|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly EpisodeMetadataGuess? _guess;
    private readonly ImdbDatasetSearchService? _imdbDatasetSearch;
    private string _seriesSearchText;
    private string _episodeSearchText;
    private string _searchText;
    private string _imdbInput;
    private string _comparisonSummaryText;
    private string _statusText;
    private SearchOptionItem? _selectedSearchOption;
    private ImdbEpisodeCandidate? _selectedLocalCandidate;
    private ImdbSeriesCandidate? _selectedLocalSeries;
    private ImdbSeasonFilterOption? _selectedSeasonFilter;
    private string _localDatasetStatusText;
    private bool _isLocalSearchRunning;
    private bool _suppressLocalSeriesRefresh;
    private int _localSearchRevision;
    private CancellationTokenSource? _liveSearchCancellationSource;
    private Task _pendingLiveSearch = Task.CompletedTask;
    private IReadOnlyList<ImdbEpisodeCandidate> _unfilteredLocalCandidates = [];

    internal ImdbLookupWindowViewModel(
        EpisodeMetadataGuess? guess,
        string? currentImdbId,
        ImdbDatasetSearchService? imdbDatasetSearch = null)
    {
        _guess = guess;
        _imdbDatasetSearch = imdbDatasetSearch;
        _seriesSearchText = guess?.SeriesName ?? string.Empty;
        _episodeSearchText = guess?.EpisodeTitle ?? string.Empty;
        _searchText = BuildDefaultSearchText(guess);
        _imdbInput = TryNormalizeImdbId(currentImdbId, out var normalizedImdbId)
            ? normalizedImdbId!
            : (currentImdbId ?? string.Empty).Trim();
        _comparisonSummaryText = "Noch keine gültige IMDb-ID eingetragen.";
        _localDatasetStatusText = _imdbDatasetSearch?.IsAvailable == true
            ? "Die lokale Suche startet beim Öffnen des Dialogs."
            : "Der lokale IMDb-Index ist nicht aktiviert oder noch nicht installiert.";
        _statusText = BuildInitialStatusText(guess, _imdbInput);
        GuessSummaryText = BuildGuessSummaryText(guess, _imdbInput);
        RebuildSearchOptions();
        UpdateComparisonSummary();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Beschreibt die lokale Erkennung und eine eventuell bereits vorhandene IMDb-ID.
    /// </summary>
    public string GuessSummaryText { get; }

    /// <summary>
    /// Serienname für die kombinierte IMDb-Suche.
    /// </summary>
    public string SeriesSearchText
    {
        get => _seriesSearchText;
        set => SetStructuredSearchField(ref _seriesSearchText, value, refreshSeries: true);
    }

    /// <summary>
    /// Episodentitel für die kombinierte IMDb-Suche.
    /// </summary>
    public string EpisodeSearchText
    {
        get => _episodeSearchText;
        set => SetStructuredSearchField(ref _episodeSearchText, value, refreshSeries: false);
    }

    /// <summary>
    /// Optionaler frei editierbarer Suchtext.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set => SetSearchField(ref _searchText, value);
    }

    /// <summary>
    /// Verfügbare direkte IMDb-Ziele und Suchvarianten.
    /// </summary>
    public ObservableCollection<SearchOptionItem> SearchOptions { get; } = [];

    /// <summary>
    /// Treffer aus den optional lokal installierten offiziellen IMDb-Datensätzen.
    /// </summary>
    public ObservableCollection<ImdbEpisodeCandidate> LocalCandidates { get; } = [];

    /// <summary>
    /// Lokalisierte Serienkandidaten des Offlineindex. Die ausgewählte Serie bestimmt den Episodenkatalog.
    /// </summary>
    public ObservableCollection<ImdbSeriesCandidate> LocalSeriesCandidates { get; } = [];

    /// <summary>
    /// Verfügbare Staffelfilter der ausgewählten IMDb-Serie einschließlich „Alle Staffeln“.
    /// </summary>
    public ObservableCollection<ImdbSeasonFilterOption> SeasonFilters { get; } = [];

    /// <summary>
    /// Aktuell gewählte IMDb-Serie für die lokale Browse-Ansicht.
    /// </summary>
    public ImdbSeriesCandidate? SelectedLocalSeries
    {
        get => _selectedLocalSeries;
        set
        {
            if (ReferenceEquals(_selectedLocalSeries, value))
            {
                return;
            }

            _selectedLocalSeries = value;
            OnPropertyChanged();
            if (!_suppressLocalSeriesRefresh)
            {
                ScheduleLocalRefresh(refreshSeries: false, immediate: true);
            }
        }
    }

    /// <summary>
    /// Optionaler Staffelfilter; ein leerer Zahlenwert zeigt den gesamten Serienkatalog.
    /// </summary>
    public ImdbSeasonFilterOption? SelectedSeasonFilter
    {
        get => _selectedSeasonFilter;
        set
        {
            if (Equals(_selectedSeasonFilter, value))
            {
                return;
            }

            _selectedSeasonFilter = value;
            OnPropertyChanged();
            ApplySeasonFilter();
        }
    }

    /// <summary>
    /// Aktuell markierter lokaler Kandidat, der erst nach einer bewussten Benutzeraktion übernommen wird.
    /// </summary>
    public ImdbEpisodeCandidate? SelectedLocalCandidate
    {
        get => _selectedLocalCandidate;
        set
        {
            if (ReferenceEquals(_selectedLocalCandidate, value))
            {
                return;
            }

            _selectedLocalCandidate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanApplyLocalCandidate));
        }
    }

    /// <summary>
    /// Erklärt, ob der lokale Index verfügbar ist und wie viele passende Kandidaten gefunden wurden.
    /// </summary>
    public string LocalDatasetStatusText
    {
        get => _localDatasetStatusText;
        private set
        {
            if (_localDatasetStatusText == value)
            {
                return;
            }

            _localDatasetStatusText = value;
            OnPropertyChanged();
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
        }
    }

    /// <summary>
    /// Manuell bestätigte IMDb-ID oder komplette IMDb-Titel-URL.
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
    /// Kurze Rückmeldung zur aktuell eingetragenen ID.
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

    public bool CanOpenSelectedSearch => SelectedSearchOption is not null;

    public bool CanApplyLocalCandidate => SelectedLocalCandidate is not null;

    /// <summary>
    /// Kennzeichnet eine laufende SQLite-Suche.
    /// </summary>
    public bool IsLocalSearchRunning
    {
        get => _isLocalSearchRunning;
        private set
        {
            if (_isLocalSearchRunning == value)
            {
                return;
            }

            _isLocalSearchRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRefreshLocalCandidates));
        }
    }

    /// <summary>
    /// Gibt an, ob die aktuell sichtbaren Serien- und Episodentexte lokal gesucht werden können.
    /// </summary>
    public bool CanRefreshLocalCandidates =>
        _imdbDatasetSearch?.IsAvailable == true
        && !IsLocalSearchRunning
        && !string.IsNullOrWhiteSpace(SeriesSearchText);

    public bool CanApply => TryNormalizeImdbId(ImdbInput, out _);

    /// <summary>
    /// Sucht lokalisierte Serienkandidaten und lädt anschließend den vollständigen Episodenkatalog
    /// der bevorzugten Serie. Änderungen während der Suche verwerfen veraltete Ergebnisse.
    /// </summary>
    public async Task RefreshLocalCandidatesAsync(CancellationToken cancellationToken = default)
    {
        if (_imdbDatasetSearch?.IsAvailable != true)
        {
            ReplaceItems(LocalSeriesCandidates, []);
            ReplaceItems(LocalCandidates, []);
            SelectedLocalCandidate = null;
            LocalDatasetStatusText = "Der lokale IMDb-Index ist nicht aktiviert oder noch nicht installiert.";
            OnPropertyChanged(nameof(CanRefreshLocalCandidates));
            return;
        }

        if (string.IsNullOrWhiteSpace(SeriesSearchText))
        {
            ReplaceItems(LocalSeriesCandidates, []);
            ReplaceItems(LocalCandidates, []);
            SelectedLocalCandidate = null;
            LocalDatasetStatusText = "Für die lokale Suche fehlt der Serienname.";
            OnPropertyChanged(nameof(CanRefreshLocalCandidates));
            return;
        }

        var searchRevision = _localSearchRevision;
        var previousSeriesId = SelectedLocalSeries?.ImdbId;
        IsLocalSearchRunning = true;
        LocalDatasetStatusText = "Lokale IMDb-Serien werden gesucht...";
        try
        {
            var seriesCandidates = await _imdbDatasetSearch.SearchSeriesCandidatesAsync(
                SeriesSearchText.Trim(),
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (searchRevision != _localSearchRevision)
            {
                return;
            }

            ReplaceItems(LocalSeriesCandidates, seriesCandidates);
            _suppressLocalSeriesRefresh = true;
            try
            {
                SelectedLocalSeries = LocalSeriesCandidates.FirstOrDefault(candidate =>
                                          string.Equals(candidate.ImdbId, previousSeriesId, StringComparison.OrdinalIgnoreCase))
                                      ?? LocalSeriesCandidates.FirstOrDefault();
            }
            finally
            {
                _suppressLocalSeriesRefresh = false;
            }

            if (SelectedLocalSeries is null)
            {
                _unfilteredLocalCandidates = [];
                ReplaceItems(LocalCandidates, []);
                ReplaceItems(SeasonFilters, []);
                SelectedLocalCandidate = null;
                LocalDatasetStatusText = "Keine passende IMDb-Serie gefunden. Seriennamen anpassen oder Browsersuche verwenden.";
                return;
            }

            await RefreshSelectedSeriesEpisodesAsync(searchRevision, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ReplaceItems(LocalCandidates, []);
            SelectedLocalCandidate = null;
            LocalDatasetStatusText = $"Lokale IMDb-Suche fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            if (searchRevision == _localSearchRevision)
            {
                IsLocalSearchRunning = false;
            }
        }
    }

    private async Task RefreshSelectedSeriesEpisodesAsync(
        int searchRevision,
        CancellationToken cancellationToken)
    {
        if (_imdbDatasetSearch is null || SelectedLocalSeries is not { } series)
        {
            _unfilteredLocalCandidates = [];
            ReplaceItems(LocalCandidates, []);
            ReplaceItems(SeasonFilters, []);
            return;
        }

        var previousEpisodeId = SelectedLocalCandidate?.ImdbId;
        var previousSeason = SelectedSeasonFilter?.SeasonNumber;
        IsLocalSearchRunning = true;
        LocalDatasetStatusText = $"Episoden von „{series.DisplayTitle}“ werden geladen...";
        var candidates = await _imdbDatasetSearch.BrowseSeriesEpisodesAsync(
            series,
            EpisodeSearchText.Trim(),
            seasonNumber: null,
            _guess?.SeasonNumber,
            _guess?.EpisodeNumber,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (searchRevision != _localSearchRevision || !ReferenceEquals(series, SelectedLocalSeries))
        {
            return;
        }

        _unfilteredLocalCandidates = candidates;
        RebuildSeasonFilters(previousSeason);
        ApplySeasonFilter(previousEpisodeId);
        LocalDatasetStatusText = BuildLocalDatasetStatusText();
    }

    public void MarkSelectedSearchOpened()
    {
        if (SelectedSearchOption is not null)
        {
            StatusText = $"IMDb-Suche geöffnet: {SelectedSearchOption.DisplayText}";
        }
    }

    public bool TryBuildImdbId(out string? imdbId, out string? validationMessage)
    {
        if (TryNormalizeImdbId(ImdbInput, out imdbId))
        {
            validationMessage = null;
            return true;
        }

        validationMessage = "Bitte eine IMDb-ID im Format tt1234567 oder eine komplette IMDb-Titel-URL eintragen.";
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

    /// <summary>
    /// Überträgt den ausgewählten Offline-Treffer in das weiterhin explizit zu bestätigende ID-Feld.
    /// </summary>
    public bool ApplySelectedLocalCandidate()
    {
        if (SelectedLocalCandidate is not { } candidate)
        {
            return false;
        }

        ImdbInput = candidate.ImdbId;
        StatusText = $"Lokalen IMDb-Treffer übernommen: {candidate.ImdbId}";
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

    private void SetSearchField(ref string field, string? value, [CallerMemberName] string? propertyName = null)
    {
        var normalized = value ?? string.Empty;
        if (field == normalized)
        {
            return;
        }

        field = normalized;
        OnPropertyChanged(propertyName);
        RebuildSearchOptions();
    }

    private void ScheduleLocalRefresh(bool refreshSeries, bool immediate = false)
    {
        _localSearchRevision++;
        _liveSearchCancellationSource?.Cancel();
        _liveSearchCancellationSource?.Dispose();
        _liveSearchCancellationSource = new CancellationTokenSource();
        var revision = _localSearchRevision;
        var cancellationToken = _liveSearchCancellationSource.Token;
        if (_imdbDatasetSearch?.IsAvailable != true)
        {
            LocalDatasetStatusText = "Der lokale IMDb-Index ist nicht aktiviert oder noch nicht installiert.";
        }
        else if (string.IsNullOrWhiteSpace(SeriesSearchText))
        {
            ReplaceItems(LocalSeriesCandidates, []);
            ReplaceItems(LocalCandidates, []);
            LocalDatasetStatusText = "Für die lokale Suche fehlt der Serienname.";
        }
        else
        {
            LocalDatasetStatusText = immediate
                ? "Lokale IMDb-Suche wird aktualisiert..."
                : "Eingabe erkannt; lokale IMDb-Suche wird gleich aktualisiert...";
            _pendingLiveSearch = RunScheduledLocalRefreshAsync(
                refreshSeries,
                immediate,
                revision,
                cancellationToken);
        }

        OnPropertyChanged(nameof(CanRefreshLocalCandidates));
    }

    private async Task RunScheduledLocalRefreshAsync(
        bool refreshSeries,
        bool immediate,
        int revision,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(LocalSearchDebounceDelay, cancellationToken);
            }

            if (revision != _localSearchRevision)
            {
                return;
            }

            if (refreshSeries)
            {
                await RefreshLocalCandidatesAsync(cancellationToken);
            }
            else
            {
                await RefreshSelectedSeriesEpisodesAsync(revision, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (revision == _localSearchRevision)
            {
                LocalDatasetStatusText = $"Lokale IMDb-Suche fehlgeschlagen: {ex.Message}";
            }
        }
        finally
        {
            if (revision == _localSearchRevision)
            {
                IsLocalSearchRunning = false;
            }
        }
    }

    private void SetStructuredSearchField(
        ref string field,
        string? value,
        bool refreshSeries,
        [CallerMemberName] string? propertyName = null)
    {
        var normalized = value ?? string.Empty;
        if (field == normalized)
        {
            return;
        }

        var previousCombinedQuery = BuildCombinedQuery();
        var searchTextWasDerived = string.Equals(SearchText.Trim(), previousCombinedQuery, StringComparison.Ordinal);
        field = normalized;
        OnPropertyChanged(propertyName);

        if (searchTextWasDerived)
        {
            _searchText = BuildCombinedQuery();
            OnPropertyChanged(nameof(SearchText));
        }

        RebuildSearchOptions();
        ScheduleLocalRefresh(refreshSeries || SelectedLocalSeries is null);
    }

    private void RebuildSeasonFilters(int? preferredSeason)
    {
        var availableSeasons = _unfilteredLocalCandidates
            .Where(candidate => candidate.SeasonNumber is not null)
            .Select(candidate => candidate.SeasonNumber!.Value)
            .Distinct()
            .OrderBy(number => number)
            .ToArray();
        var filters = new List<ImdbSeasonFilterOption> { new(null, "Alle Staffeln") };
        filters.AddRange(availableSeasons.Select(number => new ImdbSeasonFilterOption(number, $"Staffel {number}")));
        ReplaceItems(SeasonFilters, filters);

        var guessedSeason = int.TryParse(_guess?.SeasonNumber, out var parsedGuess) ? parsedGuess : (int?)null;
        SelectedSeasonFilter = SeasonFilters.FirstOrDefault(filter => filter.SeasonNumber == preferredSeason)
                               ?? SeasonFilters.FirstOrDefault(filter => filter.SeasonNumber == guessedSeason)
                               ?? SeasonFilters.FirstOrDefault();
    }

    private void ApplySeasonFilter(string? preferredEpisodeId = null)
    {
        var seasonNumber = SelectedSeasonFilter?.SeasonNumber;
        var filtered = _unfilteredLocalCandidates.Where(candidate =>
            seasonNumber is null || candidate.SeasonNumber == seasonNumber);
        ReplaceItems(LocalCandidates, filtered);
        SelectedLocalCandidate = LocalCandidates.FirstOrDefault(candidate =>
                                     string.Equals(candidate.ImdbId, preferredEpisodeId, StringComparison.OrdinalIgnoreCase))
                                 ?? LocalCandidates.FirstOrDefault();
        if (!IsLocalSearchRunning && SelectedLocalSeries is not null)
        {
            LocalDatasetStatusText = BuildLocalDatasetStatusText();
        }
    }

    private string BuildLocalDatasetStatusText()
    {
        if (SelectedLocalSeries is null)
        {
            return "Keine IMDb-Serie ausgewählt.";
        }

        var scope = SelectedSeasonFilter?.SeasonNumber is { } season
            ? $"Staffel {season}"
            : "alle Staffeln";
        return $"{LocalCandidates.Count} Episode(n) aus {scope}; ähnliche Titel stehen oben.";
    }

    private void RebuildSearchOptions()
    {
        var previousTarget = SelectedSearchOption?.TargetUrl;
        ReplaceItems(SearchOptions, BuildSearchOptions());
        SelectedSearchOption = SearchOptions.FirstOrDefault(option =>
                                   string.Equals(option.TargetUrl, previousTarget, StringComparison.OrdinalIgnoreCase))
                               ?? SearchOptions.FirstOrDefault();
    }

    private IReadOnlyList<SearchOptionItem> BuildSearchOptions()
    {
        var options = new List<SearchOptionItem>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (TryNormalizeImdbId(ImdbInput, out var currentImdbId))
        {
            AddOption(options, seenTargets, "Aktuellen IMDb-Eintrag öffnen", BuildImdbTitleUrl(currentImdbId!),
                "Öffnet direkt den derzeit eingetragenen IMDb-Titel.");
        }

        var combinedQuery = BuildCombinedQuery();
        AddSearchOption(options, seenTargets, "Serie + Episodentitel", combinedQuery,
            "Sucht nach Serienname und Episodentitel.");

        AddSearchOption(options, seenTargets, "Suchtext", SearchText,
            "Öffnet die IMDb-Titelsuche mit dem frei editierbaren Suchtext.");

        if (_guess is not null)
        {
            var episodeCode = EpisodeFileNameHelper.BuildEpisodeCode(_guess.SeasonNumber, _guess.EpisodeNumber);
            if (!episodeCode.Contains("xx", StringComparison.OrdinalIgnoreCase))
            {
                AddSearchOption(options, seenTargets, "Serie + Episodencode",
                    $"{SeriesSearchText} {episodeCode} {EpisodeSearchText}",
                    "Alternative Suche mit Staffel und Folge; IMDb kann eine abweichende Nummerierung verwenden.");
            }
        }

        return options;
    }

    private string BuildCombinedQuery() => string.Join(" ", new[] { SeriesSearchText.Trim(), EpisodeSearchText.Trim() }
        .Where(part => !string.IsNullOrWhiteSpace(part)));

    private static void AddSearchOption(
        ICollection<SearchOptionItem> options,
        ISet<string> seenTargets,
        string displayText,
        string query,
        string description)
    {
        if (!string.IsNullOrWhiteSpace(query))
        {
            AddOption(options, seenTargets, displayText, BuildImdbSearchUrl(query.Trim()), description);
        }
    }

    private static void AddOption(
        ICollection<SearchOptionItem> options,
        ISet<string> seenTargets,
        string displayText,
        string targetUrl,
        string description)
    {
        if (seenTargets.Add(targetUrl))
        {
            options.Add(new SearchOptionItem(displayText, targetUrl, description));
        }
    }

    private void UpdateComparisonSummary()
    {
        ComparisonSummaryText = TryNormalizeImdbId(ImdbInput, out var imdbId)
            ? $"Aktuelle IMDb-ID: {imdbId}"
            : "Noch keine gültige IMDb-ID eingetragen.";
    }

    private static string BuildDefaultSearchText(EpisodeMetadataGuess? guess) => guess is null
        ? string.Empty
        : $"{guess.SeriesName} {guess.EpisodeTitle}".Trim();

    private static string BuildInitialStatusText(EpisodeMetadataGuess? guess, string currentImdbId)
    {
        if (TryNormalizeImdbId(currentImdbId, out var normalizedImdbId))
        {
            return $"Bereits eingetragen: {normalizedImdbId}";
        }

        return guess is null
            ? "Browserhilfe ohne automatische Vorbelegung vorbereitet."
            : "Browserhilfe aus der lokalen Erkennung vorbereitet.";
    }

    private static string BuildGuessSummaryText(EpisodeMetadataGuess? guess, string? currentImdbId)
    {
        var currentImdbInfo = TryNormalizeImdbId(currentImdbId, out var normalizedImdbId)
            ? $"Aktuell eingetragen: {normalizedImdbId}"
            : "Aktuell eingetragen: keine IMDb-ID";
        if (guess is null)
        {
            return $"{currentImdbInfo}{Environment.NewLine}Die Datei liefert keine automatische Serien-/Episodenvorbelegung.";
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

    private static bool IsSupportedImdbHost(string host) =>
        string.Equals(host, "imdb.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".imdb.com", StringComparison.OrdinalIgnoreCase);

    private static string BuildImdbSearchUrl(string query) =>
        $"https://www.imdb.com/find/?q={Uri.EscapeDataString(query)}&s=tt&ttype=ep&ref_=fn_tt_ex";

    private static string BuildImdbTitleUrl(string imdbId) => $"https://www.imdb.com/title/{imdbId}/";

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Beendet eine noch ausstehende verzögerte Offline-Suche beim Schließen des Dialogs.
    /// </summary>
    public void Dispose()
    {
        _liveSearchCancellationSource?.Cancel();
        _liveSearchCancellationSource?.Dispose();
        _liveSearchCancellationSource = null;
    }

    /// <summary>
    /// Ermöglicht deterministische Tests der verzögerten Live-Suche, ohne die Verzögerungsdauer zu duplizieren.
    /// </summary>
    internal Task WaitForPendingLocalSearchAsync() => _pendingLiveSearch;

    public sealed record SearchOptionItem(string DisplayText, string TargetUrl, string Description);

    public sealed record ImdbSeasonFilterOption(int? SeasonNumber, string DisplayText);
}
