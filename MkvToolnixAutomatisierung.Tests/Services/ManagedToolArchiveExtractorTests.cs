using System.IO;
using System.IO.Compression;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class ManagedToolArchiveExtractorTests : IDisposable
{
    private readonly string _tempDirectory;

    public ManagedToolArchiveExtractorTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "mkv-auto-managed-tool-archive-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void ExtractArchive_ExtractsZipEntriesAndReportsProgress()
    {
        var sourceDirectory = CreateDirectory("source");
        File.WriteAllText(Path.Combine(sourceDirectory, "ffprobe.exe"), "tool");
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "nested"));
        File.WriteAllText(Path.Combine(sourceDirectory, "nested", "readme.txt"), "info");

        var archivePath = Path.Combine(_tempDirectory, "ffprobe.zip");
        ZipFile.CreateFromDirectory(sourceDirectory, archivePath);

        var destinationDirectory = Path.Combine(_tempDirectory, "extracted");
        var progressEvents = new List<ManagedToolExtractionProgress>();
        var extractor = new ManagedToolArchiveExtractor();

        extractor.ExtractArchive(archivePath, destinationDirectory, new CollectingProgress(progressEvents));

        Assert.True(File.Exists(Path.Combine(destinationDirectory, "ffprobe.exe")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "nested", "readme.txt")));
        Assert.NotEmpty(progressEvents);
        Assert.Equal(0, progressEvents[0].ExtractedEntryCount);
        Assert.Equal(0, progressEvents[0].ExtractedByteCount);
        Assert.NotNull(progressEvents[0].TotalByteCount);
        Assert.Equal(progressEvents[^1].TotalEntryCount, progressEvents[^1].ExtractedEntryCount);
        Assert.Equal(progressEvents[^1].TotalByteCount, progressEvents[^1].ExtractedByteCount);
        Assert.Contains(progressEvents, entry => string.Equals(entry.CurrentEntryPath, "ffprobe.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractArchive_ForMkvToolNix_SkipsDocumentationAndLocalePayload()
    {
        var sourceDirectory = CreateDirectory("source");
        var toolDirectory = Path.Combine(sourceDirectory, "mkvtoolnix");
        Directory.CreateDirectory(toolDirectory);
        File.WriteAllText(Path.Combine(toolDirectory, "mkvmerge.exe"), "tool");
        File.WriteAllText(Path.Combine(toolDirectory, "mkvpropedit.exe"), "tool");
        File.WriteAllText(Path.Combine(toolDirectory, "mkvextract.exe"), "tool");
        File.WriteAllText(Path.Combine(toolDirectory, "libebml.dll"), "dependency");
        Directory.CreateDirectory(Path.Combine(toolDirectory, "doc"));
        File.WriteAllText(Path.Combine(toolDirectory, "doc", "guide.txt"), "docs");
        Directory.CreateDirectory(Path.Combine(toolDirectory, "locale"));
        File.WriteAllText(Path.Combine(toolDirectory, "locale", "de.mo"), "translations");

        var archivePath = Path.Combine(_tempDirectory, "mkvtoolnix.zip");
        ZipFile.CreateFromDirectory(sourceDirectory, archivePath);

        var destinationDirectory = Path.Combine(_tempDirectory, "mkvtoolnix-extracted");
        var extractor = new ManagedToolArchiveExtractor();

        extractor.ExtractArchive(archivePath, destinationDirectory, toolKind: ManagedToolKind.MkvToolNix);

        Assert.True(File.Exists(Path.Combine(destinationDirectory, "mkvtoolnix", "mkvmerge.exe")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "mkvtoolnix", "mkvpropedit.exe")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "mkvtoolnix", "mkvextract.exe")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "mkvtoolnix", "libebml.dll")));
        Assert.False(File.Exists(Path.Combine(destinationDirectory, "mkvtoolnix", "doc", "guide.txt")));
        Assert.False(File.Exists(Path.Combine(destinationDirectory, "mkvtoolnix", "locale", "de.mo")));
    }

    [Fact]
    public void ExtractArchive_ReadsSevenZipArchives()
    {
        var archivePath = Path.Combine(_tempDirectory, "fixture.7z");
        WriteEmbeddedSevenZipFixture(archivePath);

        var destinationDirectory = Path.Combine(_tempDirectory, "mkvtoolnix-7z-extracted");
        var extractor = new ManagedToolArchiveExtractor();

        extractor.ExtractArchive(archivePath, destinationDirectory);

        Assert.True(File.Exists(Path.Combine(destinationDirectory, "7Zip.Tar.tar")));
    }

    [Fact]
    public void ExtractArchive_ForFfprobe_SkipsDocumentationAndPreservesBinPayload()
    {
        var sourceDirectory = CreateDirectory("source");
        var archiveRoot = Path.Combine(sourceDirectory, "ffmpeg-master-latest-win64-gpl-shared");
        var binDirectory = Path.Combine(archiveRoot, "bin");
        Directory.CreateDirectory(binDirectory);
        File.WriteAllText(Path.Combine(binDirectory, "ffprobe.exe"), "tool");
        File.WriteAllText(Path.Combine(binDirectory, "avcodec-61.dll"), "dependency");
        File.WriteAllText(Path.Combine(binDirectory, "ffmpeg.exe"), "other-tool");
        Directory.CreateDirectory(Path.Combine(archiveRoot, "doc"));
        File.WriteAllText(Path.Combine(archiveRoot, "doc", "readme.txt"), "docs");
        Directory.CreateDirectory(Path.Combine(archiveRoot, "presets"));
        File.WriteAllText(Path.Combine(archiveRoot, "presets", "libx264.ffpreset"), "preset");

        var archivePath = Path.Combine(_tempDirectory, "ffprobe.zip");
        ZipFile.CreateFromDirectory(sourceDirectory, archivePath);

        var destinationDirectory = Path.Combine(_tempDirectory, "ffprobe-extracted");
        var extractor = new ManagedToolArchiveExtractor();

        extractor.ExtractArchive(archivePath, destinationDirectory, toolKind: ManagedToolKind.Ffprobe);

        Assert.True(File.Exists(Path.Combine(destinationDirectory, "ffmpeg-master-latest-win64-gpl-shared", "bin", "ffprobe.exe")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "ffmpeg-master-latest-win64-gpl-shared", "bin", "avcodec-61.dll")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "ffmpeg-master-latest-win64-gpl-shared", "bin", "ffmpeg.exe")));
        Assert.False(File.Exists(Path.Combine(destinationDirectory, "ffmpeg-master-latest-win64-gpl-shared", "doc", "readme.txt")));
        Assert.False(File.Exists(Path.Combine(destinationDirectory, "ffmpeg-master-latest-win64-gpl-shared", "presets", "libx264.ffpreset")));
    }

    [Fact]
    public void ExtractArchive_ThrowsWhenArchiveIsCorrupt()
    {
        var archivePath = Path.Combine(_tempDirectory, "broken.zip");
        File.WriteAllText(archivePath, "not-a-real-archive");
        var destinationDirectory = Path.Combine(_tempDirectory, "broken-extracted");
        var extractor = new ManagedToolArchiveExtractor();

        var exception = Record.Exception(() => extractor.ExtractArchive(archivePath, destinationDirectory));

        Assert.NotNull(exception);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destinationDirectory));
    }

    [Fact]
    public void ExtractArchive_RejectsEntriesOutsideDestinationDirectory()
    {
        var archivePath = Path.Combine(_tempDirectory, "zip-slip.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../evil.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("outside");
        }

        var destinationDirectory = Path.Combine(_tempDirectory, "zip-slip-extracted");
        var extractor = new ManagedToolArchiveExtractor();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            extractor.ExtractArchive(archivePath, destinationDirectory));

        Assert.Contains("unsicheren relativen Pfad", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_tempDirectory, "evil.txt")));
    }

    [Fact]
    public void ExtractArchive_ObservesCancellationBeforeWritingNextEntry()
    {
        var sourceDirectory = CreateDirectory("cancel-source");
        File.WriteAllText(Path.Combine(sourceDirectory, "ffprobe.exe"), "tool");
        var archivePath = Path.Combine(_tempDirectory, "cancel.zip");
        ZipFile.CreateFromDirectory(sourceDirectory, archivePath);
        var destinationDirectory = Path.Combine(_tempDirectory, "cancel-extracted");
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var extractor = new ManagedToolArchiveExtractor();

        Assert.Throws<OperationCanceledException>(() =>
            extractor.ExtractArchive(archivePath, destinationDirectory, cancellationToken: cancellationSource.Token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(destinationDirectory));
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

    private static void WriteEmbeddedSevenZipFixture(string archivePath)
    {
        // Dieses winzige LZMA-7z-Fixture stammt aus den MIT-lizenzierten SharpCompress-Testdaten.
        // Es hält den Lesetest deterministisch und unabhängig von externen Werkzeugen oder der
        // separaten 7z-Schreib-API von SharpCompress.
        const string sevenZipTarArchiveBase64 =
            "N3q8ryccAAROQeMgjgAAAAAAAABiAAAAAAAAAPuVhFTgJ/8Ahl0AG5aJJwF8"
            + "i4Hohbo/g3l1MhrPDLofTBbHVI8Pdh5jx6VLFVj6aObpjQsRguRsba+9Od+7"
            + "hRsFwfPBHfAJn7TexW8Go5hhdsgkt8J385KiQ14huF0gETgQxlOd0GRYF3d/"
            + "72iFMHpwYsuRh6ZEowfn9eiH2qi+DtrM0CAL7ge9/YeKGC3oUgAAAQQGAAEJ"
            + "gI4ABwsBAAEhIQEDDKgAAAgKAV3XFwsAAAUBGQoAAAAAAAAAAAAAERsANwBa"
            + "AGkAcAAuAFQAYQByAC4AdABhAHIAAAAZABQKAQAAJgdey9fZARUGAQAggKSB"
            + "AAA=";
        File.WriteAllBytes(archivePath, Convert.FromBase64String(sevenZipTarArchiveBase64));
    }

    private sealed class CollectingProgress(List<ManagedToolExtractionProgress> events) : IProgress<ManagedToolExtractionProgress>
    {
        public void Report(ManagedToolExtractionProgress value)
        {
            events.Add(value);
        }
    }
}
