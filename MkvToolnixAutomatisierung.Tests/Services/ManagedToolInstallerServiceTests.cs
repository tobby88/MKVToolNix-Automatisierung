using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

[Collection("PortableStorage")]
public sealed class ManagedToolInstallerServiceTests
{
    private readonly PortableStorageFixture _storageFixture;

    public ManagedToolInstallerServiceTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_InstallsMissingManagedMkvToolNix()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var settings = toolPathStore.Load();
        settings.ManagedFfprobe.AutoManageEnabled = false;
        toolPathStore.Save(settings);

        var archiveBytes = "mkvtoolnix-archive"u8.ToArray();
        var archiveHash = Convert.ToHexString(SHA256.HashData(archiveBytes));
        var downloadUri = new Uri("https://example.invalid/mkvtoolnix.7z");
        var handler = new FakeHttpMessageHandler((downloadUri, archiveBytes));
        var service = new ManagedToolInstallerService(
            toolPathStore,
            [new StubPackageSource(new ManagedToolPackage(
                ManagedToolKind.MkvToolNix,
                "98.0",
                "98.0",
                downloadUri,
                "mkvtoolnix-64-bit-98.0.7z",
                archiveHash))],
            new StubArchiveExtractor((destinationDirectory) =>
            {
                var toolDirectory = Path.Combine(destinationDirectory, "mkvtoolnix");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(Path.Combine(toolDirectory, "mkvmerge.exe"), "tool");
                File.WriteAllText(Path.Combine(toolDirectory, "mkvpropedit.exe"), "tool");
            }),
            new HttpClient(handler));

        var result = await service.EnsureManagedToolsAsync();
        var savedSettings = toolPathStore.Load();

        Assert.False(result.HasWarning);
        Assert.Equal("98.0", savedSettings.ManagedMkvToolNix.InstalledVersion);
        Assert.True(Directory.Exists(savedSettings.ManagedMkvToolNix.InstalledPath));
        Assert.True(File.Exists(Path.Combine(savedSettings.ManagedMkvToolNix.InstalledPath, "mkvmerge.exe")));
        Assert.True(File.Exists(Path.Combine(savedSettings.ManagedMkvToolNix.InstalledPath, "mkvpropedit.exe")));
        Assert.Single(handler.RequestedUris);
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_InstallsFfprobeWithRealArchiveExtractor()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var settings = toolPathStore.Load();
        settings.ManagedMkvToolNix.AutoManageEnabled = false;
        toolPathStore.Save(settings);

        var archiveBytes = CreateFfprobeZipArchive();
        var archiveHash = Convert.ToHexString(SHA256.HashData(archiveBytes));
        var downloadUri = new Uri("https://example.invalid/ffprobe.zip");
        var handler = new FakeHttpMessageHandler((downloadUri, archiveBytes));
        var service = new ManagedToolInstallerService(
            toolPathStore,
            [new StubPackageSource(new ManagedToolPackage(
                ManagedToolKind.Ffprobe,
                "2026-04-22T10-00-00Z",
                "Latest Auto-Build",
                downloadUri,
                "ffmpeg-master-latest-win64-gpl-shared.zip",
                archiveHash))],
            new ManagedToolArchiveExtractor(),
            new HttpClient(handler));

        var result = await service.EnsureManagedToolsAsync();
        var savedSettings = toolPathStore.Load();

