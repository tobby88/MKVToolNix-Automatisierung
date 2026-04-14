using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Schlanker TVDB-v4-Client mit gemeinsamem Auth-Token und Seiteniteration für Serien-/Episodenabfragen.
/// </summary>
internal interface ITvdbClient : IDisposable
{
    /// <summary>
    /// Sucht TVDB-Serien über die v4-API.
    /// </summary>
    Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(
        string apiKey,
        string? pin,
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt alle Episoden einer TVDB-Serie über die v4-API.
    /// </summary>
    Task<IReadOnlyList<TvdbEpisodeRecord>> GetSeriesEpisodesAsync(
        string apiKey,
        string? pin,
        int seriesId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Standardimplementierung des TVDB-v4-Clients.
/// </summary>
internal sealed class TvdbClient : ITvdbClient
{
    private static readonly Uri BaseAddress = new("https://api4.thetvdb.com/v4/");

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _authSync = new(1, 1);

    private string? _currentApiKey;
    private string? _currentPin;
    private string? _bearerToken;
    private DateTimeOffset _tokenValidUntilUtc;

    /// <summary>
    /// Erstellt den TVDB-Client optional auf Basis eines bereits vorhandenen <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Optionaler externer HTTP-Client; ohne Angabe wird intern ein eigener Client erzeugt.</param>
    public TvdbClient(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Sucht TVDB-Serien über die v4-API.
    /// </summary>
    /// <param name="apiKey">TVDB-API-Key.</param>
    /// <param name="pin">Optionaler TVDB-PIN.</param>
    /// <param name="query">Freitext-Suchbegriff.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Gefundene TVDB-Serien.</returns>
    public async Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(
        string apiKey,
        string? pin,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        using var response = await SendAuthorizedGetAsync(
            apiKey,
            pin,
            $"search?query={Uri.EscapeDataString(query)}&type=series&limit=20",
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<TvdbSeriesSearchResult>();
        foreach (var item in dataElement.EnumerateArray())
        {
            var id = ReadInt(item, "tvdb_id") ?? ReadInt(item, "id");
            var name = ReadString(item, "name");
            if (id is null || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            results.Add(new TvdbSeriesSearchResult(
                id.Value,
                name.Trim(),
                ReadString(item, "year"),
                ReadString(item, "overview")));
        }

        return results;
    }

    /// <summary>
    /// Lädt alle Episoden einer TVDB-Serie über die v4-API und iteriert dabei automatisch über alle Seiten.
    /// </summary>
    /// <param name="apiKey">TVDB-API-Key.</param>
    /// <param name="pin">Optionaler TVDB-PIN.</param>
    /// <param name="seriesId">TVDB-Serien-ID.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Alle geladenen Episoden der Serie.</returns>
    public async Task<IReadOnlyList<TvdbEpisodeRecord>> GetSeriesEpisodesAsync(
        string apiKey,
        string? pin,
        int seriesId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TvdbEpisodeRecord>();
        var page = 0;

        while (true)
        {
            using var response = await SendAuthorizedGetAsync(
                apiKey,
                pin,
                $"series/{seriesId}/episodes/default?page={page}",
                cancellationToken);
            response.EnsureSuccessStatusCode();

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            if (document.RootElement.TryGetProperty("data", out var dataElement)
                && dataElement.ValueKind == JsonValueKind.Object
                && dataElement.TryGetProperty("episodes", out var episodesElement)
                && episodesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in episodesElement.EnumerateArray())
                {
                    var id = ReadInt(item, "id");
                    var name = ReadString(item, "name");
                    if (id is null || string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    results.Add(new TvdbEpisodeRecord(
                        id.Value,
                        name.Trim(),
                        ReadInt(item, "seasonNumber"),
                        ReadInt(item, "number") ?? ReadInt(item, "episodeNumber"),
                        ReadString(item, "aired")));
                }
            }

            if (!HasNextPage(document.RootElement))
            {
                break;
            }

            page++;
        }

        return results;
    }

    private async Task EnsureAuthenticatedAsync(string apiKey, string? pin, CancellationToken cancellationToken)
    {
        if (HasReusableToken(apiKey, pin))
        {
            return;
        }

        await _authSync.WaitAsync(cancellationToken);
        try
        {
            if (HasReusableToken(apiKey, pin))
            {
                return;
            }

            await AuthenticateAsync(apiKey, pin, cancellationToken);
        }
        finally
        {
            _authSync.Release();
        }
    }

    private async Task ReauthenticateAsync(string apiKey, string? pin, CancellationToken cancellationToken)
    {
        await _authSync.WaitAsync(cancellationToken);
        try
        {
            InvalidateAuthenticationState();
            await AuthenticateAsync(apiKey, pin, cancellationToken);
        }
        finally
        {
            _authSync.Release();
        }
    }

    private async Task AuthenticateAsync(string apiKey, string? pin, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["apikey"] = apiKey
        };

        if (!string.IsNullOrWhiteSpace(pin))
        {
            payload["pin"] = pin;
        }

        using var response = await _httpClient.PostAsJsonAsync(BuildRequestUri("login"), payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var dataElement)
            || !dataElement.TryGetProperty("token", out var tokenElement))
        {
            throw new InvalidOperationException("TVDB-Antwort enthält kein 'data.token'-Feld.");
        }

        var token = tokenElement.GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("TVDB hat kein Bearer-Token geliefert.");
        }

        _currentApiKey = apiKey;
        _currentPin = pin;
        _bearerToken = token;
        _tokenValidUntilUtc = DateTimeOffset.UtcNow.AddDays(30);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<HttpResponseMessage> SendAuthorizedGetAsync(
        string apiKey,
        string? pin,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(apiKey, pin, cancellationToken);

        var requestUri = BuildRequestUri(relativePath);
        var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        if (!ShouldRetryWithFreshAuthentication(response.StatusCode))
        {
            return response;
        }

        response.Dispose();
        await ReauthenticateAsync(apiKey, pin, cancellationToken);
        return await _httpClient.GetAsync(requestUri, cancellationToken);
    }

    private void InvalidateAuthenticationState()
    {
        _currentApiKey = null;
        _currentPin = null;
        _bearerToken = null;
        _tokenValidUntilUtc = DateTimeOffset.MinValue;
        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    /// <summary>
    /// Gibt interne Synchronisationsobjekte sowie optional den besessenen <see cref="HttpClient"/> frei.
    /// </summary>
    public void Dispose()
    {
        _authSync.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private bool HasReusableToken(string apiKey, string? pin)
    {
        return !string.IsNullOrWhiteSpace(_bearerToken)
            && _tokenValidUntilUtc > DateTimeOffset.UtcNow.AddMinutes(5)
            && string.Equals(_currentApiKey, apiKey, StringComparison.Ordinal)
            && string.Equals(_currentPin ?? string.Empty, pin ?? string.Empty, StringComparison.Ordinal);
    }

    private static bool ShouldRetryWithFreshAuthentication(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    }

    private static bool HasNextPage(JsonElement rootElement)
    {
        if (!rootElement.TryGetProperty("links", out var linksElement) || linksElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!linksElement.TryGetProperty("next", out var nextElement))
        {
            return false;
        }

        return nextElement.ValueKind switch
        {
            JsonValueKind.Null => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(nextElement.GetString()),
            JsonValueKind.Number => true,
            JsonValueKind.Object => true,
            _ => false
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out number))
        {
            return number;
        }

        return null;
    }

    private static Uri BuildRequestUri(string relativePath)
    {
        return new(BaseAddress, relativePath);
    }
}
