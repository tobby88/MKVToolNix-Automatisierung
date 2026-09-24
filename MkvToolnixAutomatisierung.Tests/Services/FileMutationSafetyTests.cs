using System.IO;
using System.Runtime.InteropServices;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class FileMutationSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mutation-safety-" + Guid.NewGuid().ToString("N"));
    public FileMutationSafetyTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void OrdinaryAndNotYetExistingPathsAreAllowed()
    {
        var path = Path.Combine(_root, "episode.mkv");
        File.WriteAllText(path, "test");
        FileMutationSafety.EnsureOrdinaryFile(path);
        FileMutationSafety.EnsureOrdinaryFile(Path.Combine(_root, "new", "episode.mkv"));
    }

    [Fact]
    public void HardlinkedFileIsRejectedWithoutChangingEitherEntry()
    {
        var path = Path.Combine(_root, "episode.mkv");
        var alias = Path.Combine(_root, "alias.mkv");
        File.WriteAllText(path, "original");
        Assert.True(CreateHardLink(alias, path, IntPtr.Zero));
        Assert.Throws<IOException>(() => FileMutationSafety.EnsureOrdinaryFile(path));
        Assert.Throws<IOException>(() => new MuxOutputTransaction(alias));
        Assert.Equal("original", File.ReadAllText(alias));
    }

    [Fact]
    public void JunctionAncestorIsRejectedEvenWhenFinalFileDoesNotExist()
    {
        var target = Path.Combine(_root, "target");
        var link = Path.Combine(_root, "link");
        Directory.CreateDirectory(target);
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { CreateNoWindow = true, UseShellExecute = false };
        foreach (var argument in new[] { "/c", "mklink", "/j", link, target }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        try
        {
            Assert.Throws<IOException>(() => FileMutationSafety.EnsureOrdinaryFile(Path.Combine(link, "new", "episode.mkv")));
        }
        finally
        {
            // Nur den selbst erzeugten Junction-Eintrag entfernen, niemals sein Ziel rekursiv.
            Directory.Delete(link);
        }
    }

    public void Dispose() => Directory.Delete(_root, true);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newName, string existingName, IntPtr security);
}
