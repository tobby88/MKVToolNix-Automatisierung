using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Services.Emby;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Bindbare Zeile des Emby-Abgleichs für eine neu erzeugte MKV.
/// </summary>
internal sealed class EmbySyncItemViewModel : INotifyPropertyChanged, IDataErrorInfo
{
    private readonly EmbyProviderIds _reportedProviderIds;
    private bool _isSelected = true;
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

    public EmbyProviderIds ProviderIds => new(
        string.IsNullOrWhiteSpace(TvdbId) ? null : TvdbId,
        string.IsNullOrWhiteSpace(ImdbId) ? null : ImdbId);

    public void ApplyAnalysis(EmbyFileAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        NfoPath = analysis.NfoPath;
        OnPropertyChanged(nameof(NfoPath));

        var providerIds = _reportedProviderIds.MergeFallback(analysis.EffectiveProviderIds);
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
            SetStatus("NFO fehlt", "Bitte Emby zuerst scannen lassen, damit die Episoden-NFO angelegt wird.");
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
        var providerIds = _reportedProviderIds.MergeFallback(embyProviderIds);
        if (!string.IsNullOrWhiteSpace(providerIds.TvdbId))
        {
            TvdbId = providerIds.TvdbId!;
        }

        if (!string.IsNullOrWhiteSpace(providerIds.ImdbId))
        {
            ImdbId = providerIds.ImdbId!;
        }

        SetStatus(
            HasProviderIds ? "Bereit" : "IDs fehlen",
            BuildProviderMismatchNote(
                nfoTvdbId: null,
                embyTvdbId: embyProviderIds.TvdbId)
            ?? $"Emby-Item gefunden: {item.Name}");
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
        if (string.IsNullOrWhiteSpace(_reportedProviderIds.TvdbId))
        {
            return null;
        }

        var mismatches = new List<string>();
        if (!string.IsNullOrWhiteSpace(nfoTvdbId)
            && !string.Equals(nfoTvdbId, _reportedProviderIds.TvdbId, StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add($"NFO: {nfoTvdbId}");
        }

        if (!string.IsNullOrWhiteSpace(embyTvdbId)
            && !string.Equals(embyTvdbId, _reportedProviderIds.TvdbId, StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add($"Emby: {embyTvdbId}");
        }

        return mismatches.Count == 0
            ? null
            : $"JSON-Report liefert TVDB-ID {_reportedProviderIds.TvdbId}; {string.Join(", ", mismatches)}. Beim Sync wird die Report-ID in die NFO geschrieben.";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
