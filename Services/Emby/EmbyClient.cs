using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MkvToolnixAutomatisierung.Services.Emby;

/// <summary>
/// Minimale Emby-API-Oberfläche, die das Modul für Serverprüfung, Library-Scan und Item-Refresh benötigt.
/// </summary>
internal interface IEmbyClient : IDisposable
{
    /// <summary>
    /// Liest die in Emby konfigurierten Bibliotheken inklusive Pfaden und aktuellem Refreshstatus.
    /// </summary>
    Task<IReadOnlyList<EmbyLibraryFolder>> GetLibrariesAsync(AppEmbySettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Liest grundlegende Serverinformationen, um Adresse und API-Key zu prüfen.
    /// </summary>
    Task<EmbyServerInfo> GetSystemInfoAsync(AppEmbySettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Startet einen Emby-Library-Scan.
    /// </summary>
    Task TriggerLibraryScanAsync(AppEmbySettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Startet für ein konkretes Emby-Item einen "Scan library files"-ähnlichen Refresh.
    /// Bei Ordner-Items entspricht das fachlich dem bevorzugten, gezielten Bibliotheksscan.
    /// </summary>
    Task TriggerItemFileScanAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sucht ein Emby-Item über den lokalen Dateipfad der MKV.
    /// </summary>
    Task<EmbyItem?> FindItemByPathAsync(AppEmbySettings settings, string mediaFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fordert für ein konkretes Item einen Metadaten-Refresh an.
    /// </summary>
    Task RefreshItemMetadataAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Schlanker HTTP-Client für die wenigen Emby-Endpunkte, die der lokale NFO-/Provider-ID-Abgleich braucht.
/// </summary>
internal sealed class EmbyClient : IEmbyClient
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private static readonly IReadOnlyDictionary<string, string?> EmptyQueryParameters = new Dictionary<string, string?>();

    /// <summary>
    /// Erstellt den Client optional auf Basis eines bereits vorhandenen <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Optional injizierter Client für Tests; ohne Wert wird ein eigener Client erzeugt.</param>
    public EmbyClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        if (_ownsHttpClient)
        {
            // Die Emby-Aktionen sind kurze Steuerbefehle; ein harter Timeout verhindert
            // UI-seitig schwer einzuordnende Hänger bei Netzwerk- oder Serverproblemen.
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }
    }

    /// <inheritdoc />
    public async Task<EmbyServerInfo> GetSystemInfoAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(settings, HttpMethod.Get, "System/Info", EmptyQueryParameters, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        var root = document.RootElement;
        return new EmbyServerInfo(
            ReadString(root, "ServerName") ?? "(unbekannter Emby-Server)",
            ReadString(root, "Version") ?? "(unbekannte Version)",
            ReadString(root, "Id") ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmbyLibraryFolder>> GetLibrariesAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(settings, HttpMethod.Get, "Library/VirtualFolders/Query", EmptyQueryParameters, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        if (!document.RootElement.TryGetProperty("Items", out var itemsElement)
            || itemsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return itemsElement
            .EnumerateArray()
            .Select(ParseLibraryFolder)
            .Where(folder => folder is not null)
            .Cast<EmbyLibraryFolder>()
            .ToList();
    }

    /// <inheritdoc />
    public async Task TriggerLibraryScanAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(settings, HttpMethod.Post, "Library/Refresh", EmptyQueryParameters, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    public Task TriggerItemFileScanAsync(
        AppEmbySettings settings,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        return PostItemRefreshAsync(
            settings,
            itemId,
            recursive: true,
            metadataRefreshMode: "Default",
            imageRefreshMode: "Default",
            replaceAllMetadata: false,
            replaceAllImages: false,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmbyItem?> FindItemByPathAsync(
        AppEmbySettings settings,
        string mediaFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaFilePath);

        var queryParameters = new Dictionary<string, string?>
        {
            ["Recursive"] = "true",
            ["Path"] = mediaFilePath,
            ["Fields"] = "Path,ProviderIds"
        };

        using var response = await SendAsync(settings, HttpMethod.Get, "Items", queryParameters, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        if (!document.RootElement.TryGetProperty("Items", out var itemsElement)
            || itemsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var itemElement in itemsElement.EnumerateArray())
        {
            var item = ParseItem(itemElement);
            if (item is null)
            {
                continue;
            }

            if (AreSameEmbyPath(item.Path, mediaFilePath))
            {
                return item;
            }
        }

        // Emby behandelt den Path-Filter nicht in allen Versionen als harte Gleichheitsbedingung.
        // Ein nicht exakt passender Einzeltreffer ist deshalb genauso unsicher wie mehrere Treffer.
        return null;
    }

    /// <inheritdoc />
    public async Task RefreshItemMetadataAsync(
        AppEmbySettings settings,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        await PostItemRefreshAsync(
            settings,
            itemId,
            recursive: false,
            metadataRefreshMode: "FullRefresh",
            imageRefreshMode: "Default",
            replaceAllMetadata: false,
            replaceAllImages: false,
            cancellationToken);
    }

    private async Task PostItemRefreshAsync(
        AppEmbySettings settings,
        string itemId,
        bool recursive,
        string metadataRefreshMode,
        string imageRefreshMode,
        bool replaceAllMetadata,
        bool replaceAllImages,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var queryParameters = new Dictionary<string, string?>
        {
            ["Recursive"] = recursive ? "true" : "false",
            ["MetadataRefreshMode"] = metadataRefreshMode,
            ["ImageRefreshMode"] = imageRefreshMode,
            ["ReplaceAllMetadata"] = replaceAllMetadata ? "true" : "false",
            ["ReplaceAllImages"] = replaceAllImages ? "true" : "false"
        };

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await SendAsync(
            settings,
            HttpMethod.Post,
            $"Items/{Uri.EscapeDataString(itemId)}/Refresh",
            queryParameters,
            cancellationToken,
            content);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        AppEmbySettings settings,
        HttpMethod method,
        string relativePath,
        IReadOnlyDictionary<string, string?> queryParameters,
        CancellationToken cancellationToken,
        HttpContent? content = null)
    {
        ValidateSettings(settings);
        var requestUri = BuildRequestUri(settings.ServerUrl, relativePath, queryParameters);
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("X-Emby-Token", settings.ApiKey.Trim());
        request.Content = content;

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Emby-Anfrage hat den Timeout überschritten.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Emby-Server nicht erreichbar. Bitte Adresse und Netzwerkverbindung prüfen.", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        try
        {
            await ThrowForFailureAsync(response, cancellationToken);
        }
        finally
        {
            response.Dispose();
        }

        throw new InvalidOperationException("Emby-Anfrage ist unerwartet fehlgeschlagen.");
    }

    private static void ValidateSettings(AppEmbySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(settings.ServerUrl))
        {
            throw new InvalidOperationException("Bitte zuerst die Emby-Serveradresse eintragen.");
        }

        if (!Uri.TryCreate(settings.ServerUrl.Trim(), UriKind.Absolute, out var serverUri)
            || (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Die Emby-Serveradresse muss eine gültige HTTP- oder HTTPS-Adresse sein.");
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Bitte zuerst einen Emby-API-Key eintragen.");
        }
    }

    private static Uri BuildRequestUri(string serverUrl, string relativePath, IReadOnlyDictionary<string, string?> queryParameters)
    {
        var baseUri = new Uri(serverUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute);
        var uriBuilder = new UriBuilder(new Uri(baseUri, relativePath.TrimStart('/')));
        if (queryParameters.Count > 0)
        {
            uriBuilder.Query = string.Join(
                "&",
                queryParameters
                    .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
                    .Select(parameter =>
                        Uri.EscapeDataString(parameter.Key)
                        + "="
                        + Uri.EscapeDataString(parameter.Value!)));
        }

        return uriBuilder.Uri;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            return await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException("Emby hat keine gültige JSON-Antwort geliefert.", ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        await ThrowForFailureAsync(response, cancellationToken);
    }

    private static async Task ThrowForFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Emby lehnt den API-Key ab.",
            HttpStatusCode.NotFound => "Emby hat den angefragten Endpunkt oder das Item nicht gefunden.",
            _ => $"Emby-Anfrage fehlgeschlagen: {(int)response.StatusCode} {response.ReasonPhrase}"
        };

        if (!string.IsNullOrWhiteSpace(detail))
        {
            message += Environment.NewLine + detail.Trim();
        }

        throw new InvalidOperationException(message);
    }

    private static EmbyItem? ParseItem(JsonElement itemElement)
    {
        var id = ReadString(itemElement, "Id");
        var name = ReadString(itemElement, "Name") ?? string.Empty;
        var path = ReadString(itemElement, "Path") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (itemElement.TryGetProperty("ProviderIds", out var providerIdsElement)
            && providerIdsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var provider in providerIdsElement.EnumerateObject())
            {
                if (provider.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(provider.Value.GetString()))
                {
                    providerIds[provider.Name] = provider.Value.GetString()!;
                }
            }
        }

        return new EmbyItem(id, name, path, providerIds);
    }

    private static bool AreSameEmbyPath(string? left, string? right)
    {
        var normalizedLeft = NormalizeEmbyPath(left);
        var normalizedRight = NormalizeEmbyPath(right);
        return normalizedLeft is not null
            && normalizedRight is not null
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeEmbyPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : path.Replace('\\', '/').TrimEnd('/');
    }

    private static EmbyLibraryFolder? ParseLibraryFolder(JsonElement itemElement)
    {
        var id = ReadString(itemElement, "ItemId") ?? ReadString(itemElement, "Id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var locations = itemElement.TryGetProperty("Locations", out var locationsElement)
            && locationsElement.ValueKind == JsonValueKind.Array
                ? locationsElement
                    .EnumerateArray()
                    .Where(location => location.ValueKind == JsonValueKind.String)
                    .Select(location => location.GetString())
                    .Where(location => !string.IsNullOrWhiteSpace(location))
                    .Select(location => location!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];

        return new EmbyLibraryFolder(
            id,
            ReadString(itemElement, "Name") ?? string.Empty,
            locations,
            ReadDouble(itemElement, "RefreshProgress"),
            ReadString(itemElement, "RefreshStatus"));
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out var number))
        {
            return number;
        }

        return null;
    }
}

/// <summary>
/// Grunddaten eines Emby-Servers für Verbindungstest und Statusanzeige.
/// </summary>
internal sealed record EmbyServerInfo(string ServerName, string Version, string Id);

/// <summary>
/// Repräsentiert eine in Emby konfigurierte Bibliothek samt aktuellem Refreshstatus.
/// </summary>
internal sealed record EmbyLibraryFolder(
    string Id,
    string Name,
    IReadOnlyList<string> Locations,
    double? RefreshProgress,
    string? RefreshStatus);

/// <summary>
/// Minimale Emby-Item-Repräsentation inklusive Provider-IDs aus der Serverdatenbank.
/// </summary>
internal sealed record EmbyItem(
    string Id,
    string Name,
    string Path,
    IReadOnlyDictionary<string, string> ProviderIds)
{
    /// <summary>
    /// Liefert eine Provider-ID case-insensitive aus Emby zurück.
    /// </summary>
    public string? GetProviderId(string providerName)
    {
        return ProviderIds.TryGetValue(providerName, out var providerId)
            ? providerId
            : null;
    }
}