        Assert.False(result.HasWarning);
        Assert.Equal("2026-04-22T10-00-00Z", savedSettings.ManagedFfprobe.InstalledVersion);
        Assert.EndsWith("ffprobe.exe", savedSettings.ManagedFfprobe.InstalledPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("tool", File.ReadAllText(savedSettings.ManagedFfprobe.InstalledPath));
        Assert.Single(handler.RequestedUris);
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_SkipsDownloadWhenManagedFfprobeAlreadyMatchesLatestVersion()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var managedFfprobeDirectory = Path.Combine(
            PortableAppStorage.ToolsDirectory,
            "ffprobe",
            "2026-04-18T13-04-00Z",
            "bin");
        Directory.CreateDirectory(managedFfprobeDirectory);
        var managedFfprobePath = Path.Combine(managedFfprobeDirectory, "ffprobe.exe");
        File.WriteAllText(managedFfprobePath, "tool");

        var settings = toolPathStore.Load();
        settings.ManagedMkvToolNix.AutoManageEnabled = false;
        settings.ManagedFfprobe.AutoManageEnabled = true;
        settings.ManagedFfprobe.InstalledPath = managedFfprobePath;
        settings.ManagedFfprobe.InstalledVersion = "2026-04-18T13-04-00Z";
        toolPathStore.Save(settings);

        var handler = new FakeHttpMessageHandler();
        var service = new ManagedToolInstallerService(
            toolPathStore,
            [new StubPackageSource(new ManagedToolPackage(
                ManagedToolKind.Ffprobe,
                "2026-04-18T13-04-00Z",
                "Latest Auto-Build",
                new Uri("https://example.invalid/ffprobe.zip"),
                "ffmpeg-master-latest-win64-gpl-shared.zip",
                new string('a', 64)))],
            new StubArchiveExtractor(_ => throw new Xunit.Sdk.XunitException("Extractor should not run.")),
            new HttpClient(handler));

        var result = await service.EnsureManagedToolsAsync();
        var savedSettings = toolPathStore.Load();

        Assert.False(result.HasWarning);
        Assert.Equal(managedFfprobePath, savedSettings.ManagedFfprobe.InstalledPath);
        Assert.Empty(handler.RequestedUris);
        Assert.NotNull(savedSettings.ManagedFfprobe.LastCheckedUtc);
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_ReportsDownloadProgress()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var settings = toolPathStore.Load();
        settings.ManagedMkvToolNix.AutoManageEnabled = false;
        toolPathStore.Save(settings);

        var archiveBytes = new byte[256 * 1024];
        Random.Shared.NextBytes(archiveBytes);
        var archiveHash = Convert.ToHexString(SHA256.HashData(archiveBytes));
        var progressEvents = new List<ManagedToolStartupProgress>();
        var downloadUri = new Uri("https://example.invalid/ffprobe.zip");
        var handler = new FakeHttpMessageHandler((downloadUri, archiveBytes));
        var service = new ManagedToolInstallerService(
            toolPathStore,
            [new StubPackageSource(new ManagedToolPackage(
                ManagedToolKind.Ffprobe,
                "2026-04-22T10-00-00Z",
                "Latest Auto-Build",
                downloadUri,
                "ffmpeg-master-latest-win64-gpl-shared.zip",
                archiveHash))],
            new StubArchiveExtractor(destinationDirectory =>
            {
                var toolDirectory = Path.Combine(destinationDirectory, "ffprobe");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(Path.Combine(toolDirectory, "ffprobe.exe"), "tool");
            }),
            new HttpClient(handler));

        await service.EnsureManagedToolsAsync(new CollectingProgress(progressEvents));

        Assert.Contains(progressEvents, entry => entry.StatusText.Contains("ffprobe wird heruntergeladen", StringComparison.Ordinal));
        Assert.Contains(progressEvents, entry => !entry.IsIndeterminate && entry.ProgressPercent > 0d);
        Assert.True(progressEvents
            .Where(entry => !entry.IsIndeterminate && entry.ProgressPercent is not null)
            .Select(entry => entry.ProgressPercent!.Value)
            .Zip(progressEvents
                    .Where(entry => !entry.IsIndeterminate && entry.ProgressPercent is not null)
                    .Select(entry => entry.ProgressPercent!.Value)
                    .Skip(1),
                (previous, current) => current >= previous)
            .All(static isMonotone => isMonotone));
        Assert.Contains(progressEvents, entry => entry.StatusText.Contains("Werkzeuge bereit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_SkipsOnlineCheckWhenManagedToolWasCheckedRecently()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var managedFfprobeDirectory = Path.Combine(PortableAppStorage.ToolsDirectory, "ffprobe-existing");
        Directory.CreateDirectory(managedFfprobeDirectory);
        var managedFfprobePath = Path.Combine(managedFfprobeDirectory, "ffprobe.exe");
        File.WriteAllText(managedFfprobePath, "tool");

        var settings = toolPathStore.Load();
        settings.ManagedMkvToolNix.AutoManageEnabled = false;
        settings.ManagedFfprobe.AutoManageEnabled = true;
        settings.ManagedFfprobe.InstalledPath = managedFfprobePath;
        settings.ManagedFfprobe.InstalledVersion = "2026-04-18T13-04-00Z";
        settings.ManagedFfprobe.LastCheckedUtc = DateTimeOffset.UtcNow.AddHours(-1);
        toolPathStore.Save(settings);

        var packageSource = new RecordingPackageSource(new ManagedToolPackage(
            ManagedToolKind.Ffprobe,
            "2026-04-18T13-04-00Z",
            "Latest Auto-Build",
            new Uri("https://example.invalid/ffprobe.zip"),
            "ffmpeg-master-latest-win64-gpl-shared.zip",
            new string('a', 64)));
        var service = new ManagedToolInstallerService(
            toolPathStore,
            [packageSource],
            new StubArchiveExtractor(_ => throw new Xunit.Sdk.XunitException("Extractor should not run.")),
            new HttpClient(new FakeHttpMessageHandler()));

        var result = await service.EnsureManagedToolsAsync();

        Assert.False(result.HasWarning);
        Assert.Equal(0, packageSource.CallCount);
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_SkipsOnlineCheckWhenRecentFailureBackoffApplies()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var managedFfprobeDirectory = Path.Combine(PortableAppStorage.ToolsDirectory, "ffprobe-existing");
        Directory.CreateDirectory(managedFfprobeDirectory);
        var managedFfprobePath = Path.Combine(managedFfprobeDirectory, "ffprobe.exe");
        File.WriteAllText(managedFfprobePath, "tool");

        var settings = toolPathStore.Load();
        settings.ManagedMkvToolNix.AutoManageEnabled = false;
        settings.ManagedFfprobe.AutoManageEnabled = true;
        settings.ManagedFfprobe.InstalledPath = managedFfprobePath;
        settings.ManagedFfprobe.InstalledVersion = "2026-04-18T13-04-00Z";
        settings.ManagedFfprobe.LastFailedCheckUtc = DateTimeOffset.UtcNow.AddMinutes(-30);
        toolPathStore.Save(settings);

        var packageSource = new RecordingPackageSource(new ManagedToolPackage(
            ManagedToolKind.Ffprobe,
            "2026-04-22T10-00-00Z",
            "Latest Auto-Build",
            new Uri("https://example.invalid/ffprobe.zip"),
            "ffmpeg-master-latest-win64-gpl-shared.zip",
            new string('a', 64)));
        var service = new ManagedToolInstallerService(
            toolPathStore,
            [packageSource],
            new StubArchiveExtractor(_ => throw new Xunit.Sdk.XunitException("Extractor should not run.")),
            new HttpClient(new FakeHttpMessageHandler()));

        var result = await service.EnsureManagedToolsAsync();

        Assert.False(result.HasWarning);
        Assert.Equal(0, packageSource.CallCount);
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_PersistsFailedCheckBackoff_WhenExistingToolCanStillBeUsed()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var managedFfprobeDirectory = Path.Combine(PortableAppStorage.ToolsDirectory, "ffprobe", "old-version");
        Directory.CreateDirectory(managedFfprobeDirectory);
        var managedFfprobePath = Path.Combine(managedFfprobeDirectory, "ffprobe.exe");
        File.WriteAllText(managedFfprobePath, "tool");

        var settings = toolPathStore.Load();
        settings.ManagedMkvToolNix.AutoManageEnabled = false;
        settings.ManagedFfprobe.AutoManageEnabled = true;
        settings.ManagedFfprobe.InstalledPath = managedFfprobePath;
        settings.ManagedFfprobe.InstalledVersion = "old-version";
        toolPathStore.Save(settings);

        var service = new ManagedToolInstallerService(
            toolPathStore,
            [new ThrowingPackageSource(ManagedToolKind.Ffprobe, new InvalidOperationException("offline"))],
            new StubArchiveExtractor(_ => throw new Xunit.Sdk.XunitException("Extractor should not run.")),
            new HttpClient(new FakeHttpMessageHandler()));

        var result = await service.EnsureManagedToolsAsync();
        var savedSettings = toolPathStore.Load();

        Assert.False(result.HasWarning);
        Assert.NotNull(savedSettings.ManagedFfprobe.LastFailedCheckUtc);
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_PropagatesCancellationWithoutRecordingFailedBackoff()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var settings = toolPathStore.Load();
        settings.ManagedMkvToolNix.AutoManageEnabled = false;
        settings.ManagedFfprobe.AutoManageEnabled = true;
        toolPathStore.Save(settings);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var service = new ManagedToolInstallerService(
            toolPathStore,
            [new RecordingPackageSource(new ManagedToolPackage(
                ManagedToolKind.Ffprobe,
                "2026-04-22T10-00-00Z",
                "Latest Auto-Build",
                new Uri("https://example.invalid/ffprobe.zip"),
                "ffmpeg-master-latest-win64-gpl-shared.zip",
                new string('a', 64)))],
            new StubArchiveExtractor(_ => throw new Xunit.Sdk.XunitException("Extractor should not run.")),
            new HttpClient(new FakeHttpMessageHandler()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.EnsureManagedToolsAsync(cancellationToken: cancellationSource.Token));

        Assert.Null(toolPathStore.Load().ManagedFfprobe.LastFailedCheckUtc);
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_SkipsManagedInstallWhenManualOverrideExists()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var manualDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-manual-ffprobe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(manualDirectory);
        var manualFfprobePath = Path.Combine(manualDirectory, "ffprobe.exe");
        File.WriteAllText(manualFfprobePath, "tool");

        try
        {
            var settings = toolPathStore.Load();
            settings.ManagedMkvToolNix.AutoManageEnabled = false;
            settings.ManagedFfprobe.AutoManageEnabled = true;
            settings.FfprobePath = manualFfprobePath;
            toolPathStore.Save(settings);

            var packageSource = new RecordingPackageSource(new ManagedToolPackage(
                ManagedToolKind.Ffprobe,
                "2026-04-18T13-04-00Z",
                "Latest Auto-Build",
                new Uri("https://example.invalid/ffprobe.zip"),
                "ffmpeg-master-latest-win64-gpl-shared.zip",
                new string('a', 64)));
            var service = new ManagedToolInstallerService(
                toolPathStore,
                [packageSource],
                new StubArchiveExtractor(_ => throw new Xunit.Sdk.XunitException("Extractor should not run.")),
                new HttpClient(new FakeHttpMessageHandler()));

            var result = await service.EnsureManagedToolsAsync();

            Assert.False(result.HasWarning);
            Assert.Equal(0, packageSource.CallCount);
        }
        finally
        {
            if (Directory.Exists(manualDirectory))
            {
                Directory.Delete(manualDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_DoesNotTreatLegacyDownloadOverrideAsManualOverride()
    {
        var userProfileDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-legacy-download-override", Guid.NewGuid().ToString("N"));
        var downloadsDirectory = Path.Combine(userProfileDirectory, "Downloads");
        Directory.CreateDirectory(downloadsDirectory);
        var legacyFfprobePath = Path.Combine(downloadsDirectory, "ffmpeg", "bin", "ffprobe.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyFfprobePath)!);
        File.WriteAllText(legacyFfprobePath, "legacy-tool");

        var originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var originalHome = Environment.GetEnvironmentVariable("HOME");

        try
        {
            Environment.SetEnvironmentVariable("USERPROFILE", userProfileDirectory);
            Environment.SetEnvironmentVariable("HOME", userProfileDirectory);

            var settingsStore = new AppSettingsStore();
            var toolPathStore = new AppToolPathStore(settingsStore);
            var settings = toolPathStore.Load();
            settings.ManagedMkvToolNix.AutoManageEnabled = false;
            settings.ManagedFfprobe.AutoManageEnabled = true;
            settings.FfprobePath = legacyFfprobePath;
            toolPathStore.Save(settings);

            var archiveBytes = "ffprobe-archive"u8.ToArray();
            var archiveHash = Convert.ToHexString(SHA256.HashData(archiveBytes));
            var downloadUri = new Uri("https://example.invalid/ffprobe.zip");
            var packageSource = new RecordingPackageSource(new ManagedToolPackage(
                ManagedToolKind.Ffprobe,
                "2026-04-22T10-00-00Z",
                "Latest Auto-Build",
                downloadUri,
                "ffmpeg-master-latest-win64-gpl-shared.zip",
                archiveHash));
            var service = new ManagedToolInstallerService(
                toolPathStore,
                [packageSource],
                new StubArchiveExtractor(destinationDirectory =>
                {
                    var toolDirectory = Path.Combine(destinationDirectory, "ffmpeg-master-latest-win64-gpl-shared", "bin");
                    Directory.CreateDirectory(toolDirectory);
                    File.WriteAllText(Path.Combine(toolDirectory, "ffprobe.exe"), "tool");
                }),
                new HttpClient(new FakeHttpMessageHandler((downloadUri, archiveBytes))));

            var result = await service.EnsureManagedToolsAsync();
            var savedSettings = toolPathStore.Load();

            Assert.False(result.HasWarning);
            Assert.Equal(1, packageSource.CallCount);
            Assert.NotEqual(legacyFfprobePath, savedSettings.ManagedFfprobe.InstalledPath);
            Assert.NotEmpty(savedSettings.ManagedFfprobe.InstalledPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile);
            Environment.SetEnvironmentVariable("HOME", originalHome);
            if (Directory.Exists(userProfileDirectory))
            {
                Directory.Delete(userProfileDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_ReinstallsWhenInstalledPathDoesNotMatchVersionDirectory()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var externalDirectory = Path.Combine(PortableAppStorage.ToolsDirectory, "ffprobe", "external-copy");
        Directory.CreateDirectory(externalDirectory);
        var externalFfprobePath = Path.Combine(externalDirectory, "ffprobe.exe");
        File.WriteAllText(externalFfprobePath, "stale-tool");

        var settings = toolPathStore.Load();
        settings.ManagedMkvToolNix.AutoManageEnabled = false;
        settings.ManagedFfprobe.AutoManageEnabled = true;
        settings.ManagedFfprobe.InstalledPath = externalFfprobePath;
        settings.ManagedFfprobe.InstalledVersion = "2026-04-22T10-00-00Z";
        toolPathStore.Save(settings);

        var archiveBytes = "ffprobe-archive"u8.ToArray();
        var archiveHash = Convert.ToHexString(SHA256.HashData(archiveBytes));
        var downloadUri = new Uri("https://example.invalid/ffprobe.zip");
        var handler = new FakeHttpMessageHandler((downloadUri, archiveBytes));
        var service = new ManagedToolInstallerService(
            toolPathStore,
            [new StubPackageSource(new ManagedToolPackage(
                ManagedToolKind.Ffprobe,
                "2026-04-22T10-00-00Z",
                "Latest Auto-Build",
                downloadUri,
                "ffmpeg-master-latest-win64-gpl-shared.zip",
                archiveHash))],
            new StubArchiveExtractor(destinationDirectory =>
            {
                var toolDirectory = Path.Combine(destinationDirectory, "ffmpeg", "bin");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(Path.Combine(toolDirectory, "ffprobe.exe"), "fresh-tool");
            }),
            new HttpClient(handler));

        var result = await service.EnsureManagedToolsAsync();
        var savedSettings = toolPathStore.Load();

        Assert.False(result.HasWarning);
        Assert.Single(handler.RequestedUris);
        Assert.NotEqual(externalFfprobePath, savedSettings.ManagedFfprobe.InstalledPath);
        Assert.True(PathComparisonHelper.IsPathWithinRoot(
            savedSettings.ManagedFfprobe.InstalledPath,
            Path.Combine(PortableAppStorage.ToolsDirectory, "ffprobe", "2026-04-22T10-00-00Z")));
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_DoesNotTouchToolsDirectoryWhenAutoManageIsDisabled()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var settings = toolPathStore.Load();
        settings.ManagedMkvToolNix.AutoManageEnabled = false;
        settings.ManagedFfprobe.AutoManageEnabled = false;
        toolPathStore.Save(settings);

        if (Directory.Exists(PortableAppStorage.ToolsDirectory))
        {
            Directory.Delete(PortableAppStorage.ToolsDirectory, recursive: true);
        }

        File.WriteAllText(PortableAppStorage.ToolsDirectory, "occupied");

        try
        {
            var service = new ManagedToolInstallerService(
                toolPathStore,
                [],
                new StubArchiveExtractor(_ => throw new Xunit.Sdk.XunitException("Extractor should not run.")),
                new HttpClient(new FakeHttpMessageHandler()));

            var result = await service.EnsureManagedToolsAsync();

            Assert.False(result.HasWarning);
            Assert.Equal(string.Empty, toolPathStore.Load().ManagedFfprobe.InstalledPath);
            Assert.Equal(string.Empty, toolPathStore.Load().ManagedMkvToolNix.InstalledPath);
        }
        finally
        {
            if (File.Exists(PortableAppStorage.ToolsDirectory))
            {
                File.Delete(PortableAppStorage.ToolsDirectory);
            }
        }
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_ReturnsResultWhenToolsDirectoryCannotBeCreated()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var settings = toolPathStore.Load();
        settings.ManagedMkvToolNix.AutoManageEnabled = false;
        settings.ManagedFfprobe.AutoManageEnabled = true;
        toolPathStore.Save(settings);

        var archiveBytes = "ffprobe-archive"u8.ToArray();
        var archiveHash = Convert.ToHexString(SHA256.HashData(archiveBytes));
        var downloadUri = new Uri("https://example.invalid/ffprobe.zip");

        if (Directory.Exists(PortableAppStorage.ToolsDirectory))
        {
            Directory.Delete(PortableAppStorage.ToolsDirectory, recursive: true);
        }

        File.WriteAllText(PortableAppStorage.ToolsDirectory, "occupied");

        try
        {
            var handler = new FakeHttpMessageHandler((downloadUri, archiveBytes));
            var service = new ManagedToolInstallerService(
                toolPathStore,
                [new StubPackageSource(new ManagedToolPackage(
                    ManagedToolKind.Ffprobe,
                    "2026-04-22T10-00-00Z",
                    "Latest Auto-Build",
                    downloadUri,
                    "ffmpeg-master-latest-win64-gpl-shared.zip",
                    archiveHash))],
                new StubArchiveExtractor(_ => throw new Xunit.Sdk.XunitException("Extractor should not run.")),
                new HttpClient(handler));

            var result = await service.EnsureManagedToolsAsync();

            Assert.Empty(handler.RequestedUris);
            Assert.Equal(string.Empty, toolPathStore.Load().ManagedFfprobe.InstalledPath);
            Assert.True(!result.HasWarning || result.WarningMessage!.Contains("ffprobe", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(PortableAppStorage.ToolsDirectory))
            {
                File.Delete(PortableAppStorage.ToolsDirectory);
            }
        }
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_ReturnsWarningInsteadOfThrowingWhenToolStateSaveFails()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var settings = toolPathStore.Load();
        settings.ManagedFfprobe.AutoManageEnabled = false;
        toolPathStore.Save(settings);

        Directory.CreateDirectory(PortableAppStorage.SettingsBackupFilePath);

        var archiveBytes = "mkvtoolnix-archive"u8.ToArray();
        var archiveHash = Convert.ToHexString(SHA256.HashData(archiveBytes));
        var downloadUri = new Uri("https://example.invalid/mkvtoolnix.7z");
        var service = new ManagedToolInstallerService(
            toolPathStore,
            [new StubPackageSource(new ManagedToolPackage(
                ManagedToolKind.MkvToolNix,
                "98.0",
                "98.0",
                downloadUri,
                "mkvtoolnix-64-bit-98.0.7z",
                archiveHash))],
            new StubArchiveExtractor(destinationDirectory =>
            {
                var toolDirectory = Path.Combine(destinationDirectory, "mkvtoolnix");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(Path.Combine(toolDirectory, "mkvmerge.exe"), "tool");
                File.WriteAllText(Path.Combine(toolDirectory, "mkvpropedit.exe"), "tool");
            }),
            new HttpClient(new FakeHttpMessageHandler((downloadUri, archiveBytes))));

        var result = await service.EnsureManagedToolsAsync();
        var locator = new MkvToolNixLocator(toolPathStore);

        Assert.True(result.HasWarning);
        Assert.Contains(result.Warnings, warning => warning.Contains("Werkzeugzustände", StringComparison.Ordinal));
        Assert.EndsWith("mkvmerge.exe", locator.FindMkvMergePath(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, toolPathStore.Load().ManagedMkvToolNix.InstalledVersion);
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_RejectsPackagesWithoutChecksum()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var settings = toolPathStore.Load();
        settings.ManagedMkvToolNix.AutoManageEnabled = false;
        toolPathStore.Save(settings);

        var archiveBytes = "ffprobe-archive"u8.ToArray();
        var downloadUri = new Uri("https://example.invalid/ffprobe.zip");
        var handler = new FakeHttpMessageHandler((downloadUri, archiveBytes));
        var service = new ManagedToolInstallerService(
            toolPathStore,
            [new StubPackageSource(new ManagedToolPackage(
                ManagedToolKind.Ffprobe,
                "2026-04-22T10-00-00Z",
                "Latest Auto-Build",
                downloadUri,
                "ffmpeg-master-latest-win64-gpl-shared.zip"))],
            new StubArchiveExtractor(destinationDirectory =>
            {
                var toolDirectory = Path.Combine(destinationDirectory, "ffprobe");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(Path.Combine(toolDirectory, "ffprobe.exe"), "tool");
            }),
            new HttpClient(handler));

        var result = await service.EnsureManagedToolsAsync();

        Assert.Empty(handler.RequestedUris);
        Assert.Equal(string.Empty, toolPathStore.Load().ManagedFfprobe.InstalledPath);
        Assert.True(!result.HasWarning || result.WarningMessage!.Contains("SHA-256-Prüfsumme", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_RejectsPackagesWithMismatchedChecksum()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var settings = toolPathStore.Load();
        settings.ManagedMkvToolNix.AutoManageEnabled = false;
        toolPathStore.Save(settings);

        var archiveBytes = "ffprobe-archive"u8.ToArray();
        var downloadUri = new Uri("https://example.invalid/ffprobe.zip");
        var handler = new FakeHttpMessageHandler((downloadUri, archiveBytes));
        var service = new ManagedToolInstallerService(
            toolPathStore,
            [new StubPackageSource(new ManagedToolPackage(
                ManagedToolKind.Ffprobe,
                "2026-04-22T10-00-00Z",
                "Latest Auto-Build",
                downloadUri,
                "ffmpeg-master-latest-win64-gpl-shared.zip",
                new string('b', 64)))],
            new StubArchiveExtractor(_ => throw new Xunit.Sdk.XunitException("Extractor should not run.")),
            new HttpClient(handler));

        var result = await service.EnsureManagedToolsAsync();

        Assert.Single(handler.RequestedUris);
        Assert.Equal(string.Empty, toolPathStore.Load().ManagedFfprobe.InstalledPath);
        Assert.True(!result.HasWarning || result.WarningMessage!.Contains("Prüfsumme", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_PreservesExistingVersionDirectory_WhenReplacementDownloadFails()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var versionDirectory = Path.Combine(PortableAppStorage.ToolsDirectory, "ffprobe", "2026-04-22");
        var existingFfprobePath = Path.Combine(versionDirectory, "ffprobe.exe");
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(existingFfprobePath, "existing-tool");

        var settings = toolPathStore.Load();
        settings.ManagedMkvToolNix.AutoManageEnabled = false;
        settings.ManagedFfprobe.AutoManageEnabled = true;
        settings.ManagedFfprobe.InstalledPath = existingFfprobePath;
        settings.ManagedFfprobe.InstalledVersion = "stale-version-token";
        toolPathStore.Save(settings);

        var archiveBytes = "corrupt-download"u8.ToArray();
        var downloadUri = new Uri("https://example.invalid/ffprobe.zip");
        var service = new ManagedToolInstallerService(
            toolPathStore,
            [new StubPackageSource(new ManagedToolPackage(
                ManagedToolKind.Ffprobe,
                "2026-04-22",
                "Latest Auto-Build",
                downloadUri,
                "ffmpeg-master-latest-win64-gpl-shared.zip",
                new string('b', 64)))],
            new StubArchiveExtractor(_ => throw new Xunit.Sdk.XunitException("Extractor should not run after checksum failure.")),
            new HttpClient(new FakeHttpMessageHandler((downloadUri, archiveBytes))));

        var result = await service.EnsureManagedToolsAsync();
        var savedSettings = toolPathStore.Load();

        Assert.False(result.HasWarning);
        Assert.True(File.Exists(existingFfprobePath));
        Assert.Equal("existing-tool", File.ReadAllText(existingFfprobePath));
        Assert.Equal(existingFfprobePath, savedSettings.ManagedFfprobe.InstalledPath);
        Assert.Equal("stale-version-token", savedSettings.ManagedFfprobe.InstalledVersion);
    }

    [Fact]
    public async Task EnsureManagedToolsAsync_ReportsMonotonicOverallProgressAcrossBothTools()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var progressEvents = new List<ManagedToolStartupProgress>();

        var mkvBytes = "mkvtoolnix-archive"u8.ToArray();
        var ffprobeBytes = "ffprobe-archive"u8.ToArray();
        var mkvUri = new Uri("https://example.invalid/mkvtoolnix.7z");
        var ffprobeUri = new Uri("https://example.invalid/ffprobe.zip");
        var service = new ManagedToolInstallerService(
            toolPathStore,
            [
                new StubPackageSource(new ManagedToolPackage(
                    ManagedToolKind.MkvToolNix,
                    "98.0",
                    "98.0",
                    mkvUri,
                    "mkvtoolnix-64-bit-98.0.7z",
                    Convert.ToHexString(SHA256.HashData(mkvBytes)))),
                new StubPackageSource(new ManagedToolPackage(
                    ManagedToolKind.Ffprobe,
                    "2026-04-22T10-00-00Z",
                    "Latest Auto-Build",
                    ffprobeUri,
                    "ffmpeg-master-latest-win64-gpl-shared.zip",
                    Convert.ToHexString(SHA256.HashData(ffprobeBytes))))
            ],
            new StubArchiveExtractor(destinationDirectory =>
            {
                if (destinationDirectory.Contains("mkvtoolnix", StringComparison.OrdinalIgnoreCase))
                {
                    var toolDirectory = Path.Combine(destinationDirectory, "mkvtoolnix");
                    Directory.CreateDirectory(toolDirectory);
                    File.WriteAllText(Path.Combine(toolDirectory, "mkvmerge.exe"), "tool");
                    File.WriteAllText(Path.Combine(toolDirectory, "mkvpropedit.exe"), "tool");
                }
                else
                {
                    var toolDirectory = Path.Combine(destinationDirectory, "ffprobe");
                    Directory.CreateDirectory(toolDirectory);
                    File.WriteAllText(Path.Combine(toolDirectory, "ffprobe.exe"), "tool");
                }
            }),
            new HttpClient(new FakeHttpMessageHandler((mkvUri, mkvBytes), (ffprobeUri, ffprobeBytes))));

        await service.EnsureManagedToolsAsync(new CollectingProgress(progressEvents));

        var determinateProgress = progressEvents
            .Where(entry => !entry.IsIndeterminate && entry.ProgressPercent is not null)
            .Select(entry => entry.ProgressPercent!.Value)
            .ToArray();

        Assert.NotEmpty(determinateProgress);
        Assert.True(determinateProgress.Zip(determinateProgress.Skip(1), (previous, current) => current >= previous).All(static isMonotone => isMonotone));
        Assert.Equal(100d, determinateProgress[^1], 3);
    }

    private sealed class StubPackageSource(ManagedToolPackage package) : IManagedToolPackageSource
    {
        public ManagedToolKind Kind => package.Kind;

        public Task<ManagedToolPackage> GetLatestPackageAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(package);
        }
    }

    private sealed class RecordingPackageSource(ManagedToolPackage package) : IManagedToolPackageSource
    {
        public int CallCount { get; private set; }

        public ManagedToolKind Kind => package.Kind;

        public Task<ManagedToolPackage> GetLatestPackageAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(package);
        }
    }

    private sealed class ThrowingPackageSource(ManagedToolKind kind, Exception exception) : IManagedToolPackageSource
    {
        public ManagedToolKind Kind => kind;

        public Task<ManagedToolPackage> GetLatestPackageAsync(CancellationToken cancellationToken = default)
        {
            throw exception;
        }
    }

    private sealed class StubArchiveExtractor(Action<string> onExtract) : IManagedToolArchiveExtractor
    {
        public Task ExtractArchiveAsync(
            string archivePath,
            string destinationDirectory,
            IProgress<ManagedToolExtractionProgress>? progress = null,
            ManagedToolKind? toolKind = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destinationDirectory);
            progress?.Report(new ManagedToolExtractionProgress(0, 1, "start", 0, 4));
            onExtract(destinationDirectory);
            progress?.Report(new ManagedToolExtractionProgress(1, 1, "done", 4, 4));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHttpMessageHandler(params (Uri Uri, byte[] Content)[] responses) : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _responses = responses.ToDictionary(
            response => response.Uri.ToString(),
            response => response.Content,
            StringComparer.OrdinalIgnoreCase);

        public List<Uri> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
            }

            RequestedUris.Add(request.RequestUri!);
            if (_responses.TryGetValue(request.RequestUri!.ToString(), out var content))
            {
                var byteContent = new ByteArrayContent(content);
                byteContent.Headers.ContentLength = content.Length;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = byteContent
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class CollectingProgress(List<ManagedToolStartupProgress> events) : IProgress<ManagedToolStartupProgress>
    {
        public void Report(ManagedToolStartupProgress value)
        {
            events.Add(value);
        }
    }

    private static byte[] CreateFfprobeZipArchive()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "ffmpeg-master-latest-win64-gpl-shared/bin/ffprobe.exe", "tool");
            WriteZipEntry(archive, "ffmpeg-master-latest-win64-gpl-shared/bin/avcodec-61.dll", "dependency");
            WriteZipEntry(archive, "ffmpeg-master-latest-win64-gpl-shared/doc/readme.txt", "docs");
        }

        return stream.ToArray();
    }

    private static void WriteZipEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
