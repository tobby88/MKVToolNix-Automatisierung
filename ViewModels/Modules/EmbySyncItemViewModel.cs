using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Bindbare Zeile des Emby-Abgleichs für eine neu erzeugte MKV.
/// </summary>
internal sealed class EmbySyncItemViewModel : INotifyPropertyChanged, IDataErrorInfo
{
    private static readonly Regex EpisodeFileNamePattern = new(
        @"^\s*(?<series>.+?)\s+-\s+S(?<season>\d{2,4}|xx)E(?<episode>\d{2,4}(?:-E\d{2,4})?|xx)\s+-\s+(?<title>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TvdbIdPattern = new(@"^\d+$", RegexOptions.Compiled);
    private static readonly Regex ImdbIdPattern = new(@"^tt\d{7,10}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> EmbyAssetFoldersWithoutEpisodeNfo = new(StringComparer.OrdinalIgnoreCase)
    {
        "trailers",
        "backdrops"
    };
    private readonly EmbyProviderIds _reportedProviderIds;
    private readonly EpisodeMetadataGuess? _metadataGuess;
    private bool _isSelected = true;
    private bool _supportsProviderIdSync = true;
    private string _tvdbId = string.Empty;
    private string _imdbId = string.Empty;
    private string _embyItemId = string.Empty;
    private string _statusText = "Noch nicht geprüft";
    private string _note = string.Empty;

    public EmbySyncItemViewModel(string mediaFilePath)
        : this(mediaFilePath, EmbyProviderIds.Empty)
    {
    }

    public EmbySyncItemViewModel(string mediaFilePath, EmbyProviderIds providerIds)
    {
        MediaFilePath = mediaFilePath;
        NfoPath = Path.ChangeExtension(mediaFilePath, ".nfo");
        _reportedProviderIds = providerIds ?? EmbyProviderIds.Empty;
        _tvdbId = _reportedProviderIds.TvdbId ?? string.Empty;
        _imdbId = _reportedProviderIds.ImdbId ?? string.Empty;
        _metadataGuess = TryParseMetadataGuess(mediaFilePath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string MediaFilePath { get; }

    public string MediaFileName => Path.GetFileName(MediaFilePath);

    public string NfoPath { get; private set; }

    /// <summary>
    /// Kennzeichnet, ob bereits eine TVDB-ID gesetzt ist.
    /// </summary>
    public bool HasTvdbId => !string.IsNullOrWhiteSpace(TvdbId);

    /// <summary>
    /// Kennzeichnet, ob bereits eine IMDb-ID gesetzt ist.
    /// </summary>
    public bool HasImdbId => !string.IsNullOrWhiteSpace(ImdbId);

    /// <summary>
    /// Kennzeichnet, ob eine vorhandene TVDB-ID dem erwarteten Zielformat entspricht.
    /// </summary>
    public bool HasValidTvdbId => string.IsNullOrWhiteSpace(TvdbId) || TvdbIdPattern.IsMatch(TvdbId);

    /// <summary>
    /// Kennzeichnet, ob eine vorhandene IMDb-ID dem erwarteten Zielformat entspricht.
    /// </summary>
    public bool HasValidImdbId => string.IsNullOrWhiteSpace(ImdbId) || ImdbIdPattern.IsMatch(ImdbId);

    /// <summary>
    /// Kennzeichnet, ob für diese Zeile überhaupt ein lokaler NFO-Sync fachlich sinnvoll ist.
    /// </summary>
    public bool SupportsProviderIdSync
    {
        get => _supportsProviderIdSync;
        private set
        {
            if (_supportsProviderIdSync == value)
            {
                return;
            }

            _supportsProviderIdSync = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanReviewTvdb));
            OnPropertyChanged(nameof(CanReviewImdb));
            OnPropertyChanged(nameof(CanEditProviderIds));
            OnPropertyChanged(nameof(ProviderIdEditTooltip));
            OnPropertyChanged(nameof(TvdbLookupTooltip));
            OnPropertyChanged(nameof(ImdbLookupTooltip));
        }
    }

    public string TvdbId
    {
        get => _tvdbId;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_tvdbId == normalized)
            {
                return;
            }

            _tvdbId = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTvdbId));
            OnPropertyChanged(nameof(HasValidTvdbId));
            OnPropertyChanged(nameof(HasProviderIds));
            OnPropertyChanged(nameof(HasValidProviderIds));
            OnPropertyChanged(nameof(HasCompleteProviderIds));
        }
    }

    public string ImdbId
    {
        get => _imdbId;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_imdbId == normalized)
            {
                return;
            }

            _imdbId = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImdbId));
            OnPropertyChanged(nameof(HasValidImdbId));
            OnPropertyChanged(nameof(HasProviderIds));
            OnPropertyChanged(nameof(HasValidProviderIds));
            OnPropertyChanged(nameof(HasCompleteProviderIds));
        }
    }

    public string EmbyItemId
    {
        get => _embyItemId;
        private set
        {
            if (_embyItemId == value)
            {
                return;
            }

            _embyItemId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusTooltip));
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

    public string Note
    {
        get => _note;
        private set
        {
            if (_note == value)
            {
                return;
            }

            _note = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusTooltip));
        }
    }

    public bool HasProviderIds => !string.IsNullOrWhiteSpace(TvdbId) || !string.IsNullOrWhiteSpace(ImdbId);

    /// <summary>
    /// Für einen vollständigen Emby-Abgleich werden derzeit sowohl TVDB- als auch IMDb-ID erwartet.
    /// </summary>
    public bool HasCompleteProviderIds => HasTvdbId && HasImdbId && HasValidProviderIds;

    /// <summary>
    /// Kennzeichnet, ob alle aktuell befüllten Provider-ID-Felder formal gültig sind.
    /// </summary>
    public bool HasValidProviderIds => GetProviderIdValidationMessage() is null;

    /// <summary>
    /// Sichtbare TVDB-Suche ist nur dann sinnvoll, wenn der Dateiname vorbefüllt werden kann und
    /// die Zeile später tatsächlich in eine Episoden-NFO zurückgeschrieben wird.
    /// </summary>
    public bool CanReviewTvdb => SupportsProviderIdSync && _metadataGuess is not null;

    /// <summary>
    /// Die IMDb-Prüfung bleibt auch ohne perfekte Vorbelegung sinnvoll, weil der Dialog notfalls
    /// immer noch eine Browser-Suche und das manuelle Einfügen einer IMDb-ID anbietet.
    /// </summary>
    public bool CanReviewImdb => SupportsProviderIdSync;

    /// <summary>
    /// Kennzeichnet die gelb hinterlegten TVDB-/IMDB-Felder als aktiv editierbar.
    /// </summary>
    public bool CanEditProviderIds => SupportsProviderIdSync;

    public string ProviderIdEditTooltip => SupportsProviderIdSync
        ? "Direkt editierbar. Diese IDs werden beim NFO-Sync in die lokale Episoden-NFO geschrieben."
        : "Für diesen Emby-Eintrag gibt es keine Episoden-NFO. TVDB-/IMDB-Sync ist hier nicht anwendbar.";

    /// <summary>
    /// Kleine Zustandsklassen reichen für die Tabelle aus; die fachliche Detailbegründung bleibt im Hinweis.
    /// </summary>
    public string StatusTone => MapStatusTone(StatusText);

    /// <summary>
    /// Zeigt den Statushinweis an und hängt die Emby-ID nur noch als Debug-Kontext an.
    /// </summary>
    public string StatusTooltip => string.IsNullOrWhiteSpace(EmbyItemId)
        ? Note
        : string.IsNullOrWhiteSpace(Note)
            ? $"Emby-ID: {EmbyItemId}"
            : $"{Note}{Environment.NewLine}Emby-ID: {EmbyItemId}";

    public string TvdbLookupTooltip => !SupportsProviderIdSync
        ? "Für diesen Emby-Eintrag gibt es keine Episoden-NFO. Eine TVDB-Korrektur ist hier nicht nötig."
        : _metadataGuess is not null
            ? "Öffnet die TVDB-Suche für genau diese MKV-Zeile."
            : "Die MKV-Benennung kann nicht automatisch in die TVDB-Suche übernommen werden.";

    public string ImdbLookupTooltip => !SupportsProviderIdSync
        ? "Für diesen Emby-Eintrag gibt es keine Episoden-NFO. Eine IMDb-Korrektur ist hier nicht nötig."
        : _metadataGuess is not null
            ? "Öffnet die IMDb-Suchhilfe für genau diese MKV-Zeile."
            : "Öffnet die IMDb-Suchhilfe. Die Suche muss hier ohne Dateiname-Vorbelegung angepasst werden.";

    public EmbyProviderIds ProviderIds => new(
        string.IsNullOrWhiteSpace(TvdbId) ? null : TvdbId,
        string.IsNullOrWhiteSpace(ImdbId) ? null : ImdbId);

    public void ApplyAnalysis(EmbyFileAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        NfoPath = analysis.NfoPath;
        OnPropertyChanged(nameof(NfoPath));
        SupportsProviderIdSync = true;

        var providerIds = ProviderIds
            .MergeFallback(_reportedProviderIds)
            .MergeFallback(analysis.EffectiveProviderIds);
        if (!string.IsNullOrWhiteSpace(providerIds.TvdbId))
        {
            TvdbId = providerIds.TvdbId!;
        }

        if (!string.IsNullOrWhiteSpace(providerIds.ImdbId))
        {
            ImdbId = providerIds.ImdbId!;
        }

        EmbyItemId = analysis.EmbyItem?.Id ?? string.Empty;
        if (!analysis.MediaFileExists)
        {
            SetStatus("Fehlt", "Die MKV-Datei wurde nicht gefunden.");
            IsSelected = false;
            return;
        }

        if (!analysis.NfoExists)
        {
            if (IsNonEpisodeAssetPath(MediaFilePath))
            {
                SupportsProviderIdSync = false;
                SetStatus(
                    "Ohne NFO-Sync",
                    "Emby-Asset ohne Episoden-NFO.");
                return;
            }

            SupportsProviderIdSync = analysis.EmbyItem is null;
            SetStatus(
                analysis.EmbyItem is null ? "NFO fehlt" : "Ohne NFO-Sync",
                analysis.EmbyItem is null
                    ? "NFO fehlt. Erst Emby scannen."
                    : "Emby-Asset ohne Episoden-NFO.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(analysis.WarningMessage))
        {
            SetStatus("NFO prüfen", analysis.WarningMessage!);
            return;
        }

        var providerMismatchNote = BuildProviderMismatchNote(
            analysis.NfoProviderIds.TvdbId,
            analysis.EmbyItem?.GetProviderId("Tvdb") ?? analysis.EmbyItem?.GetProviderId("TvdbSeries"),
            analysis.NfoProviderIds.ImdbId,
            analysis.EmbyItem?.GetProviderId("Imdb"));
        if (analysis.EmbyItem is null)
        {
            SetStatus(
                HasCompleteProviderIds ? "Lokal bereit" : "IDs fehlen",
                BuildProviderStateNote(providerMismatchNote, embyItemKnown: false));
            return;
        }

        SetStatus(
            HasCompleteProviderIds ? "Bereit" : "IDs fehlen",
            BuildProviderStateNote(providerMismatchNote, embyItemKnown: true));
    }

    public void ApplyEmbyItem(EmbyItem? item)
    {
        if (item is null)
        {
            return;
        }

        EmbyItemId = item.Id;
        var embyProviderIds = new EmbyProviderIds(
            item.GetProviderId("Tvdb") ?? item.GetProviderId("TvdbSeries"),
            item.GetProviderId("Imdb"));
        var providerIds = ProviderIds
            .MergeFallback(_reportedProviderIds)
            .MergeFallback(embyProviderIds);
        if (!string.IsNullOrWhiteSpace(providerIds.TvdbId))
        {
            TvdbId = providerIds.TvdbId!;
        }

        if (!string.IsNullOrWhiteSpace(providerIds.ImdbId))
        {
            ImdbId = providerIds.ImdbId!;
        }

        if (!SupportsProviderIdSync)
        {
            SetStatus(
                "Ohne NFO-Sync",
                "Emby-Asset ohne Episoden-NFO.");
            return;
        }

        SetStatus(
            HasCompleteProviderIds ? "Bereit" : "IDs fehlen",
            BuildProviderStateNote(
                BuildProviderMismatchNote(
                    nfoTvdbId: null,
                    embyTvdbId: embyProviderIds.TvdbId,
                    nfoImdbId: null,
                    embyImdbId: embyProviderIds.ImdbId),
                embyItemKnown: true));
    }

    /// <summary>
    /// Baut aus dem standardisierten MKV-Dateinamen den lokalen TVDB-Startvorschlag.
    /// </summary>
    public bool TryBuildMetadataGuess(out EpisodeMetadataGuess? guess)
    {
        guess = _metadataGuess;
        return guess is not null;
    }

    /// <summary>
    /// Übernimmt eine manuell bestätigte TVDB-Zuordnung in die sichtbaren Provider-IDs.
    /// </summary>
    public void ApplyTvdbSelection(TvdbEpisodeSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        TvdbId = selection.TvdbEpisodeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SetStatus(
            string.IsNullOrWhiteSpace(EmbyItemId)
                ? (HasCompleteProviderIds ? "TVDB gewählt" : "IDs fehlen")
                : (HasCompleteProviderIds ? "Bereit" : "IDs fehlen"),
            $"TVDB manuell gewählt: S{selection.SeasonNumber}E{selection.EpisodeNumber} - {selection.EpisodeTitle}");
    }

    /// <summary>
    /// Übernimmt eine manuell bestätigte IMDb-ID in die sichtbaren Provider-IDs.
    /// </summary>
    public void ApplyImdbSelection(string imdbId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imdbId);

        ImdbId = imdbId.Trim();
        SetStatus(
            HasCompleteProviderIds
                ? (string.IsNullOrWhiteSpace(EmbyItemId) ? "Bereit" : "Bereit")
                : "IDs fehlen",
            $"IMDb manuell gesetzt: {ImdbId}");
    }

    public void SetStatus(string statusText, string note)
    {
        StatusText = statusText;
        Note = note;
    }

    public void MarkUpdated(bool metadataRefreshTriggered)
    {
        MarkUpdated(metadataRefreshTriggered, noRefreshReason: null);
    }

    /// <summary>
    /// Kennzeichnet eine erfolgreich geschriebene NFO und beschreibt optional, warum kein Emby-Refresh lief.
    /// </summary>
    public void MarkUpdated(bool metadataRefreshTriggered, string? noRefreshReason)
    {
        if (HasCompleteProviderIds)
        {
            SetStatus(
                "Aktualisiert",
                metadataRefreshTriggered
                    ? "NFO aktualisiert, Emby-Refresh angestoßen."
                    : string.IsNullOrWhiteSpace(noRefreshReason)
                        ? "NFO aktualisiert, Emby-Item noch nicht gefunden."
                        : $"NFO aktualisiert, {noRefreshReason}");
            return;
        }

        SetStatus(
            "IDs fehlen",
            metadataRefreshTriggered
                ? $"NFO aktualisiert. {BuildProviderCoverageNote()} Emby-Refresh angestoßen."
                : $"NFO aktualisiert. {noRefreshReason ?? BuildProviderCoverageNote()}");
    }

    /// <summary>
    /// Kennzeichnet, dass die NFO bereits geschrieben wurde, Emby den anschließenden Refresh aber nicht ausführen konnte.
    /// </summary>
    public void MarkRefreshFailed(string failureMessage)
    {
        var normalizedFailureMessage = string.IsNullOrWhiteSpace(failureMessage)
            ? "NFO aktualisiert, Emby-Refresh fehlgeschlagen."
            : $"NFO aktualisiert, Emby-Refresh fehlgeschlagen: {failureMessage.Trim()}";
        SetStatus(
            "Refresh prüfen",
            HasCompleteProviderIds
                ? normalizedFailureMessage
                : $"{normalizedFailureMessage} {BuildProviderCoverageNote()}");
    }

    /// <summary>
    /// Kennzeichnet, dass die NFO bereits den aktuell verfügbaren Stand widerspiegelt, fachlich
    /// aber noch Provider-IDs fehlen.
    /// </summary>
    public void MarkCurrentWithMissingIds()
    {
        SetStatus("IDs fehlen", $"NFO aktuell. {BuildProviderCoverageNote()}");
    }

    /// <summary>
    /// Kennzeichnet formale Fehler in den manuell bearbeiteten Provider-ID-Feldern.
    /// </summary>
    public void MarkInvalidProviderIds()
    {
        SetStatus("IDs prüfen", GetProviderIdValidationMessage() ?? "Provider-IDs enthalten ein ungültiges Format.");
    }

    /// <inheritdoc/>
    public string Error => string.Empty;

    /// <inheritdoc/>
    public string this[string columnName] => columnName switch
    {
        nameof(TvdbId) => GetTvdbValidationMessage(TvdbId) ?? string.Empty,
        nameof(ImdbId) => GetImdbValidationMessage(ImdbId) ?? string.Empty,
        _ => string.Empty
    };

    private string? BuildProviderMismatchNote(
        string? nfoTvdbId,
        string? embyTvdbId,
        string? nfoImdbId,
        string? embyImdbId)
    {
        var preferredTvdbId = string.IsNullOrWhiteSpace(TvdbId)
            ? _reportedProviderIds.TvdbId
            : TvdbId;
        var preferredImdbId = string.IsNullOrWhiteSpace(ImdbId)
            ? _reportedProviderIds.ImdbId
            : ImdbId;

        var parts = new List<string>();
        var tvdbMismatchNote = BuildProviderSourceMismatchNote("TVDB", preferredTvdbId, nfoTvdbId, embyTvdbId);
        if (!string.IsNullOrWhiteSpace(tvdbMismatchNote))
        {
            parts.Add(tvdbMismatchNote);
        }

        var imdbMismatchNote = BuildProviderSourceMismatchNote("IMDb", preferredImdbId, nfoImdbId, embyImdbId);
        if (!string.IsNullOrWhiteSpace(imdbMismatchNote))
        {
            parts.Add(imdbMismatchNote);
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static string? BuildProviderSourceMismatchNote(
        string providerLabel,
        string? preferredValue,
        string? nfoValue,
        string? embyValue)
    {
        if (string.IsNullOrWhiteSpace(preferredValue))
        {
            return null;
        }

        var mismatches = new List<string>();
        if (!string.IsNullOrWhiteSpace(nfoValue)
            && !string.Equals(nfoValue, preferredValue, StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add($"NFO: {nfoValue}");
        }

        if (!string.IsNullOrWhiteSpace(embyValue)
            && !string.Equals(embyValue, preferredValue, StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add($"Emby: {embyValue}");
        }

        return mismatches.Count == 0
            ? null
            : $"{providerLabel} {preferredValue} vorgesehen ({string.Join(", ", mismatches)}).";
    }

    private string BuildProviderStateNote(string? providerMismatchNote, bool embyItemKnown)
    {
        var providerCoverage = embyItemKnown
            ? BuildProviderCoverageNote()
            : BuildProviderCoverageNote(localOnly: true);
        if (string.IsNullOrWhiteSpace(providerMismatchNote))
        {
            return providerCoverage;
        }

        return HasCompleteProviderIds
            ? providerMismatchNote
            : $"{providerMismatchNote} {providerCoverage}";
    }

    private string BuildProviderCoverageNote(bool localOnly = false)
    {
        var validationMessage = GetProviderIdValidationMessage();
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            return validationMessage;
        }

        if (HasCompleteProviderIds)
        {
            return localOnly ? "IDs lokal vorhanden." : "IDs vorhanden.";
        }

        if (HasTvdbId && !HasImdbId)
        {
            return "IMDB-ID fehlt.";
        }

        if (!HasTvdbId && HasImdbId)
        {
            return "TVDB-ID fehlt.";
        }

        return "Keine TVDB-/IMDB-ID.";
    }

    /// <summary>
    /// Baut einen kompakten Sammelhinweis über alle aktuell formal ungültigen Provider-ID-Felder.
    /// </summary>
    private string? GetProviderIdValidationMessage()
    {
        var messages = new List<string>();
        var tvdbValidationMessage = GetTvdbValidationMessage(TvdbId);
        if (!string.IsNullOrWhiteSpace(tvdbValidationMessage))
        {
            messages.Add(tvdbValidationMessage);
        }

        var imdbValidationMessage = GetImdbValidationMessage(ImdbId);
        if (!string.IsNullOrWhiteSpace(imdbValidationMessage))
        {
            messages.Add(imdbValidationMessage);
        }

        return messages.Count == 0 ? null : string.Join(" ", messages);
    }

    private static string? GetTvdbValidationMessage(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && !TvdbIdPattern.IsMatch(value)
            ? "TVDB-ID muss eine Ganzzahl sein."
            : null;
    }

    private static string? GetImdbValidationMessage(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && !ImdbIdPattern.IsMatch(value)
            ? "IMDB-ID muss im Format tt1234567 bis tt1234567890 angegeben werden."
            : null;
    }

    private static EpisodeMetadataGuess? TryParseMetadataGuess(string mediaFilePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(mediaFilePath);
        var normalizedFileName = EpisodeFileNameHelper.NormalizeTypography(fileName);
        var match = EpisodeFileNamePattern.Match(normalizedFileName);
        if (!match.Success)
        {
            return null;
        }

        return new EpisodeMetadataGuess(
            match.Groups["series"].Value.Trim(),
            match.Groups["title"].Value.Trim(),
            match.Groups["season"].Value.Trim(),
            match.Groups["episode"].Value.Trim());
    }

    private static bool IsNonEpisodeAssetPath(string mediaFilePath)
    {
        var directory = Path.GetDirectoryName(mediaFilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        return directory
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => EmbyAssetFoldersWithoutEpisodeNfo.Contains(segment));
    }

    private static string MapStatusTone(string statusText)
    {
        return statusText switch
        {
            "Bereit" or "TVDB gewählt" or "Lokal bereit" => "Ready",
            "NFO aktuell" or "Aktualisiert" => "Done",
            "IDs fehlen" or "IDs prüfen" or "NFO fehlt" or "NFO prüfen" or "Refresh prüfen" or "Übersprungen" => "Warning",
            "Fehlt" => "Error",
            _ => "Neutral"
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(StatusText))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusTone)));
        }
    }
}
