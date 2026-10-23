namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Persistierter Abschluss einer vollständigen IMDb-Datenbankprüfung. Der Dateistempel
/// bindet ihn an genau den geprüften Dateistand; die Importmengen bleiben für Updatevergleiche erhalten.
/// </summary>
/// <param name="DatabasePath">Absoluter Pfad der geprüften Datenbank.</param>
/// <param name="FileStamp">Versionierter Stempel aus Dateigröße, Zeitstempeln und Windows-Dateikennung.</param>
/// <param name="DatasetVersion">Revisionskennung aus der geprüften Datenbank.</param>
/// <param name="BuiltUtc">In der Datenbank gespeicherter Aufbauzeitpunkt.</param>
/// <param name="SchemaVersion">Geprüfte Schemafassung.</param>
/// <param name="SeriesCount">Vollständig ermittelte Anzahl der Serien.</param>
/// <param name="EpisodeCount">Vollständig ermittelte Anzahl der Episoden.</param>
/// <param name="AliasCount">Vollständig ermittelte Anzahl der Aliasnamen.</param>
public sealed record ImdbIndexVerification(
    string DatabasePath,
    string FileStamp,
    string DatasetVersion,
    DateTimeOffset BuiltUtc,
    int SchemaVersion,
    long SeriesCount,
    long EpisodeCount,
    long AliasCount);
