using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Bricht auch synchron laufende SQLite-Sortierungen ab. SqliteCommand.Cancel allein tut dies nicht.
/// Nur für eine exklusiv vom Aufrufer verwendete, offene Verbindung einsetzen.
/// </summary>
internal static class ImdbSqliteCancellation
{
    internal static T Run<T>(SqliteConnection connection, CancellationToken cancellationToken, Func<T> action)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!cancellationToken.CanBeCanceled)
        {
            return action();
        }

        raw.sqlite3_progress_handler(connection.Handle!, 1000,
            static state => ((CancellationToken)state).IsCancellationRequested ? 1 : 0, cancellationToken);
        try
        {
            var result = action();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == raw.SQLITE_INTERRUPT && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("IMDb-SQLite-Operation abgebrochen.", ex, cancellationToken);
        }
        finally
        {
            raw.sqlite3_progress_handler(connection.Handle!, 0, null!, null!);
        }
    }
}
