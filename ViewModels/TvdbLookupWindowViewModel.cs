using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels;

/// <summary>
/// Kapselt Zustand und Suchlogik des TVDB-Dialogs, damit das Fenster selbst nur noch UI-Ereignisse weiterreicht.
/// </summary>
public sealed class TvdbLookupWindowViewModel : INotifyPropertyChanged
{
    private readonly EpisodeMetadataLookupService _lookupService;
    private readonly EpisodeMetadataGuess _guess;
    private readonly List<TvdbSeriesSearchResult> _seriesResults = [];
    private readonly List<TvdbEpisodeRecord> _episodes = [];
    private bool _isBusy;
    private bool _isInitialized;
    private bool _suppressSeriesSelectionChanged;
    private string _apiKey;
    private string _pin;
    private string _seriesSearchText;
    private string _episodeSearchText;
    private string _comparisonSummaryText;
    private string _statusText = "Bereit";
    private SelectableSeriesItem? _selectedSeriesItem;
    private SelectableEpisodeItem? _selectedEpisodeItem;

    public TvdbLookupWindowViewModel(EpisodeMetadataLookupService lookupService, EpisodeMetadataGuess guess)
    {
        _lookupService = lookupService;
        _guess = guess;

        var settings = _lookupService.LoadSettings();
        _apiKey = settings.TvdbApiKey;
        _pin = settings.TvdbPin;
        _seriesSearchText = guess.SeriesName;
        _episodeSearchText = guess.EpisodeTitle;
        _comparisonSummaryText = "Noch kein TVDB-Treffer ausgewählt.";
        GuessSummaryText = BuildGuessSummaryText();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Vorbelegung aus lokal erkannten Serien- und Episodendaten.
    /// </summary>
    public string GuessSummaryText { get; }

    /// <summary>
    /// Aktuell eingetragener TVDB-API-Key.
    /// </summary>
    public string ApiKey
    {
        get => _apiKey;
        set
        {
            if (_apiKey == value)
            {
                return;
            }

            _apiKey = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Optional eingetragene TVDB-PIN.
    /// </summary>
    public string Pin
    {
        get => _pin;
        set
        {
            if (_pin == value)
            {
                return;
            }

            _pin = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Suchtext für TVDB-Serien.
    /// </summary>
    public string SeriesSearchText
    {
        get => _seriesSearchText;
        set
        {
            if (_seriesSearchText == value)
            {
                return;
            }

            _seriesSearchText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Filtertext für die Episodenliste der gewählten Serie.
    /// </summary>
    public string EpisodeSearchText
    {
        get => _episodeSearchText;
        set
        {
            if (_episodeSearchText == value)
            {
                return;
            }

            _episodeSearchText = value;
            OnPropertyChanged();
            ApplyEpisodeFilter(autoSelectBest: false);
        }
    }

    /// <summary>
    /// Sichtbare Serie-Trefferliste.
    /// </summary>
    public ObservableCollection<SelectableSeriesItem> SeriesResults { get; } = [];

    /// <summary>
    /// Sichtbare Episodenliste der aktuell gewählten Serie.
    /// </summary>
    public ObservableCollection<SelectableEpisodeItem> EpisodeResults { get; } = [];

    /// <summary>
    /// Aktuell markierte TVDB-Serie.
    /// </summary>
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
            OnPropertyChanged(nameof(CanApply));

            if (!_suppressSeriesSelectionChanged)
            {
                SelectedEpisodeItem = null;
                ReplaceItems(EpisodeResults, []);
                UpdateComparisonSummary();
            }
        }
    }

    /// <summary>
    /// Aktuell markierte TVDB-Episode.
    /// </summary>
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
            OnPropertyChanged(nameof(CanApply));
            UpdateComparisonSummary();
        }
    }

    /// <summary>
    /// Textuelle Gegenüberstellung zwischen lokaler Erkennung und aktueller TVDB-Auswahl.
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

    /// <summary>
    /// Laufender Statustext des Dialogs.
    /// </summary>
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

    /// <summary>
    /// Kennzeichnet einen aktiven Netzwerk- oder Ladezyklus.
    /// </summary>
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

    /// <summary>
    /// Ermöglicht kompakte UI-Bindings für deaktivierbare Bereiche.
    /// </summary>
    public bool IsInteractive => !IsBusy;

    /// <summary>
    /// Aktiviert die Übernehmen-Aktion erst, wenn Serie und Episode ausgewählt sind.
    /// </summary>
    public bool CanApply => SelectedSeriesItem is not null && SelectedEpisodeItem is not null;

    /// <summary>
    /// Lädt beim ersten Öffnen direkt die vorbefüllte TVDB-Suche.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await SearchSeriesAsync(autoLoadEpisodes: true);
    }

    /// <summary>
    /// Startet die TVDB-Seriensuche mit den aktuell sichtbaren Zugangsdaten und Suchfeldern.
    /// </summary>
    /// <param name="autoLoadEpisodes">Lädt nach erfolgreicher Seriensuche direkt die Episodenliste der bevorzugten Serie.</param>
    public async Task SearchSeriesAsync(bool autoLoadEpisodes)
    {
        try
        {
            SetBusy(true, "Suche Serie bei TVDB...");
            var currentSettings = BuildTransientSettings();
            var results = await _lookupService.SearchSeriesAsync(SeriesSearchText.Trim(), currentSettings);

            _seriesResults.Clear();
            _seriesResults.AddRange(results);
            ReplaceItems(SeriesResults, results.Select(result => new SelectableSeriesItem(result)));

            _episodes.Clear();
            ReplaceItems(EpisodeResults, []);
            SelectedEpisodeItem = null;

            if (_seriesResults.Count == 0)
            {
                SelectedSeriesItem = null;
                StatusText = "Keine passende Serie gefunden.";
                UpdateComparisonSummary();
                return;
            }

            var preferredSeries = _lookupService.FindPreferredSeriesResult(_guess, _seriesResults) ?? _seriesResults[0];
            var preferredItem = SeriesResults.FirstOrDefault(result => result.Series.Id == preferredSeries.Id) ?? SeriesResults[0];

            _suppressSeriesSelectionChanged = true;
            SelectedSeriesItem = preferredItem;
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
        catch
        {
            StatusText = "TVDB-Suche fehlgeschlagen";
            throw;
        }
        finally
        {
            SetBusy(false, StatusText);
        }
    }

    /// <summary>
    /// Reagiert auf eine manuell geänderte Serienauswahl und lädt die Episoden der gewählten Serie.
    /// </summary>
    public async Task HandleSelectedSeriesSelectionChangedAsync()
    {
        if (_suppressSeriesSelectionChanged || IsBusy || SelectedSeriesItem is null)
        {
            return;
        }

        await LoadEpisodesForSelectedSeriesAsync(autoSelectBest: true);
    }

    /// <summary>
    /// Persistiert die aktuell sichtbaren Zugangsdaten, ohne den Dialog zu schließen.
    /// </summary>
    public void SaveSettings()
    {
        _lookupService.SaveSettings(BuildTransientSettings());
        StatusText = $"TVDB-Einstellungen gespeichert: {_lookupService.SettingsFilePath}";
    }

    /// <summary>
    /// Behält die lokale Erkennung bei, speichert aber vorher mögliche Credential-Änderungen.
    /// </summary>
    public void RememberLocalDetectionChoice()
    {
        _lookupService.SaveSettings(BuildTransientSettings());
    }

    /// <summary>
    /// Baut aus der aktuellen Serien-/Episodenauswahl die Rückgabe für den aufrufenden Workflow.
    /// </summary>
    /// <param name="selection">Fertige TVDB-Auswahl bei Erfolg.</param>
    /// <param name="validationMessage">Benutzerfreundlicher Hinweis, falls noch eine Auswahl fehlt.</param>
    /// <returns><see langword="true"/>, wenn die Auswahl vollständig ist.</returns>
    public bool TryBuildSelection(
        out TvdbEpisodeSelection? selection,
        out string? validationMessage)
    {
        selection = null;
        validationMessage = null;

        if (SelectedSeriesItem is null)
        {
            validationMessage = "Bitte zuerst eine Serie auswählen.";
            return false;
        }

        if (SelectedEpisodeItem is null)
        {
            validationMessage = "Bitte zuerst eine Episode auswählen.";
            return false;
        }

        _lookupService.SaveSettings(BuildTransientSettings());
        _lookupService.SaveSeriesMapping(_guess.SeriesName, SelectedSeriesItem.Series);

        selection = new TvdbEpisodeSelection(
            SelectedSeriesItem.Series.Id,
            SelectedSeriesItem.Series.Name,
            SelectedEpisodeItem.Episode.Id,
            SelectedEpisodeItem.Episode.Name,
            FormatNumber(SelectedEpisodeItem.Episode.SeasonNumber),
            FormatNumber(SelectedEpisodeItem.Episode.EpisodeNumber));
        return true;
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
            var currentSettings = BuildTransientSettings();
            var episodes = await _lookupService.LoadEpisodesAsync(SelectedSeriesItem.Series.Id, currentSettings);

            _episodes.Clear();
            _episodes.AddRange(episodes);
            ApplyEpisodeFilter(autoSelectBest);

            if (SelectedEpisodeItem is null)
            {
                StatusText = $"{_episodes.Count} Episode(n) geladen.";
            }
        }
        catch
        {
            StatusText = "Episodenliste konnte nicht geladen werden";
            throw;
        }
        finally
        {
            SetBusy(false, StatusText);
        }
    }

    private void ApplyEpisodeFilter(bool autoSelectBest)
    {
        var searchText = EpisodeSearchText.Trim();
        var normalizedSearchText = NormalizeTextForSearch(searchText);
        var filteredEpisodes = string.IsNullOrWhiteSpace(searchText)
            ? _episodes.ToList()
            : _episodes
                .Where(episode => EpisodeMatchesSearch(episode, searchText, normalizedSearchText))
                .ToList();

        var items = filteredEpisodes
            .OrderBy(episode => episode.SeasonNumber ?? int.MaxValue)
            .ThenBy(episode => episode.EpisodeNumber ?? int.MaxValue)
            .Select(episode => new SelectableEpisodeItem(episode))
            .ToList();

        var previouslySelectedEpisodeId = SelectedEpisodeItem?.Episode.Id;
        ReplaceItems(EpisodeResults, items);
        SelectedEpisodeItem = null;

        if (!autoSelectBest || SelectedSeriesItem is null)
        {
            if (previouslySelectedEpisodeId is int episodeId)
            {
                SelectedEpisodeItem = EpisodeResults.FirstOrDefault(item => item.Episode.Id == episodeId);
            }

            UpdateComparisonSummary();
            return;
        }

        var match = _lookupService.FindBestEpisodeMatch(_guess, SelectedSeriesItem.Series, _episodes);
        if (match is null)
        {
            StatusText = "Keine Episode automatisch sicher vorgewählt.";
            UpdateComparisonSummary();
            return;
        }

        SelectedEpisodeItem = EpisodeResults.FirstOrDefault(item => item.Episode.Id == match.TvdbEpisodeId);
        if (SelectedEpisodeItem is not null)
        {
            StatusText = $"TVDB-Vorschlag: S{match.SeasonNumber}E{match.EpisodeNumber} - {match.EpisodeTitle}";
        }

        UpdateComparisonSummary();
    }

    private static bool EpisodeMatchesSearch(TvdbEpisodeRecord episode, string rawSearchText, string normalizedSearchText)
    {
        if (episode.Name.Contains(rawSearchText, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(normalizedSearchText))
        {
            return false;
        }

        // Zusätzliche Tokens decken typische Suchmuster wie S01E02 oder 01x02 ab, ohne die eigentliche Titelsuche zu verdrängen.
        return BuildEpisodeSearchTokens(episode).Any(token =>
            NormalizeTextForSearch(token).Contains(normalizedSearchText, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> BuildEpisodeSearchTokens(TvdbEpisodeRecord episode)
    {
        var seasonNumber = FormatNumber(episode.SeasonNumber);
        var episodeNumber = FormatNumber(episode.EpisodeNumber);

        yield return $"s{seasonNumber}e{episodeNumber}";
        yield return $"{seasonNumber}x{episodeNumber}";
        yield return $"staffel {seasonNumber} folge {episodeNumber}";
    }

    private static string NormalizeTextForSearch(string value)
    {
        // Vereinheitlicht Eingaben wie "S01-E02" oder "Staffel 1, Folge 2" auf einen robust vergleichbaren Kern.
        return string.Concat(value
            .Where(character => char.IsLetterOrDigit(character))
            .Select(char.ToLowerInvariant));
    }

    private AppMetadataSettings BuildTransientSettings()
    {
        return new AppMetadataSettings
        {
            TvdbApiKey = ApiKey.Trim(),
            TvdbPin = Pin.Trim(),
            SeriesMappings = _lookupService.LoadSettings().SeriesMappings
        };
    }

    private string BuildGuessSummaryText()
    {
        return $"Lokal erkannt: {_guess.SeriesName} - S{NormalizeNumber(_guess.SeasonNumber)}E{NormalizeNumber(_guess.EpisodeNumber)} - {_guess.EpisodeTitle}";
    }

    private void UpdateComparisonSummary()
    {
        if (SelectedSeriesItem is null)
        {
            ComparisonSummaryText = "Noch keine TVDB-Serie ausgewählt.";
            return;
        }

        if (SelectedEpisodeItem is null)
        {
            ComparisonSummaryText = "Noch keine TVDB-Episode ausgewählt.";
            return;
        }

        var selectedSeries = SelectedSeriesItem.Series;
        var selectedSeason = FormatNumber(SelectedEpisodeItem.Episode.SeasonNumber);
        var selectedEpisodeNumber = FormatNumber(SelectedEpisodeItem.Episode.EpisodeNumber);
        var differences = new List<string>();

        if (!string.Equals(_guess.SeriesName.Trim(), selectedSeries.Name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Serie: lokal '{_guess.SeriesName}' -> TVDB '{selectedSeries.Name}'");
        }

        if (!string.Equals(NormalizeNumber(_guess.SeasonNumber), selectedSeason, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Staffel: lokal '{NormalizeNumber(_guess.SeasonNumber)}' -> TVDB '{selectedSeason}'");
        }

        if (!string.Equals(NormalizeNumber(_guess.EpisodeNumber), selectedEpisodeNumber, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Folge: lokal '{NormalizeNumber(_guess.EpisodeNumber)}' -> TVDB '{selectedEpisodeNumber}'");
        }

        if (!string.Equals(_guess.EpisodeTitle.Trim(), SelectedEpisodeItem.Episode.Name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Titel: lokal '{_guess.EpisodeTitle}' -> TVDB '{SelectedEpisodeItem.Episode.Name}'");
        }

        ComparisonSummaryText = differences.Count == 0
            ? "TVDB stimmt mit der lokalen Erkennung überein."
            : "Abweichungen: " + string.Join(" | ", differences);
    }

    private void SetBusy(bool isBusy, string statusText)
    {
        IsBusy = isBusy;
        StatusText = statusText;
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static string NormalizeNumber(string value)
    {
        return int.TryParse(value, out var number) && number >= 0 ? number.ToString("00") : "xx";
    }

    private static string FormatNumber(int? value)
    {
        return value is null or <= 0 ? "xx" : value.Value.ToString("00");
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// UI-taugliche Serienzeile für die linke Trefferliste.
    /// </summary>
    public sealed class SelectableSeriesItem
    {
        public SelectableSeriesItem(TvdbSeriesSearchResult series)
        {
            Series = series;
        }

        public TvdbSeriesSearchResult Series { get; }

        public string DisplayText => string.IsNullOrWhiteSpace(Series.Year)
            ? $"{Series.Name} (ID {Series.Id})"
            : $"{Series.Name} ({Series.Year}) - ID {Series.Id}";
    }

    /// <summary>
    /// UI-taugliche Episodenzeile für die rechte Ergebnisliste.
    /// </summary>
    public sealed class SelectableEpisodeItem
    {
        public SelectableEpisodeItem(TvdbEpisodeRecord episode)
        {
            Episode = episode;
        }

        public TvdbEpisodeRecord Episode { get; }

        public string DisplayText => $"S{FormatNumber(Episode.SeasonNumber)}E{FormatNumber(Episode.EpisodeNumber)} - {Episode.Name}";
    }
}
