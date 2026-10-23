using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Liest den aktivierten Datenstand aus der Datenbank selbst. Vollprüfungen laufen bei
/// neuen oder geänderten Dateiständen im Worker; unveränderte Start- und Verfügbarkeitsabfragen
/// prüfen nur Struktur und Abschlussmarker.
/// </summary>
internal static class ImdbIndexInspection
{
    // Increase this when integrity rules change, even if the dataset schema stays the same.
    private const int VerificationVersion = 1;
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, CachedFileValue<Snapshot?>> Verified = new(StringComparer.OrdinalIgnoreCase);
    internal sealed record Snapshot(string Version, DateTimeOffset BuiltUtc, int Schema,
        long SeriesCount, long EpisodeCount, long AliasCount);

    internal sealed record StartupInspection(Snapshot? Index, ImdbIndexVerification? Verification);

    /// <summary>
    /// Prüft beim Start nur Struktur und Abschlussmarker, wenn der persistierte Vollprüfstand
    /// noch zu Datei und Datenstand passt. Andernfalls werden Integrität und Importmengen neu geprüft.
    /// </summary>
    /// <param name="path">Aktive Datenbankdatei.</param>
    /// <param name="previous">Persistierter erfolgreicher Prüfstand eines früheren Starts.</param>
    /// <param name="cancellationToken">Bricht auch eine notwendige Vollprüfung ab.</param>
    /// <param name="onFullVerification">Meldet den Beginn der längeren Vollprüfung an die Startanzeige.</param>
    internal static StartupInspection ReadForStartup(string path, ImdbIndexVerification? previous,
        CancellationToken cancellationToken = default, Action? onFullVerification = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stamp = FileStateSnapshot.TryCreate(path);
        if (stamp is null) return new(null, null);
        var quick = Read(path, cancellationToken: cancellationToken);
        if (quick is null) return new(null, null);
        if (previous is not null
            && previous.SeriesCount > 0 && previous.EpisodeCount > 0 && previous.AliasCount >= 0
            && string.Equals(previous.DatabasePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
            && BuildVerificationStamp(stamp.Value) is { } fileStamp && previous.FileStamp == fileStamp
            && previous.DatasetVersion == quick.Version && previous.BuiltUtc == quick.BuiltUtc
            && previous.SchemaVersion == quick.Schema
            && FileStateSnapshot.TryCreate(path) == stamp)
        {
            return new(new Snapshot(quick.Version, quick.BuiltUtc, quick.Schema,
                previous.SeriesCount, previous.EpisodeCount, previous.AliasCount), previous);
        }

        onFullVerification?.Invoke();
        var verified = Read(path, verifyIntegrity: true, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (verified is null || FileStateSnapshot.TryCreate(path) != stamp) return new(null, null);
        return new(verified, CreateVerification(path, verified, stamp));
    }

    /// <summary>
    /// Bindet einen bereits vollständig geprüften Snapshot an den aktuellen Dateistand.
    /// Nach atomarer Aktivierung wird so kein zweiter vollständiger Datenbankscan benötigt.
    /// </summary>
    internal static ImdbIndexVerification? CreateVerification(string path, Snapshot verified,
        FileStateSnapshot? expectedStamp = null)
    {
        var stamp = FileStateSnapshot.TryCreate(path);
        if (stamp is null || (expectedStamp is not null && stamp != expectedStamp)
            || verified.SeriesCount <= 0 || verified.EpisodeCount <= 0) return null;
        var fileStamp = BuildVerificationStamp(stamp.Value);
        if (fileStamp is null) return null;
        var quick = Read(path);
        if (quick is null || quick.Version != verified.Version || quick.BuiltUtc != verified.BuiltUtc
            || quick.Schema != verified.Schema || FileStateSnapshot.TryCreate(path) != stamp) return null;
        return new(Path.GetFullPath(path), fileStamp, verified.Version, verified.BuiltUtc,
            verified.Schema, verified.SeriesCount, verified.EpisodeCount, verified.AliasCount);
    }

    private static string? BuildVerificationStamp(FileStateSnapshot stamp)
    {
        // Length and last-write time alone miss replacements with restored timestamps.
        // Without a native file identity keep using full verification instead of trusting a weak stamp.
        return stamp.Version is { } version
            ? FormattableString.Invariant($"{VerificationVersion}|{stamp.Length}|{stamp.LastWriteTimeUtc.Ticks}|{version.Volume}|{version.FileId}|{version.ChangeTime}")
            : null;
    }

    /// <summary>Eine spätere Vollprüfung geht einem älteren positiven UI-Struktursnapshot vor.</summary>
    internal static bool TryGetVerified(string path, FileStateSnapshot stamp, out Snapshot? result)
    {
        lock (CacheLock)
        {
            if (Verified.TryGetValue(path, out var cached) && cached.Matches(stamp))
            {
                result = cached.Value;
                return true;
            }
        }
        result = null;
        return false;
    }

    public static Snapshot? Read(string path, bool verifyIntegrity = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stamp = FileStateSnapshot.TryCreate(path);
        if (stamp is null) return null;
        if (TryGetVerified(path, stamp.Value, out var cached)) return cached;
        var result = ReadCore(path, verifyIntegrity, cancellationToken);
        if (verifyIntegrity && FileStateSnapshot.TryCreate(path) == stamp)
            lock (CacheLock)
            {
                if (Verified.Count >= 4) Verified.Clear();
                Verified[path] = new CachedFileValue<Snapshot?>(stamp.Value, result);
            }
        return result;
    }

    private static Snapshot? ReadCore(string path, bool verifyIntegrity, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
            }.ToString());
            connection.Open();
            return ImdbSqliteCancellation.Run(connection, cancellationToken, () =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT key, value FROM metadata";
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) values[reader.GetString(0)] = reader.GetString(1);
                if (!values.TryGetValue("version", out var version) || string.IsNullOrWhiteSpace(version)
                    || !DateTimeOffset.TryParse(values.GetValueOrDefault("builtUtc"), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var builtUtc)) return null;
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('titles','aliases')";
                if (Convert.ToInt64(command.ExecuteScalar()) != 2) return null;
                var schema = int.TryParse(values.GetValueOrDefault("schema"), out var parsedSchema) ? parsedSchema : 4;
                if (schema != ImdbDatasetIndexBuilder.SchemaVersion) return null;
                if (!verifyIntegrity) return new Snapshot(version, builtUtc, schema, 0, 0, 0);
                command.CommandText = "PRAGMA quick_check";
                if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.Ordinal)) return null;
                command.CommandText = "SELECT SUM(kind=1), SUM(kind=2) FROM titles";
                long series, episodes;
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read() || reader.IsDBNull(0)) return null;
                    series = reader.GetInt64(0); episodes = reader.GetInt64(1);
                }
                command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='series_aliases')";
                var hasSeriesAliases = Convert.ToInt64(command.ExecuteScalar()) != 0;
                command.CommandText = hasSeriesAliases
                    ? "SELECT (SELECT COUNT(*) FROM aliases) + (SELECT COUNT(*) FROM series_aliases)"
                    : "SELECT COUNT(*) FROM aliases";
                var aliases = Convert.ToInt64(command.ExecuteScalar());
                return series > 0 && episodes > 0 ? new Snapshot(version, builtUtc, schema, series, episodes, aliases) : null;
            });
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    /// <summary>Ein starker Datenverlust gegenüber einem realen Altbestand verlangt Prüfung statt Aktivierung.</summary>
    public static void EnsurePlausibleReplacement(Snapshot candidate, Snapshot? previous)
    {
        if (previous is null) return;
        Check("Serien", candidate.SeriesCount, previous.SeriesCount);
        Check("Episoden", candidate.EpisodeCount, previous.EpisodeCount);
        Check("Aliasnamen", candidate.AliasCount, previous.AliasCount);
        static void Check(string label, long current, long old)
        {
            if (old >= 100 && current < old * 0.8)
                throw new InvalidDataException($"IMDb-Import unplausibel: {label} von {old:N0} auf {current:N0} gesunken. Der bisherige Index bleibt erhalten.");
        }
    }
}
