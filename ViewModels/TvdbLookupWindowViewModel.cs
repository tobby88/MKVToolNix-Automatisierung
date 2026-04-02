using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels;

/// <summary>
/// Kapselt Zustand und Suchlogik des TVDB-Dialogs, damit das Fenster selbst nur noch UI-Ereignisse weiterreicht.
/// </summary>
public sealed partial class TvdbLookupWindowViewModel : INotifyPropertyChanged
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

    /// <summary>
    /// Initialisiert das ViewModel für den manuellen TVDB-Abgleich einer Episode.
    /// </summary>
    /// <param name="lookupService">Service für Settings, Suche und Episodenabgleich.</param>
    /// <param name="guess">Lokal erkannter Ausgangspunkt für Serie, Staffel, Folge und Titel.</param>
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
        GuessSummaryText = TvdbLookupWindowTextFormatter.BuildGuessSummaryText(_guess);
    }

    /// <summary>
    /// Benachrichtigt die UI über geänderte Bindungswerte.
    /// </summary>
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
            TvdbLookupWindowTextFormatter.FormatTvdbNumber(SelectedEpisodeItem.Episode.SeasonNumber),
            TvdbLookupWindowTextFormatter.FormatTvdbNumber(SelectedEpisodeItem.Episode.EpisodeNumber));
        return true;
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

    private void UpdateComparisonSummary()
    {
        ComparisonSummaryText = TvdbLookupWindowTextFormatter.BuildComparisonSummaryText(
            _guess,
            SelectedSeriesItem?.Series,
            SelectedEpisodeItem?.Episode);
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
}
