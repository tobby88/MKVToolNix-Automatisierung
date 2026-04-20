using System.Globalization;

namespace MkvToolnixAutomatisierung.Services.Emby;

/// <summary>
/// Orchestriert den lokalen Emby-Abgleich aus JSON-Metadatenreport, NFO-Provider-IDs und Emby-API-Itemdaten.
/// </summary>
internal sealed class EmbyMetadataSyncService
{
    private readonly IEmbyClient _embyClient;
    private readonly EmbyNfoProviderIdService _nfoProviderIds;

    /// <summary>
    /// Initialisiert den Service mit API-Client und NFO-Helfer.
    /// </summary>
    public EmbyMetadataSyncService(IEmbyClient embyClient, EmbyNfoProviderIdService nfoProviderIds)
    {
        _embyClient = embyClient;
        _nfoProviderIds = nfoProviderIds;
    }

    /// <summary>
    /// Liest einen Batch-Report mit neu erzeugten Ausgabedateien.
    /// </summary>
    public IReadOnlyList<EmbyImportEntry> LoadNewOutputReport(string reportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException("Der ausgewählte Metadatenreport wurde nicht gefunden.", reportPath);
        }

        if (!string.Equals(Path.GetExtension(reportPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Der Emby-Abgleich erwartet den strukturierten JSON-Metadatenreport (*.metadata.json). Die ältere TXT-Dateiliste wird nicht mehr importiert.");
        }

        return LoadStructuredOutputReport(reportPath);
    }

    /// <summary>
    /// Prüft die Emby-Verbindung anhand der gespeicherten Serveradresse und des API-Keys.
    /// </summary>
    public Task<EmbyServerInfo> TestConnectionAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
    {
        return _embyClient.GetSystemInfoAsync(settings, cancellationToken);
    }

    /// <summary>
    /// Startet bevorzugt einen gezielten Scan der konfigurierten Serienbibliothek.
    /// Falls Emby den Bibliothekswurzelpfad nicht direkt als Item auflösen kann,
    /// wird konservativ auf den globalen Library-Scan zurückgefallen.
    /// </summary>
    public async Task<EmbyLibraryScanTriggerResult> TriggerSeriesLibraryScanAsync(
        AppEmbySettings settings,
        string? archiveRootPath,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(archiveRootPath))
        {
            var libraryRootItem = await _embyClient.FindItemByPathAsync(settings, archiveRootPath, cancellationToken);
            if (libraryRootItem is not null)
            {
                await _embyClient.TriggerItemFileScanAsync(settings, libraryRootItem.Id, cancellationToken);
                return new EmbyLibraryScanTriggerResult(
                    UsedGlobalLibraryScan: false,
                    $"Serienbibliotheksscan angestoßen: {archiveRootPath}");
            }
        }

        await _embyClient.TriggerLibraryScanAsync(settings, cancellationToken);
        return string.IsNullOrWhiteSpace(archiveRootPath)
            ? new EmbyLibraryScanTriggerResult(
                UsedGlobalLibraryScan: true,
                "Globaler Emby-Library-Scan angestoßen, weil kein Serienbibliothekspfad konfiguriert ist.")
            : new EmbyLibraryScanTriggerResult(
                UsedGlobalLibraryScan: true,
                $"Globaler Emby-Library-Scan angestoßen, weil die Serienbibliothek in Emby nicht direkt gefunden wurde: {archiveRootPath}");
    }

    /// <summary>
    /// Ermittelt NFO- und, falls konfiguriert, Emby-Provider-IDs für eine MKV.
    /// </summary>
    public async Task<EmbyFileAnalysis> AnalyzeFileAsync(
        AppEmbySettings settings,
        string mediaFilePath,
        bool queryEmby,
        CancellationToken cancellationToken = default)
    {
        var localAnalysis = AnalyzeLocalFile(mediaFilePath);
        if (!queryEmby || !localAnalysis.MediaFileExists)
        {
            return localAnalysis;
        }

        var embyItem = await _embyClient.FindItemByPathAsync(settings, mediaFilePath, cancellationToken);
        return localAnalysis.WithEmbyItem(embyItem);
    }

    /// <summary>
    /// Ermittelt nur lokale Datei- und NFO-Daten ohne Emby-Netzwerkzugriff.
    /// </summary>
    public EmbyFileAnalysis AnalyzeLocalFile(string mediaFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaFilePath);

