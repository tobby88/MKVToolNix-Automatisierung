using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Liest den aktivierten Datenstand aus der Datenbank selbst. Vollprüfungen laufen im
/// Update-Worker; die häufige Verfügbarkeitsabfrage prüft nur Struktur und Abschlussmarker.
/// </summary>
internal static class ImdbIndexInspection
{
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, CachedFileValue<Snapshot?>> Verified = new(StringComparer.OrdinalIgnoreCase);
    internal sealed record Snapshot(string Version, DateTimeOffset BuiltUtc, int Schema,
        long SeriesCount, long EpisodeCount, long AliasCount);

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
