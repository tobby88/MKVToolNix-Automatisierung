using System.IO;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class MuxOutputTransactionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mkv-output-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Commit_PublishesCompleteOutput_OnlyAfterSuccess(bool existingTarget)
    {
        Directory.CreateDirectory(_directory);
        var output = Path.Combine(_directory, "episode.mkv");
        if (existingTarget) File.WriteAllText(output, "old archive");
        using (var transaction = new MuxOutputTransaction(output))
        {
            File.WriteAllText(transaction.TemporaryOutputPath, "new archive");
            Assert.Equal(".tmp", Path.GetExtension(transaction.TemporaryOutputPath));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(transaction.TemporaryOutputPath)!, "*.mkv"));
            Assert.Equal(existingTarget, File.Exists(output));
            if (existingTarget) Assert.Equal("old archive", File.ReadAllText(output));
            transaction.Commit(CancellationToken.None);
            Assert.Equal("new archive", File.ReadAllText(output));
        }
        Assert.Empty(Directory.GetDirectories(_directory));
    }

    [Fact]
    public void Dispose_WithoutCommit_PreservesArchiveAndRemovesPartialOutput()
    {
        Directory.CreateDirectory(_directory);
        var output = Path.Combine(_directory, "episode.mkv");
        File.WriteAllText(output, "old archive");
        using (var transaction = new MuxOutputTransaction(output))
        {
            File.WriteAllText(transaction.TemporaryOutputPath, "incomplete");
        }
        Assert.Equal("old archive", File.ReadAllText(output));
        Assert.Empty(Directory.GetDirectories(_directory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Commit_RejectsMissingOrEmptyOutput(bool createsEmptyFile)
    {
        Directory.CreateDirectory(_directory);
        var output = Path.Combine(_directory, "episode.mkv");
        File.WriteAllText(output, "old archive");
        using var transaction = new MuxOutputTransaction(output);
        if (createsEmptyFile) File.WriteAllText(transaction.TemporaryOutputPath, "");
        Assert.Throws<IOException>(() => transaction.Commit(CancellationToken.None));
        Assert.Equal("old archive", File.ReadAllText(output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Commit_DoesNotOverwriteNewOrChangedTarget(bool existingTarget)
    {
        Directory.CreateDirectory(_directory);
        var output = Path.Combine(_directory, "episode.mkv");
        if (existingTarget) File.WriteAllText(output, "old");
        using var transaction = new MuxOutputTransaction(output);
        File.WriteAllText(transaction.TemporaryOutputPath, "our new archive");
        File.WriteAllText(output, "concurrent external changes");
        Assert.Throws<IOException>(() => transaction.Commit(CancellationToken.None));
        Assert.Equal("concurrent external changes", File.ReadAllText(output));
    }

    [Fact]
    public void Commit_CancellationAfterMux_DoesNotReplaceTarget()
    {
        Directory.CreateDirectory(_directory);
        var output = Path.Combine(_directory, "episode.mkv");
        File.WriteAllText(output, "old archive");
        using var transaction = new MuxOutputTransaction(output);
        File.WriteAllText(transaction.TemporaryOutputPath, "new archive");
        Assert.Throws<OperationCanceledException>(() => transaction.Commit(new CancellationToken(true)));
        Assert.Equal("old archive", File.ReadAllText(output));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
