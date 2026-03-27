using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MkvToolnixAutomatisierung.Services.Metadata;

public sealed class TvdbClient
{
    private static readonly Uri BaseAddress = new("https://api4.thetvdb.com/v4/");

    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = BaseAddress,
        Timeout = TimeSpan.FromSeconds(30)
    };
    private readonly SemaphoreSlim _authSync = new(1, 1);

    private string? _currentApiKey;
    private string? _currentPin;
    private string? _bearerToken;
    private DateTimeOffset _tokenValidUntilUtc;

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

        await EnsureAuthenticatedAsync(apiKey, pin, cancellationToken);

        using var response = await _httpClient.GetAsync(
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

    public async Task<IReadOnlyList<TvdbEpisodeRecord>> GetSeriesEpisodesAsync(
        string apiKey,
        string? pin,
        int seriesId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(apiKey, pin, cancellationToken);

        var results = new List<TvdbEpisodeRecord>();
        var page = 0;

        while (true)
        {
            using var response = await _httpClient.GetAsync(
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

            var payload = new Dictionary<string, string>
            {
                ["apikey"] = apiKey
            };

            if (!string.IsNullOrWhiteSpace(pin))
            {
                payload["pin"] = pin;
            }

            using var response = await _httpClient.PostAsJsonAsync("login", payload, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            var token = document.RootElement
                .GetProperty("data")
                .GetProperty("token")
                .GetString();

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
        finally
        {
            _authSync.Release();
        }
    }

    private bool HasReusableToken(string apiKey, string? pin)
    {
        return !string.IsNullOrWhiteSpace(_bearerToken)
            && _tokenValidUntilUtc > DateTimeOffset.UtcNow.AddMinutes(5)
            && string.Equals(_currentApiKey, apiKey, StringComparison.Ordinal)
            && string.Equals(_currentPin ?? string.Empty, pin ?? string.Empty, StringComparison.Ordinal);
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
}
