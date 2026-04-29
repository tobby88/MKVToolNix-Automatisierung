using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Übersetzt technische Provider-Ausnahmen in kurze Statusmeldungen für die GUI.
/// </summary>
internal static class ProviderLookupErrorFormatter
{
    public static string FormatTvdbSearchFailure(Exception exception)
    {
        return $"TVDB-Suche nicht möglich: {BuildReason(exception)}";
    }

    public static string FormatTvdbEpisodeFailure(Exception exception)
    {
        return $"TVDB-Episodenliste nicht möglich: {BuildReason(exception)}";
    }

    public static string FormatTvdbAutomaticFailure(Exception exception)
    {
        return $"TVDB-Automatik nicht möglich: {BuildReason(exception)}";
    }

    public static string FormatImdbSearchFailure(Exception exception)
    {
        return $"IMDb-Suche über imdbapi.dev nicht möglich: {BuildReason(exception)}";
    }

    public static string FormatImdbEpisodeFailure(Exception exception)
    {
        return $"IMDb-Episodenliste über imdbapi.dev nicht möglich: {BuildReason(exception)}";
    }

    public static string FormatImdbFallbackReason(Exception exception)
    {
        return $"imdbapi.dev ist derzeit nicht erreichbar: {BuildReason(exception)}";
    }

    private static string BuildReason(Exception exception)
    {
        if (exception is HttpRequestException httpException)
        {
            return BuildHttpReason(httpException);
        }

        if (exception is TaskCanceledException or TimeoutException)
        {
            return "Zeitüberschreitung bei der Provider-Abfrage. Bitte Verbindung prüfen oder später erneut versuchen.";
        }

        if (exception is JsonException)
        {
            return "Der Provider hat eine unerwartete Antwort geliefert. Bitte später erneut versuchen.";
        }

        return string.IsNullOrWhiteSpace(exception.Message)
            ? "Unbekannter Provider-Fehler. Bitte später erneut versuchen."
            : $"{exception.Message.Trim()} Bitte später erneut versuchen.";
    }

    private static string BuildHttpReason(HttpRequestException exception)
    {
        return exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => "Der Provider lehnt die Anfrage ab. Bitte API-Key und Zugangsdaten prüfen.",
            HttpStatusCode.TooManyRequests
                => "Das Provider-Limit ist erreicht. Bitte später erneut versuchen.",
            HttpStatusCode.NotFound
                => "Der Provider-Endpunkt oder Eintrag wurde nicht gefunden. Bitte später erneut versuchen.",
            { } statusCode when (int)statusCode >= 500
                => "Der Provider-Server meldet einen Fehler. Bitte später erneut versuchen.",
            { } statusCode
                => $"Der Provider meldet HTTP {(int)statusCode}. Bitte Zugangsdaten und Verbindung prüfen.",
            null
                => BuildNetworkReason(exception)
        };
    }

    private static string BuildNetworkReason(Exception exception)
    {
        return string.IsNullOrWhiteSpace(exception.Message)
            ? "Netzwerkfehler. Bitte Internetverbindung und DNS prüfen."
            : $"Netzwerkfehler. Bitte Internetverbindung und DNS prüfen. Details: {exception.Message.Trim()}";
    }
}
