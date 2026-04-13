namespace MkvToolnixAutomatisierung.Services.Emby;

/// <summary>
/// Orchestriert den lokalen Emby-Abgleich aus Dateiliste, NFO-Provider-IDs und Emby-API-Itemdaten.
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
    /// Liest die einfache Liste neu erzeugter Batch-Ausgabedateien.
    /// </summary>
    /// <remarks>
    /// Das bestehende Reportformat ist absichtlich menschenlesbar. Deshalb werden nur Zeilen
    /// übernommen, die wie MKV-Pfade aussehen; Kopfzeilen bleiben ignoriert.
    /// </remarks>
    public IReadOnlyList<string> LoadNewOutputReport(string reportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException("Die ausgewählte Dateiliste wurde nicht gefunden.", reportPath);
        }

        return File.ReadLines(reportPath)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => string.Equals(Path.GetExtension(line), ".mkv", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Prüft die Emby-Verbindung anhand der gespeicherten Serveradresse und des API-Keys.
    /// </summary>
    public Task<EmbyServerInfo> TestConnectionAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
    {
        return _embyClient.GetSystemInfoAsync(settings, cancellationToken);
    }

    /// <summary>
    /// Startet einen Emby-Library-Scan.
    /// </summary>
    public Task TriggerLibraryScanAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
    {
        return _embyClient.TriggerLibraryScanAsync(settings, cancellationToken);
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
}

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
