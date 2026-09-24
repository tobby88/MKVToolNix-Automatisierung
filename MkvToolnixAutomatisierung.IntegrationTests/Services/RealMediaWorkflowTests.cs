using System.Diagnostics;
using System.IO;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Services;

/// <summary>
/// Opt-in verification of the real binaries. Synthetic media and settings stay in GUID temp roots;
/// no download, live archive, provider or Emby server is involved.
/// </summary>
[Collection("PortableStorage")]
public sealed class RealMediaWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mkv-real-media-" + Guid.NewGuid().ToString("N"));
    public RealMediaWorkflowTests(PortableStorageFixture storage)
    {
        storage.Reset();
        Directory.CreateDirectory(_root);
    }

    [RealMediaFact]
    public async Task SharedMux_HeaderEdit_AndSeasonRename_PreserveTracksAndSidecars()
    {
        var tools = new RealTools();
        var source = Path.Combine(_root, "Review - Pilot (S01_E01).mp4");
        await RunAsync(Environment.GetEnvironmentVariable("MKV_TEST_FFMPEG")!,
            ["-nostdin", "-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=320x180:r=25",
             "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "2", "-c:v", "libx264",
             "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-metadata:s:a:0", "language=deu", source]);
        var subtitle = Path.ChangeExtension(source, ".srt");
        File.WriteAllText(subtitle, "1\n00:00:00,000 --> 00:00:01,000\nSynthetic review fixture\n");
        var attachment = Path.ChangeExtension(source, ".txt");
        File.WriteAllText(attachment, "Sender: ZDF\nThema: Review\nTitel: Pilot (S01_E01)\nDauer: 00:00:02");
        var archiveRoot = Path.Combine(_root, "archive");
        Directory.CreateDirectory(archiveRoot);
        var probe = new MkvMergeProbeService();
        var duration = new FfprobeDurationProbe(tools);
        var settings = new AppSettingsStore();
        new AppToolPathStore(settings).Save(new AppToolPathSettings
        {
            MkvToolNixDirectoryPath = Environment.GetEnvironmentVariable("MKV_TEST_TOOLNIX")!,
            MkvToolNixPathExplicitlySelected = true
        });
        var archive = new SeriesArchiveService(probe, new AppArchiveSettingsStore(settings), duration);
        archive.ConfigureArchiveRootDirectory(archiveRoot);
        var mux = new SeriesEpisodeMuxService(new SeriesEpisodeMuxPlanner(new MkvToolNixLocator(new AppToolPathStore(settings)), probe, archive, duration, duration),
            new MuxExecutionService(), new MkvMergeOutputParser());
        var destination = Path.Combine(archiveRoot, "Review", "Season 1", "Review - S01E01 - Pilot.mkv");
        var plan = await mux.CreatePlanAsync(new(source, null, [subtitle], [attachment], destination, "Pilot"));
        var coordinator = new MuxWorkflowCoordinator(mux, new FileCopyService(), new EpisodeCleanupService());
        var result = await coordinator.ExecuteMuxAsync(plan);
        Assert.InRange(result.ExitCode, 0, 1);
        var before = await probe.ReadContainerMetadataAsync(tools.FindMkvMergePath(), destination);
        Assert.Equal("Pilot", before.Title);
        Assert.Equal(3, before.Tracks.Count);
        Assert.Single(before.Attachments);
        Assert.InRange(duration.TryReadDuration(destination)!.Value.TotalSeconds, 1.9, 2.2);

        var nfo = Path.ChangeExtension(destination, ".nfo");
        var thumb = Path.Combine(Path.GetDirectoryName(destination)!, Path.GetFileNameWithoutExtension(destination) + "-thumb.jpg");
        File.WriteAllText(nfo, "<episodedetails><title>Pilot</title><uniqueid type=\"tvdb\">7</uniqueid></episodedetails>");
        File.WriteAllBytes(thumb, [1, 2, 3, 4]);
        var target = Path.Combine(archiveRoot, "Review", "Season 2", "Review - S02E01 - Corrected.mkv");
        var targetNfo = Path.ChangeExtension(target, ".nfo");
        var targetThumb = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target) + "-thumb.jpg");
        var maintenance = new ArchiveMaintenanceService(probe, tools, new MuxExecutionService(), nfoProviderIds: new EmbyNfoProviderIdService());
        var edited = await maintenance.ApplyAsync(new(destination,
            new(destination, target, [new(nfo, targetNfo), new(thumb, targetThumb)]),
            new("Pilot", "Corrected"),
            [new("track:a1", "Audio", before.Tracks.Single(t => t.Type == "audio").TrackName, "Review audio")],
            new(new("8", "tt1234567"), false),
            new("Pilot", "Corrected", null, null, false, true, false, false)));
        Assert.True(edited.Success, edited.Message);
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(nfo));
        Assert.False(File.Exists(thumb));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(targetThumb));
        var after = await probe.ReadContainerMetadataAsync(tools.FindMkvMergePath(), target);
        Assert.Equal("Corrected", after.Title);
        Assert.Equal("Review audio", after.Tracks.Single(t => t.Type == "audio").TrackName);
        Assert.Equal(before.Tracks.Count, after.Tracks.Count);
        Assert.Equal(before.Attachments.Count, after.Attachments.Count);
        var metadata = new EmbyNfoProviderIdService().ReadEpisodeMetadata(target);
        Assert.Equal("8", metadata.ProviderIds.TvdbId);
        Assert.Equal("tt1234567", metadata.ProviderIds.ImdbId);
        Assert.Equal("Corrected", metadata.Title);
        Assert.True(metadata.IsTitleLocked);
        Assert.Empty(Directory.EnumerateDirectories(archiveRoot, ".mux-*", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(archiveRoot, ".archive-change-*", SearchOption.AllDirectories));
    }

    private static async Task RunAsync(string executable, string[] arguments)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var process = new Process { StartInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class RealTools : IMkvToolNixLocator, IFfprobeLocator
    {
        public string FindMkvMergePath() => Path.Combine(Environment.GetEnvironmentVariable("MKV_TEST_TOOLNIX")!, "mkvmerge.exe");
        public string FindMkvPropEditPath() => Path.Combine(Environment.GetEnvironmentVariable("MKV_TEST_TOOLNIX")!, "mkvpropedit.exe");
        public string? TryFindFfprobePath() => Environment.GetEnvironmentVariable("MKV_TEST_FFPROBE");
    }
}

internal sealed class RealMediaFactAttribute : FactAttribute
{
    public RealMediaFactAttribute()
    {
        if (new[] { "MKV_TEST_TOOLNIX", "MKV_TEST_FFMPEG", "MKV_TEST_FFPROBE" }.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Set MKV_TEST_TOOLNIX, MKV_TEST_FFMPEG and MKV_TEST_FFPROBE to run isolated real-media tests.";
    }
}
