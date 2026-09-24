using System.Text.Json;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Dauerhafte Wiederaufnahmeinformation für mehrstufige Archivänderungen. Der nächste
/// Schreibschritt wird vor seiner Ausführung geflusht. Nach einem Absturz ist damit auch
/// ein möglicherweise nur teilweise ausgeführtes mkvpropedit erkennbar.
/// </summary>
/// <remarks>
/// Es wird bewusst keine atomare Änderung einer beliebig großen MKV versprochen. Die
/// Wiederaufnahme erfolgt über einen neuen Scan des tatsächlichen Zustands, nicht durch
/// blindes Wiederholen alter Pläne oder Rückschreiben möglicherweise überholter NFOs.
/// </remarks>
internal sealed class ArchiveChangeJournal(ArchiveMaintenanceApplyRequest request)
{
    public string PathName { get; } = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(request.FilePath))!, $".archive-change-{Guid.NewGuid():N}.json");
    public string? Stage { get; private set; }

    public void BeforeStep(string stage)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ArchiveChangeRecovery(request, stage, DateTimeOffset.UtcNow));
        if (Stage is null)
        {
            using var stream = new FileStream(PathName, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        else
        {
            new SmallFileUpdate(PathName).Commit(bytes);
        }
        Stage = stage;
    }

    public string RecoveryMessage => $"Änderungen können bereits teilweise angewendet sein (Schritt: {Stage}). Vor erneutem Schreiben bitte neu scannen. Vorgang: {PathName}";

    public void Complete()
    {
        if (Stage is null) return;
        // Ein übrig gebliebenes abgeschlossenes Journal darf nicht als offener Auftrag gelten.
        BeforeStep("Abgeschlossen");
        try { File.Delete(PathName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Versionierbarer Datensatz für die explizite Wiederaufnahmeprüfung.</summary>
internal sealed record ArchiveChangeRecovery(ArchiveMaintenanceApplyRequest Request, string Stage, DateTimeOffset UpdatedAt, int Version = 1);