        var mediaFileExists = File.Exists(mediaFilePath);
        var nfoResult = _nfoProviderIds.ReadProviderIds(mediaFilePath);
        return new EmbyFileAnalysis(
            mediaFilePath,
            nfoResult.NfoPath,
            mediaFileExists,
            nfoResult.NfoExists,
            nfoResult.ProviderIds,
            EmbyItem: null,
            nfoResult.WarningMessage);
    }

    /// <summary>
    /// Sucht das Emby-Item zu einer MKV.
    /// </summary>
    public Task<EmbyItem?> FindItemByPathAsync(
        AppEmbySettings settings,
        string mediaFilePath,
        CancellationToken cancellationToken = default)
    {
        return _embyClient.FindItemByPathAsync(settings, mediaFilePath, cancellationToken);
    }

    /// <summary>
    /// Aktualisiert die Provider-IDs in der lokalen NFO.
    /// </summary>
    public EmbyNfoUpdateResult UpdateNfoProviderIds(string mediaFilePath, EmbyProviderIds providerIds)
    {
        return _nfoProviderIds.UpdateProviderIds(mediaFilePath, providerIds);
    }

    /// <summary>
    /// Fordert nach NFO-Änderungen einen gezielten Emby-Metadaten-Refresh an.
    /// </summary>
    public Task RefreshItemMetadataAsync(
        AppEmbySettings settings,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        return _embyClient.RefreshItemMetadataAsync(settings, itemId, cancellationToken);
    }

    private static IReadOnlyList<EmbyImportEntry> LoadStructuredOutputReport(string reportPath)
    {
        var report = BatchOutputMetadataReportJson.Deserialize(File.ReadAllText(reportPath));
        if (report is null)
        {
            throw new InvalidDataException("Der ausgewählte Metadaten-Report konnte nicht gelesen werden.");
        }

        return report.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.OutputPath))
            .Where(item => string.Equals(Path.GetExtension(item.OutputPath), ".mkv", StringComparison.OrdinalIgnoreCase))
            .Select(item => new EmbyImportEntry(item.OutputPath, BuildProviderIds(item)))
            .GroupBy(item => item.MediaFilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.MediaFilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static EmbyProviderIds BuildProviderIds(BatchOutputMetadataEntry item)
    {
        var tvdbId = string.IsNullOrWhiteSpace(item.ProviderIds?.Tvdb)
            ? item.TvdbEpisodeId ?? item.Tvdb?.EpisodeId?.ToString(CultureInfo.InvariantCulture)
            : item.ProviderIds!.Tvdb;
        var imdbId = string.IsNullOrWhiteSpace(item.ProviderIds?.Imdb)
            ? null
            : item.ProviderIds!.Imdb;

        return new EmbyProviderIds(tvdbId, imdbId);
    }
}

internal sealed record EmbyImportEntry(string MediaFilePath, EmbyProviderIds ProviderIds);

/// <summary>
/// Beschreibt, ob der Emby-Abgleich den bevorzugten Serienbibliotheksscan oder den globalen Fallback nutzen musste.
/// </summary>
internal sealed record EmbyLibraryScanTriggerResult(
    bool UsedGlobalLibraryScan,
    string Message);

/// <summary>
/// Kombiniert lokale NFO-Daten und optionale Emby-Item-Daten für eine einzelne MKV.
/// </summary>
internal sealed record EmbyFileAnalysis(
    string MediaFilePath,
    string NfoPath,
    bool MediaFileExists,
    bool NfoExists,
    EmbyProviderIds NfoProviderIds,
    EmbyItem? EmbyItem,
    string? WarningMessage)
{
    /// <summary>
    /// Effektive Provider-IDs, bei denen vorhandene NFO-Werte Vorrang vor Emby-Daten haben.
    /// </summary>
    public EmbyProviderIds EffectiveProviderIds => NfoProviderIds.MergeFallback(EmbyProviderIdsFromItem(EmbyItem));

    /// <summary>
    /// Erstellt eine Kopie mit aktualisiertem Emby-Item.
    /// </summary>
    public EmbyFileAnalysis WithEmbyItem(EmbyItem? embyItem)
    {
        return this with { EmbyItem = embyItem };
    }

    private static EmbyProviderIds EmbyProviderIdsFromItem(EmbyItem? item)
    {
        return item is null
            ? EmbyProviderIds.Empty
            : new EmbyProviderIds(
                item.GetProviderId("Tvdb") ?? item.GetProviderId("TvdbSeries"),
                item.GetProviderId("Imdb"));
    }
}
