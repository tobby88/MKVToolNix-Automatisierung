using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels;

/// <summary>
/// Kapselt den browsergestützten IMDb-Abgleich für eine einzelne Emby-Zeile.
/// </summary>
internal sealed class ImdbLookupWindowViewModel : INotifyPropertyChanged
{
    private static readonly Regex ImdbIdPattern = new(@"tt\d{7,10}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly EpisodeMetadataGuess? _guess;
    private string _searchText;
    private string _imdbInput;
    private string _statusText;
    private SearchOptionItem? _selectedSearchOption;

    /// <summary>
    /// Initialisiert das ViewModel mit lokaler Dateinamenschätzung und optional bereits vorhandener IMDb-ID.
    /// </summary>
    internal ImdbLookupWindowViewModel(EpisodeMetadataGuess? guess, string? currentImdbId)
    {
        _guess = guess;
        _searchText = BuildDefaultSearchText(guess);
        _imdbInput = currentImdbId ?? string.Empty;
        _statusText = BuildInitialStatusText(guess, currentImdbId);
        GuessSummaryText = BuildGuessSummaryText(guess, currentImdbId);
        RebuildSearchOptions();
    }

    /// <summary>
    /// Benachrichtigt die UI über geänderte Bindungswerte.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Zeigt die lokale Ausgangslage für Serie, Episode und bereits bekannte IMDb-ID an.
    /// </summary>
    public string GuessSummaryText { get; }

    /// <summary>
    /// Frei anpassbarer Suchtext für die IMDb-Browserhilfe.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_searchText == normalized)
            {
                return;
            }

            _searchText = normalized;
            OnPropertyChanged();
            RebuildSearchOptions();
        }
    }

    /// <summary>
    /// Manuell bestätigte IMDb-ID oder eine komplette IMDb-URL, aus der die ID extrahiert werden kann.
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
        }
    }

    /// <summary>
    /// Liste der im Browser zu öffnenden IMDb-Suchhilfen.
    /// </summary>
    public ObservableCollection<SearchOptionItem> SearchOptions { get; } = [];

    /// <summary>
    /// Aktuell ausgewählte Suchhilfe.
    /// </summary>
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
    /// Sichtbarer Statustext des Dialogs.
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
    /// Aktiviert den Browser-Button nur bei ausgewählter Suchhilfe.
    /// </summary>
    public bool CanOpenSelectedSearch => SelectedSearchOption is not null;

    /// <summary>
    /// Aktiviert die Übernahme, sobald aus der Eingabe eine valide IMDb-ID extrahiert werden kann.
    /// </summary>
    public bool CanApply => TryNormalizeImdbId(ImdbInput, out _);

    /// <summary>
    /// Öffnet die aktuell ausgewählte Suchhilfe im Browser.
    /// </summary>
    public void MarkSelectedSearchOpened()
    {
        if (SelectedSearchOption is null)
        {
            return;
        }

        StatusText = $"IMDb-Suche geöffnet: {SelectedSearchOption.DisplayText}";
    }

    /// <summary>
    /// Baut aus der aktuellen Eingabe die zu übernehmende IMDb-ID.
    /// </summary>
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

    /// <summary>
    /// Übernimmt eine IMDb-ID oder IMDb-URL aus der Zwischenablage und normalisiert sie sofort.
    /// </summary>
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

    private void RebuildSearchOptions()
    {
        var items = BuildSearchOptions(_guess, SearchText, ImdbInput);
        SearchOptions.Clear();
        foreach (var item in items)
        {
            SearchOptions.Add(item);
        }

        SelectedSearchOption = SearchOptions.FirstOrDefault();
        if (SearchOptions.Count == 0)
        {
            StatusText = "Keine IMDb-Suchhilfe verfügbar. Suchtext anpassen oder IMDb-ID direkt eintragen.";
        }
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
                    "Hilfreich, wenn IMDb die Episode eher über S/E statt über den Titel findet."));
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

    private static string BuildImdbSearchUrl(string query)
    {
        return $"https://www.imdb.com/find/?q={Uri.EscapeDataString(query)}&s=tt&ttype=ep&ref_=fn_tt_ex";
    }

    private static string BuildImdbTitleUrl(string imdbId)
    {
        return $"https://www.imdb.com/title/{imdbId}/";
    }

    private static string BuildDefaultSearchText(EpisodeMetadataGuess? guess)
    {
        return guess is null
            ? string.Empty
            : $"{guess.SeriesName} {guess.EpisodeTitle}".Trim();
    }

    private static string BuildInitialStatusText(EpisodeMetadataGuess? guess, string? currentImdbId)
    {
        if (TryNormalizeImdbId(currentImdbId, out var normalizedImdbId))
        {
            return $"Bereits eingetragen: {normalizedImdbId}";
        }

        return guess is null
            ? "Keine automatische IMDb-Vorbelegung vorhanden."
            : "IMDb-Suchhilfe aus der lokalen Erkennung vorbereitet.";
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

    internal static bool TryNormalizeImdbId(string? input, out string? imdbId)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            imdbId = null;
            return false;
        }

        var match = ImdbIdPattern.Match(normalized);
        if (!match.Success)
        {
            imdbId = null;
            return false;
        }

        imdbId = match.Value.ToLowerInvariant();
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Sichtbare Suchhilfezeile des IMDb-Dialogs.
    /// </summary>
    public sealed record SearchOptionItem(string DisplayText, string TargetUrl, string Description);
}
