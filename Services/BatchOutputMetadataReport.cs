using System.Text.Json;
using System.Text.Json.Serialization;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Maschinenlesbarer Zusatzreport zu den in einem Batch-Lauf neu erzeugten Ausgabedateien.
/// </summary>
/// <remarks>
/// Das bestehende menschenlesbare Log und die bisherige reine Pfadliste bleiben bewusst erhalten.
/// Dieses Objekt bildet nur die erweiterbare Sidecar-Datei ab, damit nachgelagerte Module wie der
/// Emby-Abgleich Provider-IDs und Episodenmetadaten zuverlässig importieren können.
/// </remarks>
public sealed class BatchOutputMetadataReport
{
    /// <summary>Unbekannte optionale Felder bleiben beim Emby-Roundtrip erhalten.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>
    /// Version des JSON-Schemas. Neue optionale Felder dürfen ohne Versionssprung ergänzt werden;
    /// inkompatible Strukturänderungen müssen diese Zahl erhöhen.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Zeitpunkt, zu dem der Report geschrieben wurde.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Quellordner des Batch-Laufs.
    /// </summary>
    public string SourceDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Ausgabeordner, in den der Batch-Lauf geschrieben hat.
    /// </summary>
    public string OutputDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Neu erzeugte Ausgabedateien samt optionaler Metadaten für Folgeprozesse.
    /// </summary>
    public List<BatchOutputMetadataEntry> Items { get; init; } = [];

    /// <summary>
    /// Zeitpunkt, zu dem alle relevanten Einträge im Emby-Abgleich abgeschlossen wurden.
    /// </summary>
    public DateTimeOffset? EmbySyncCompletedAt { get; set; }
}

/// <summary>
/// Einzelner Eintrag des strukturierten Batch-Metadatenreports.
/// </summary>
public sealed class BatchOutputMetadataEntry
{
    /// <summary>Zusatzdaten anderer Report-Verbraucher, die Emby nicht verändern darf.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>
    /// Vollständiger Pfad zur neu erzeugten MKV-Datei.
    /// </summary>
    public string OutputPath { get; init; } = string.Empty;

    /// <summary>
    /// Erwarteter Pfad der direkt neben der MKV liegenden NFO-Datei.
    /// </summary>
    public string? NfoPath { get; init; }

    /// <summary>
    /// Fachlich verwendeter Serienname.
    /// </summary>
    public string? SeriesName { get; init; }

    /// <summary>
    /// Fachlich verwendete Staffelnummer.
    /// </summary>
    public string? SeasonNumber { get; init; }

    /// <summary>
    /// Fachlich verwendete Episodennummer oder Mehrfachfolgen-Kennung.
    /// </summary>
    public string? EpisodeNumber { get; init; }

    /// <summary>
    /// Fachlich verwendeter Episodentitel.
    /// </summary>
    public string? EpisodeTitle { get; init; }

    /// <summary>
    /// Direkt lesbare TVDB-Episoden-ID. Sie dupliziert bewusst <see cref="ProviderIds"/>
    /// für nachgelagerte einfache JSON-Verbraucher, während <c>providerIds.tvdb</c>
    /// als kompatibler erweiterbarer Provider-ID-Block erhalten bleibt.
    /// </summary>
    public string? TvdbEpisodeId { get; init; }

    /// <summary>
    /// Provider-IDs, die nachgelagerte Metadaten-Workflows direkt übernehmen können.
    /// </summary>
    public BatchOutputProviderIds? ProviderIds { get; init; } = new();

    /// <summary>
    /// Zusatzdaten zur ursprünglichen TVDB-Auswahl, falls eine solche Auswahl vorlag.
    /// </summary>
    public BatchOutputTvdbMetadata? Tvdb { get; init; }

    /// <summary>
    /// Wird vom Emby-Abgleich gesetzt, sobald diese MKV dort erfolgreich abgearbeitet wurde.
    /// Nullable, damit neue Reports nicht mit lauter <c>false</c>-Feldern aufgebläht werden.
    /// </summary>
    public bool? EmbySyncDone { get; set; }

