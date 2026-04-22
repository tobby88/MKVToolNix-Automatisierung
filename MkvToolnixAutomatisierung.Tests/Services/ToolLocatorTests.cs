using System.IO;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

[Collection("PortableStorage")]
public sealed class ToolLocatorTests : IDisposable
{
    private readonly PortableStorageFixture _storageFixture;
    private readonly string _tempDirectory;

    public ToolLocatorTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-tool-locator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void FfprobeLocator_PrefersManualOverrideBeforeManagedInstallation()
    {
        var manualPath = CreateFile(Path.Combine("manual", "ffprobe.exe"));
        var managedPath = CreateFile(Path.Combine("managed", "ffprobe.exe"));
        var settingsStore = new AppSettingsStore();
        settingsStore.Save(new CombinedAppSettings
        {
            ToolPaths = new AppToolPathSettings
            {
                FfprobePath = manualPath,
                ManagedFfprobe = new ManagedToolSettings
                {
                    InstalledPath = managedPath,
                    InstalledVersion = "2026-04-18T13-04-00Z"
                }
            }
        });

        var locator = new FfprobeLocator(new AppToolPathStore(settingsStore));

        Assert.Equal(manualPath, locator.TryFindFfprobePath());
    }

    [Fact]
    public void MkvToolNixLocator_UsesManagedInstallationWhenNoManualOverrideExists()
    {
        var managedDirectory = CreateDirectory("managed-mkvtoolnix");
        _ = CreateFile(Path.Combine("managed-mkvtoolnix", "mkvmerge.exe"));
        _ = CreateFile(Path.Combine("managed-mkvtoolnix", "mkvpropedit.exe"));
        var settingsStore = new AppSettingsStore();
        settingsStore.Save(new CombinedAppSettings
        {
            ToolPaths = new AppToolPathSettings
            {
                ManagedMkvToolNix = new ManagedToolSettings
                {
                    InstalledPath = managedDirectory,
                    InstalledVersion = "98.0"
                }
            }
        });

        var locator = new MkvToolNixLocator(new AppToolPathStore(settingsStore));

        Assert.Equal(Path.Combine(managedDirectory, "mkvmerge.exe"), locator.FindMkvMergePath());
        Assert.Equal(Path.Combine(managedDirectory, "mkvpropedit.exe"), locator.FindMkvPropEditPath());
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

    private string CreateFile(string relativePath, string content = "tool")
    {
        var path = Path.Combine(_tempDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}

