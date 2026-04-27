using System.Diagnostics;
using System.IO;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class MediathekViewLauncherTests : IDisposable
{
    private readonly string _tempDirectory;

    public MediathekViewLauncherTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-mediathekview-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void PathResolver_UsesConfiguredExecutable()
    {
        var executablePath = CreateFile(Path.Combine("portable", "MediathekView.exe"));

        var resolved = MediathekViewPathResolver.TryResolve(new AppToolPathSettings
        {
            MediathekViewPath = executablePath
        });

        Assert.NotNull(resolved);
        Assert.Equal(executablePath, resolved.Path);
        Assert.Equal(ToolPathResolutionSource.ManualOverride, resolved.Source);
    }

    [Fact]
    public void PathResolver_UsesPortableDownloadFallback()
    {
        var userProfileDirectory = CreateDirectory("profile");
        var portableDirectory = Path.Combine(userProfileDirectory, "Downloads", "MediathekView-latest-win");
        Directory.CreateDirectory(portableDirectory);
        var executablePath = Path.Combine(portableDirectory, "MediathekView.exe");
        File.WriteAllText(executablePath, "tool");
        var originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var originalHome = Environment.GetEnvironmentVariable("HOME");

        try
        {
            Environment.SetEnvironmentVariable("USERPROFILE", userProfileDirectory);
            Environment.SetEnvironmentVariable("HOME", userProfileDirectory);

            var resolved = MediathekViewPathResolver.TryResolve(new AppToolPathSettings());

            Assert.NotNull(resolved);
            Assert.Equal(executablePath, resolved.Path);
            Assert.Equal(ToolPathResolutionSource.DownloadsFallback, resolved.Source);
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile);
            Environment.SetEnvironmentVariable("HOME", originalHome);
        }
    }

    [Fact]
    public void Launch_UsesResolvedExecutableAndWorkingDirectory()
    {
        var executablePath = CreateFile(Path.Combine("portable", "MediathekView.exe"));
        ProcessStartInfo? capturedStartInfo = null;
        var launcher = new MediathekViewLauncher(
            new StubMediathekViewLocator(new ResolvedToolPath(executablePath, ToolPathResolutionSource.ManualOverride)),
            startInfo =>
            {
                capturedStartInfo = startInfo;
                return true;
            });

        var result = launcher.Launch();

        Assert.True(result.IsSuccess);
        Assert.Equal(executablePath, result.ExecutablePath);
        Assert.NotNull(capturedStartInfo);
        Assert.Equal(executablePath, capturedStartInfo.FileName);
        Assert.Equal(Path.GetDirectoryName(executablePath), capturedStartInfo.WorkingDirectory);
        Assert.True(capturedStartInfo.UseShellExecute);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(_tempDirectory, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private string CreateFile(string relativePath)
    {
        var path = Path.Combine(_tempDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "tool");
        return path;
    }

    private sealed class StubMediathekViewLocator(ResolvedToolPath? resolvedPath) : IMediathekViewLocator
    {
        public ResolvedToolPath? TryFindMediathekView()
        {
            return resolvedPath;
        }
    }
}