    /// <summary>
    /// Zeitpunkt der erfolgreichen Emby-Abgleich-Bearbeitung dieser MKV.
    /// </summary>
    public DateTimeOffset? EmbySyncDoneAt { get; set; }

    /// <summary>
    /// Zuletzt gespeicherte Emby-Auswahl, getrennt von den ursprünglichen Mux-Metadaten.
    /// Fehlende Werte in alten Reports bedeuten ausdrücklich noch keine bewusste Ablehnung.
    /// </summary>
    public BatchOutputEmbyReview? EmbyReview { get; set; }
}

/// <summary>
/// Wiederaufnehmbare Provider-Auswahl. Eine bewusst fehlende ID wird je Anbieter gespeichert;
/// dadurch kann ein leerer Treffer von einer noch ausstehenden Zuordnung unterschieden werden.
/// </summary>
public sealed record BatchOutputEmbyReview
{
    /// <summary>Optionale Review-Erweiterungen neuerer kompatibler Versionen.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>Zuletzt gewählte TVDB-Episoden-ID.</summary>
    public string? TvdbId { get; init; }
    /// <summary>Zuletzt gewählte IMDb-ID.</summary>
    public string? ImdbId { get; init; }
    /// <summary>Der Benutzer hat bewusst keinen passenden TVDB-Eintrag zugeordnet.</summary>
    public bool TvdbUnavailable { get; init; }
    /// <summary>Der Benutzer hat bewusst keinen passenden IMDb-Eintrag zugeordnet.</summary>
    public bool ImdbUnavailable { get; init; }
    /// <summary>Die TVDB-Auswahl wurde manuell bestätigt, nicht nur automatisch übernommen.</summary>
    public bool TvdbManuallyReviewed { get; init; }
    /// <summary>Die IMDb-Auswahl wurde manuell bestätigt, nicht nur automatisch übernommen.</summary>
    public bool ImdbManuallyReviewed { get; init; }
}

/// <summary>
/// Provider-IDs eines neu erzeugten Batch-Ausgabeeintrags.
/// </summary>
public sealed class BatchOutputProviderIds
{
    /// <summary>Weitere Anbieter werden unverändert durchgereicht.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>
    /// TVDB-Episoden-ID, die in eine Episoden-NFO als TVDB-ID übernommen werden kann.
    /// </summary>
    public string? Tvdb { get; init; }

    /// <summary>
    /// IMDB-ID, sobald sie durch einen späteren Workflow ergänzt wurde.
    /// </summary>
    public string? Imdb { get; init; }
}

/// <summary>
/// Strukturierte Details zur TVDB-Zuordnung einer neu erzeugten Ausgabedatei.
/// </summary>
public sealed class BatchOutputTvdbMetadata
{
    /// <summary>Weitere Herkunftsmetadaten bleiben beim Emby-Roundtrip erhalten.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>
    /// TVDB-Serien-ID.
    /// </summary>
    public int? SeriesId { get; init; }

    /// <summary>
    /// TVDB-Serienname, der bei der Zuordnung verwendet wurde.
    /// </summary>
    public string? SeriesName { get; init; }

    /// <summary>
    /// TVDB-Episoden-ID. Diese ID ist für Episoden-NFOs die wichtigste TVDB-ID.
    /// </summary>
    public int? EpisodeId { get; init; }
}

/// <summary>
/// Einheitlicher JSON-Zugriff für den strukturierten Batch-Metadatenreport.
/// </summary>
internal static class BatchOutputMetadataReportJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(BatchOutputMetadataReport report)
    {
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    public static BatchOutputMetadataReport? Deserialize(string json)
    {
        var report = JsonSerializer.Deserialize<BatchOutputMetadataReport>(json, JsonOptions);
        if (report is not null)
        {
            if (report.SchemaVersion != 1)
            {
                throw new InvalidDataException($"Nicht unterstützte Metadatenreport-Version: {report.SchemaVersion}.");
            }

            if (report.Items is null || report.Items.Any(item => item is null))
            {
                throw new InvalidDataException("Der Metadatenreport enthält keine gültige Eintragsliste.");
            }
        }

        return report;
    }
}
