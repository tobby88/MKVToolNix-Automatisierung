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
}

/// <summary>
/// Einzelner Eintrag des strukturierten Batch-Metadatenreports.
/// </summary>
public sealed class BatchOutputMetadataEntry
{
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
    /// Provider-IDs, die nachgelagerte Metadaten-Workflows direkt übernehmen können.
    /// </summary>
    public BatchOutputProviderIds? ProviderIds { get; init; } = new();

    /// <summary>
    /// Zusatzdaten zur ursprünglichen TVDB-Auswahl, falls eine solche Auswahl vorlag.
    /// </summary>
    public BatchOutputTvdbMetadata? Tvdb { get; init; }
}

/// <summary>
/// Provider-IDs eines neu erzeugten Batch-Ausgabeeintrags.
/// </summary>
public sealed class BatchOutputProviderIds
{
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
        return JsonSerializer.Deserialize<BatchOutputMetadataReport>(json, JsonOptions);
    }
}
