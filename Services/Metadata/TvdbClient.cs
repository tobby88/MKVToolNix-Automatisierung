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
    /// <param name="apiKey">TVDB-API-Key.</param>
    /// <param name="pin">Optionaler TVDB-PIN.</param>
    /// <param name="seriesId">TVDB-Serien-ID.</param>
    /// <param name="language">
    /// Optionaler ISO-639-2-Sprachcode (z. B. <c>deu</c>) für sprachspezifische Episodentitel.
    /// Liefert der Sprach-Endpunkt keine Ergebnisse, wird automatisch auf den sprachneutralen Endpunkt zurückgefallen.
    /// </param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    Task<IReadOnlyList<TvdbEpisodeRecord>> GetSeriesEpisodesAsync(
        string apiKey,
        string? pin,
        int seriesId,
        string? language = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Standardimplementierung des TVDB-v4-Clients.
/// </summary>
internal sealed class TvdbClient : ITvdbClient
{
    private const int MaxEpisodePages = 100;
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
            // TVDB liefert Übersetzungen im translations-Objekt als Sprachcode→Name-Map.
            // Bevorzuge deutschen Namen (translations.deu), dann name_translated, dann Originalname.
            var name = ReadTranslationString(item, "translations", "deu")
                    ?? ReadString(item, "name_translated")
                    ?? ReadString(item, "name");
            if (id is null || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            results.Add(new TvdbSeriesSearchResult(
                id.Value,
                name.Trim(),
                ReadString(item, "year"),
                ReadTranslationString(item, "overviews", "deu") ?? ReadString(item, "overview"),
                ReadString(item, "primary_language")));
        }

        return results;
    }

    /// <summary>
    /// Lädt alle Episoden einer TVDB-Serie über die v4-API und iteriert dabei automatisch über alle Seiten.
    /// </summary>
    /// <param name="apiKey">TVDB-API-Key.</param>
    /// <param name="pin">Optionaler TVDB-PIN.</param>
    /// <param name="seriesId">TVDB-Serien-ID.</param>
    /// <param name="language">
    /// Optionaler ISO-639-2-Sprachcode (z. B. <c>deu</c>) für sprachspezifische Episodentitel.
    /// Liefert der Sprach-Endpunkt keine Ergebnisse, wird automatisch auf den sprachneutralen Endpunkt zurückgefallen.
    /// </param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Alle geladenen Episoden der Serie.</returns>
    public async Task<IReadOnlyList<TvdbEpisodeRecord>> GetSeriesEpisodesAsync(
        string apiKey,
        string? pin,
        int seriesId,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var useLanguage = !string.IsNullOrWhiteSpace(language);
        var localizedResults = await FetchSeriesEpisodesInternalAsync(
            apiKey, pin, seriesId, useLanguage ? language!.Trim() : null, cancellationToken);
        if (!useLanguage)
        {
            return localizedResults;
        }

        // Der sprachspezifische Endpunkt kann einzelne Episoden ohne Uebersetzung auslassen oder ohne
        // Titel liefern. Deshalb wird der sprachneutrale Endpunkt immer als Vollstaendigkeits-Fallback
        // nachgeladen und pro Episode nur dort herangezogen, wo die lokalisierte Antwort Luecken hat.
        var neutralResults = await FetchSeriesEpisodesInternalAsync(
            apiKey,
            pin,
            seriesId,
            language: null,
            cancellationToken);
        if (localizedResults.Count == 0)
        {
            return neutralResults;
        }

        if (neutralResults.Count == 0)
        {
            return localizedResults;
        }

        return MergeEpisodeRecords(localizedResults, neutralResults);
    }

    private async Task<List<TvdbEpisodeRecord>> FetchSeriesEpisodesInternalAsync(
        string apiKey,
        string? pin,
        int seriesId,
        string? language,
        CancellationToken cancellationToken)
    {
        var episodePath = string.IsNullOrWhiteSpace(language)
            ? $"series/{seriesId}/episodes/default"
            : $"series/{seriesId}/episodes/default/{language}";

        var results = new List<TvdbEpisodeRecord>();
        var page = 0;
        var pageCount = 0;

        while (true)
        {
            if (pageCount++ >= MaxEpisodePages)
            {
                throw new InvalidOperationException($"TVDB-Pagination abgebrochen, weil mehr als {MaxEpisodePages} Episodenseiten angefordert wurden.");
            }

            using var response = await SendAuthorizedGetAsync(
                apiKey,
                pin,
                $"{episodePath}?page={page}",
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

    /// <summary>
    /// Liest einen Sprachcode-Schlüssel aus einem eingebetteten Übersetzungs-Objekt (z. B. translations.deu).
    /// </summary>
    private static string? ReadTranslationString(JsonElement element, string translationsProperty, string languageKey)
    {
        return element.TryGetProperty(translationsProperty, out var translations)
            && translations.ValueKind == JsonValueKind.Object
            ? ReadString(translations, languageKey)
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

    private static IReadOnlyList<TvdbEpisodeRecord> MergeEpisodeRecords(
        IReadOnlyList<TvdbEpisodeRecord> localizedResults,
        IReadOnlyList<TvdbEpisodeRecord> neutralResults)
    {
        var localizedById = localizedResults
            .GroupBy(episode => episode.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var mergedEpisodes = new List<TvdbEpisodeRecord>(neutralResults.Count + localizedResults.Count);
        var seenIds = new HashSet<int>();

        foreach (var neutralEpisode in neutralResults)
        {
            seenIds.Add(neutralEpisode.Id);
            if (localizedById.TryGetValue(neutralEpisode.Id, out var localizedEpisode))
            {
                mergedEpisodes.Add(new TvdbEpisodeRecord(
                    neutralEpisode.Id,
                    string.IsNullOrWhiteSpace(localizedEpisode.Name) ? neutralEpisode.Name : localizedEpisode.Name,
                    localizedEpisode.SeasonNumber ?? neutralEpisode.SeasonNumber,
                    localizedEpisode.EpisodeNumber ?? neutralEpisode.EpisodeNumber,
                    string.IsNullOrWhiteSpace(localizedEpisode.Aired) ? neutralEpisode.Aired : localizedEpisode.Aired));
                continue;
            }

            mergedEpisodes.Add(neutralEpisode);
        }

        foreach (var localizedEpisode in localizedResults.Where(episode => !seenIds.Contains(episode.Id)))
        {
            mergedEpisodes.Add(localizedEpisode);
        }

        return mergedEpisodes;
    }

    private static Uri BuildRequestUri(string relativePath)
    {
        return new(BaseAddress, relativePath);
    }
}
