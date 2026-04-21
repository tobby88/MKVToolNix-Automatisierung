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
            OnPropertyChanged(nameof(CanEditProviderIds));
            OnPropertyChanged(nameof(ProviderIdEditTooltip));
            OnPropertyChanged(nameof(TvdbLookupTooltip));
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
            OnPropertyChanged(nameof(HasProviderIds));
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
            OnPropertyChanged(nameof(HasProviderIds));
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
        }
    }

    public bool HasProviderIds => !string.IsNullOrWhiteSpace(TvdbId) || !string.IsNullOrWhiteSpace(ImdbId);

    /// <summary>
    /// Sichtbare TVDB-Suche ist nur dann sinnvoll, wenn der Dateiname vorbefüllt werden kann und
    /// die Zeile später tatsächlich in eine Episoden-NFO zurückgeschrieben wird.
    /// </summary>
    public bool CanReviewTvdb => SupportsProviderIdSync && _metadataGuess is not null;

    /// <summary>
    /// Kennzeichnet die gelb hinterlegten TVDB-/IMDB-Felder als aktiv editierbar.
    /// </summary>
    public bool CanEditProviderIds => SupportsProviderIdSync;

    public string ProviderIdEditTooltip => SupportsProviderIdSync
        ? "Direkt editierbar. Diese IDs werden beim NFO-Sync in die lokale Episoden-NFO geschrieben."
        : "Für diesen Emby-Eintrag gibt es keine Episoden-NFO. TVDB-/IMDB-Sync ist hier nicht anwendbar.";

    public string TvdbLookupTooltip => !SupportsProviderIdSync
        ? "Für diesen Emby-Eintrag gibt es keine Episoden-NFO. Eine TVDB-Korrektur ist hier nicht nötig."
        : _metadataGuess is not null
            ? "Öffnet die TVDB-Suche für genau diese MKV-Zeile."
            : "Die MKV-Benennung kann nicht automatisch in die TVDB-Suche übernommen werden.";

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
            SupportsProviderIdSync = analysis.EmbyItem is null;
            SetStatus(
                analysis.EmbyItem is null ? "NFO fehlt" : "Ohne NFO-Sync",
                analysis.EmbyItem is null
                    ? "Bitte Emby zuerst scannen lassen, damit die Episoden-NFO angelegt wird."
                    : $"Emby-Item gefunden: {analysis.EmbyItem.Name}. Für diesen Pfad legt Emby keine Episoden-NFO an (z. B. trailers oder backdrops); TVDB-/IMDB-Sync ist hier nicht anwendbar.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(analysis.WarningMessage))
        {
            SetStatus("NFO prüfen", analysis.WarningMessage!);
            return;
        }

        var providerMismatchNote = BuildProviderMismatchNote(
            analysis.NfoProviderIds.TvdbId,
            analysis.EmbyItem?.GetProviderId("Tvdb") ?? analysis.EmbyItem?.GetProviderId("TvdbSeries"));
        if (analysis.EmbyItem is null)
        {
            SetStatus(
                HasProviderIds ? "Lokal bereit" : "IDs fehlen",
                providerMismatchNote
                ?? (HasProviderIds
                    ? "Provider-IDs liegen lokal vor; Emby-Item wurde noch nicht gefunden."
                    : "Weder NFO noch Emby liefern TVDB-/IMDB-IDs. IDs bitte manuell ergänzen oder Emby-Metadaten prüfen."));
            return;
        }

        SetStatus(
            HasProviderIds ? "Bereit" : "IDs fehlen",
            providerMismatchNote
            ?? (HasProviderIds
                ? $"Emby-Item gefunden: {analysis.EmbyItem.Name}"
                : "Emby-Item gefunden, aber ohne TVDB-/IMDB-ID."));
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
                $"Emby-Item gefunden: {item.Name}. Für diesen Pfad legt Emby keine Episoden-NFO an (z. B. trailers oder backdrops); TVDB-/IMDB-Sync ist hier nicht anwendbar.");
            return;
        }

        SetStatus(
            HasProviderIds ? "Bereit" : "IDs fehlen",
            BuildProviderMismatchNote(
                nfoTvdbId: null,
                embyTvdbId: embyProviderIds.TvdbId)
            ?? $"Emby-Item gefunden: {item.Name}");
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
            string.IsNullOrWhiteSpace(EmbyItemId) ? "TVDB gewählt" : "Bereit",
            $"TVDB manuell gewählt: S{selection.SeasonNumber}E{selection.EpisodeNumber} - {selection.EpisodeTitle}");
    }

    public void SetStatus(string statusText, string note)
    {
        StatusText = statusText;
        Note = note;
    }

    public void MarkUpdated(bool metadataRefreshTriggered)
    {
        SetStatus(
            "Aktualisiert",
            metadataRefreshTriggered
                ? "NFO aktualisiert und Emby-Metadatenrefresh angestoßen."
                : "NFO aktualisiert. Emby-Item wurde noch nicht gefunden, daher kein gezielter Refresh.");
    }

    /// <inheritdoc/>
    public string Error => string.Empty;

    /// <inheritdoc/>
    public string this[string columnName] => columnName switch
    {
        nameof(TvdbId) when !string.IsNullOrWhiteSpace(TvdbId) && !Regex.IsMatch(TvdbId, @"^\d+$")
            => "TVDB-ID muss eine Ganzzahl sein.",
        nameof(ImdbId) when !string.IsNullOrWhiteSpace(ImdbId) && !Regex.IsMatch(ImdbId, @"^tt\d+$")
            => "IMDB-ID muss im Format tt1234567 angegeben werden.",
        _ => string.Empty
    };

    private string? BuildProviderMismatchNote(string? nfoTvdbId, string? embyTvdbId)
    {
        var preferredTvdbId = string.IsNullOrWhiteSpace(TvdbId)
            ? _reportedProviderIds.TvdbId
            : TvdbId;
        if (string.IsNullOrWhiteSpace(preferredTvdbId))
        {
            return null;
        }

        var mismatches = new List<string>();
        if (!string.IsNullOrWhiteSpace(nfoTvdbId)
            && !string.Equals(nfoTvdbId, preferredTvdbId, StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add($"NFO: {nfoTvdbId}");
        }

        if (!string.IsNullOrWhiteSpace(embyTvdbId)
            && !string.Equals(embyTvdbId, preferredTvdbId, StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add($"Emby: {embyTvdbId}");
        }

        return mismatches.Count == 0
            ? null
            : $"Für den Sync ist TVDB-ID {preferredTvdbId} vorgemerkt; {string.Join(", ", mismatches)}. Beim Sync wird diese ID in die NFO geschrieben.";
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
