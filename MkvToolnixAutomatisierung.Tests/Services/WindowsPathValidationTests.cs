using System.IO;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class WindowsPathValidationTests
{
    [Theory]
    [InlineData("CON.mkv")]
    [InlineData("aux.any.mkv")]
    [InlineData("COM1.mkv")]
    [InlineData("LPT\u00b2.mkv")]
    [InlineData("CON .mkv")]
    [InlineData("CONIN$.mkv")]
    [InlineData("Episode.mkv.")]
    [InlineData("..\\Episode.mkv")]
    public void InvalidWindowsNamesAreRejectedBeforeRename(string name) =>
        Assert.Throws<ArgumentException>(() => ArchiveMaintenanceService.BuildManualRenameOperation(@"C:\Archive\Source.mkv", name));

    [Theory]
    [InlineData("Gigolo ist tot....mkv")]
    [InlineData("'Ich werde dich töten'.mkv")]
    [InlineData("COM10.mkv")]
    public void ValidPunctuationAndNonDeviceNamesRemainAllowed(string name) =>
        WindowsPathValidation.ValidateFileName(name);

    [Fact]
    public void SegmentLimitIsValidatedWithoutImposingLegacyMaxPath()
    {
        Assert.Throws<ArgumentException>(() => WindowsPathValidation.ValidateFileName(new string('x', 256)));
        WindowsPathValidation.ValidateFilePath(@"C:\" + new string('x', 200) + "\\" + new string('y', 100) + ".mkv");
        Assert.Throws<PathTooLongException>(() => WindowsPathValidation.ValidateFilePath(@"C:\" + string.Join('\\', Enumerable.Repeat(new string('x', 200), 170))));
    }

    [Fact]
    public void RedirectedDownloadsAreUsedButExplicitProfileOverridesStayIsolated()
    {
        Assert.Equal(@"D:\My downloads", PreferredDownloadDirectoryHelper.ResolveDownloadsDirectory(@"C:\Users\Test", @"C:\Users\Test", () => @"D:\My downloads"));
        Assert.Equal(@"C:\Sandbox\Downloads", PreferredDownloadDirectoryHelper.ResolveDownloadsDirectory(@"C:\Sandbox", @"C:\Users\Test", () => throw new Exception("Must not query real user")));
        Assert.Equal(@"C:\Users\Test\Downloads", PreferredDownloadDirectoryHelper.ResolveDownloadsDirectory(@"C:\Users\Test", @"C:\Users\Test", () => null));
    }
}
