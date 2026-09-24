using System.IO;
using System.Text.Json;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class ArchiveChangeJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "archive-journal-" + Guid.NewGuid().ToString("N"));
    public ArchiveChangeJournalTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void RecordsIntentBeforeMutationAndRetainsInterruptedStage()
    {
        var request = new ArchiveMaintenanceApplyRequest(Path.Combine(_root, "episode.mkv"), null, null, [], null);
        var journal = new ArchiveChangeJournal(request);
        Assert.Empty(Directory.GetFiles(_root));
        journal.BeforeStep("MKV-Header");
        journal.BeforeStep("NFO-Titel und Sperren");
        var recovered = JsonSerializer.Deserialize<ArchiveChangeRecovery>(File.ReadAllText(journal.PathName))!;
        Assert.Equal(request.FilePath, recovered.Request.FilePath);
        Assert.Equal("NFO-Titel und Sperren", recovered.Stage);
        Assert.Contains("neu scannen", journal.RecoveryMessage);
    }

    [Fact]
    public void CompletedRequestRemovesJournalAndTemporaryBackups()
    {
        var journal = new ArchiveChangeJournal(new(Path.Combine(_root, "episode.mkv"), null, null, [], null));
        journal.BeforeStep("MKV-Header");
        journal.Complete();
        Assert.Empty(Directory.GetFiles(_root));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
