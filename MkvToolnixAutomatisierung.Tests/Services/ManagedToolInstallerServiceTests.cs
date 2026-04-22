using System.IO;
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
    public async Task EnsureManagedToolsAsync_SkipsDownloadWhenManagedFfprobeAlreadyMatchesLatestVersion()
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

    private sealed class StubPackageSource(ManagedToolPackage package) : IManagedToolPackageSource
    {
        public ManagedToolKind Kind => package.Kind;

        public Task<ManagedToolPackage> GetLatestPackageAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(package);
        }
    }

    private sealed class StubArchiveExtractor(Action<string> onExtract) : IManagedToolArchiveExtractor
    {
        public void ExtractArchive(string archivePath, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            onExtract(destinationDirectory);
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
            RequestedUris.Add(request.RequestUri!);
            if (_responses.TryGetValue(request.RequestUri!.ToString(), out var content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
