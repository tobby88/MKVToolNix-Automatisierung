using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class ManagedToolArchiveExtractorTests : IDisposable
{
    /// <summary>
    /// Uses the existing tiny real LZMA2 payload with a larger declared dictionary,
    /// not a large download or generated 7z writer dependency. Extraction must
    /// preserve the complete payload at the official package's 256 MiB boundary,
    /// while a 512 MiB request must fail before any output file is created.
    /// </summary>
    [Theory]
    [InlineData((byte)28, true)]
    [InlineData((byte)32, true)]
    [InlineData((byte)34, false)]
    public async Task ExtractArchive_Lzma2DictionaryAcceptsPortableToolBoundaryButRejectsLargerAllocations(byte dictionaryProperty, bool accepted)
    {
        var archivePath = Path.Combine(_tempDirectory, "dictionary.7z");
        var destination = Path.Combine(_tempDirectory, "dictionary-target");
        WriteEmbeddedSevenZipFixture(archivePath, dictionaryProperty);
        var extractor = new ManagedToolArchiveExtractor();
        Assert.Equal(256L * 1024 * 1024, SharpCompress.Common.DictionarySizeLimits.Lzma2);
        if (!accepted)
        {
            var error = await Assert.ThrowsAsync<SharpCompress.Common.InvalidFormatException>(() =>
                extractor.ExtractArchiveAsync(archivePath, destination));
            Assert.Contains("268435456", error.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories));
            return;
        }

        await extractor.ExtractArchiveAsync(archivePath, destination);
        var baselineArchive = Path.Combine(_tempDirectory, "baseline.7z");
        var baselineDestination = Path.Combine(_tempDirectory, "baseline-target");
        WriteEmbeddedSevenZipFixture(baselineArchive);
        await extractor.ExtractArchiveAsync(baselineArchive, baselineDestination);
        Assert.Equal(File.ReadAllBytes(Path.Combine(baselineDestination, "7Zip.Tar.tar")),
            File.ReadAllBytes(Path.Combine(destination, "7Zip.Tar.tar")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExtractArchive_RejectsOversizedPayloadOrInsufficientSpaceBeforeFirstFile(bool oversized)
    {
        var archive = Path.Combine(_tempDirectory, "budget.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using var first = zip.CreateEntry("tool.exe").Open();
            first.Write(new byte[4096]);
        }
        var destination = Path.Combine(_tempDirectory, "budget-target");
        var extractor = new ManagedToolArchiveExtractor(oversized ? 2048 : 8192, _ => oversized ? long.MaxValue : 1024);
        await Assert.ThrowsAsync<IOException>(() => extractor.ExtractArchiveAsync(archive, destination));
        Assert.Empty(Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExtractArchive_BudgetOnlyCountsSelectedToolPayload()
    {
        var archive = Path.Combine(_tempDirectory, "selected-budget.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using (var binary = zip.CreateEntry("bin/ffprobe.exe").Open()) binary.Write(new byte[100]);
            using (var docs = zip.CreateEntry("doc/large.txt").Open()) docs.Write(new byte[4096]);
        }
        var destination = Path.Combine(_tempDirectory, "selected-target");
        await new ManagedToolArchiveExtractor(128, _ => null).ExtractArchiveAsync(archive, destination, toolKind: ManagedToolKind.Ffprobe);
        Assert.Single(Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories));
    }

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
    public async Task ExtractArchive_ExtractsZipEntriesAndReportsProgress()
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

        await extractor.ExtractArchiveAsync(archivePath, destinationDirectory, new CollectingProgress(progressEvents));

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
    public async Task ExtractArchive_ReportsByteProgressWhileWritingLargeEntry()
    {
        var sourceDirectory = CreateDirectory("large-source");
        var largePayload = new byte[220_000];
        new Random(42).NextBytes(largePayload);
        File.WriteAllBytes(Path.Combine(sourceDirectory, "ffprobe.exe"), largePayload);

        var archivePath = Path.Combine(_tempDirectory, "large.zip");
        ZipFile.CreateFromDirectory(sourceDirectory, archivePath);

        var destinationDirectory = Path.Combine(_tempDirectory, "large-extracted");
        var progressEvents = new List<ManagedToolExtractionProgress>();
        var extractor = new ManagedToolArchiveExtractor();

        await extractor.ExtractArchiveAsync(archivePath, destinationDirectory, new CollectingProgress(progressEvents));

        Assert.Contains(
            progressEvents,
            entry => entry.ExtractedEntryCount == 0
                     && entry.ExtractedByteCount is > 0
                     && entry.ExtractedByteCount < entry.TotalByteCount);
        Assert.Equal(largePayload.Length, new FileInfo(Path.Combine(destinationDirectory, "ffprobe.exe")).Length);
    }

    [Fact]
    public async Task ExtractArchive_ForMkvToolNix_SkipsDocumentationAndLocalePayload()
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

        await extractor.ExtractArchiveAsync(archivePath, destinationDirectory, toolKind: ManagedToolKind.MkvToolNix);

        Assert.True(File.Exists(Path.Combine(destinationDirectory, "mkvtoolnix", "mkvmerge.exe")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "mkvtoolnix", "mkvpropedit.exe")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "mkvtoolnix", "mkvextract.exe")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "mkvtoolnix", "libebml.dll")));
        Assert.False(File.Exists(Path.Combine(destinationDirectory, "mkvtoolnix", "doc", "guide.txt")));
        Assert.False(File.Exists(Path.Combine(destinationDirectory, "mkvtoolnix", "locale", "de.mo")));
    }

    [Fact]
    public async Task ExtractArchive_ReadsSevenZipArchives()
    {
        var archivePath = Path.Combine(_tempDirectory, "fixture.7z");
        WriteEmbeddedSevenZipFixture(archivePath);

        var destinationDirectory = Path.Combine(_tempDirectory, "mkvtoolnix-7z-extracted");
        var extractor = new ManagedToolArchiveExtractor();

        await extractor.ExtractArchiveAsync(archivePath, destinationDirectory);

        Assert.True(File.Exists(Path.Combine(destinationDirectory, "7Zip.Tar.tar")));
    }

    [Fact]
    public async Task ExtractArchive_ForFfprobe_SkipsDocumentationAndPreservesBinPayload()
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

        await extractor.ExtractArchiveAsync(archivePath, destinationDirectory, toolKind: ManagedToolKind.Ffprobe);

        Assert.True(File.Exists(Path.Combine(destinationDirectory, "ffmpeg-master-latest-win64-gpl-shared", "bin", "ffprobe.exe")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "ffmpeg-master-latest-win64-gpl-shared", "bin", "avcodec-61.dll")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "ffmpeg-master-latest-win64-gpl-shared", "bin", "ffmpeg.exe")));
        Assert.False(File.Exists(Path.Combine(destinationDirectory, "ffmpeg-master-latest-win64-gpl-shared", "doc", "readme.txt")));
        Assert.False(File.Exists(Path.Combine(destinationDirectory, "ffmpeg-master-latest-win64-gpl-shared", "presets", "libx264.ffpreset")));
    }

    [Fact]
    public async Task ExtractArchive_ForMediathekView_PreservesCompletePortablePayload()
    {
        var sourceDirectory = CreateDirectory("source-mediathekview");
        var archiveRoot = Path.Combine(sourceDirectory, "MediathekView");
        var portableDirectory = Path.Combine(archiveRoot, "Portable");
        var javaDirectory = Path.Combine(archiveRoot, "jre", "bin");
        Directory.CreateDirectory(portableDirectory);
        Directory.CreateDirectory(javaDirectory);
        File.WriteAllText(Path.Combine(archiveRoot, "MediathekView.exe"), "standard");
        File.WriteAllText(Path.Combine(portableDirectory, "MediathekView_Portable.exe"), "portable");
        File.WriteAllText(Path.Combine(javaDirectory, "java.exe"), "java");
        File.WriteAllText(Path.Combine(archiveRoot, "lib.jar"), "library");

        var archivePath = Path.Combine(_tempDirectory, "mediathekview.zip");
        ZipFile.CreateFromDirectory(sourceDirectory, archivePath);

        var destinationDirectory = Path.Combine(_tempDirectory, "mediathekview-extracted");
        var extractor = new ManagedToolArchiveExtractor();

        await extractor.ExtractArchiveAsync(archivePath, destinationDirectory, toolKind: ManagedToolKind.MediathekView);

        Assert.True(File.Exists(Path.Combine(destinationDirectory, "MediathekView", "MediathekView.exe")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "MediathekView", "Portable", "MediathekView_Portable.exe")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "MediathekView", "jre", "bin", "java.exe")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "MediathekView", "lib.jar")));
    }

    [Fact]
    public async Task ExtractArchive_ThrowsWhenArchiveIsCorrupt()
    {
        var archivePath = Path.Combine(_tempDirectory, "broken.zip");
        File.WriteAllText(archivePath, "not-a-real-archive");
        var destinationDirectory = Path.Combine(_tempDirectory, "broken-extracted");
        var extractor = new ManagedToolArchiveExtractor();

        var exception = await Record.ExceptionAsync(() => extractor.ExtractArchiveAsync(archivePath, destinationDirectory));

        Assert.NotNull(exception);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destinationDirectory));
    }

    [Fact]
    public async Task ExtractArchive_RejectsEntriesOutsideDestinationDirectory()
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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            extractor.ExtractArchiveAsync(archivePath, destinationDirectory));

        Assert.Contains("unsicheren relativen Pfad", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_tempDirectory, "evil.txt")));
    }

    [Fact]
    public async Task ExtractArchive_ObservesCancellationBeforeWritingNextEntry()
    {
        var sourceDirectory = CreateDirectory("cancel-source");
        File.WriteAllText(Path.Combine(sourceDirectory, "ffprobe.exe"), "tool");
        var archivePath = Path.Combine(_tempDirectory, "cancel.zip");
        ZipFile.CreateFromDirectory(sourceDirectory, archivePath);
        var destinationDirectory = Path.Combine(_tempDirectory, "cancel-extracted");
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var extractor = new ManagedToolArchiveExtractor();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            extractor.ExtractArchiveAsync(archivePath, destinationDirectory, cancellationToken: cancellationSource.Token));

        Assert.False(Directory.Exists(destinationDirectory));
    }

    [Theory]
    [InlineData("payload.exe:stream")]
    [InlineData("folder./payload.exe")]
    [InlineData("folder /payload.exe")]
    [InlineData("NUL.txt")]
    [InlineData("COM1/file.txt")]
    [InlineData("../outside.txt")]
    public async Task ExtractArchive_PreflightsUnsafePathsBeforeWritingAnyPayload(string unsafeEntry)
    {
        var archivePath = Path.Combine(_tempDirectory, "unsafe.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("valid.txt");
            archive.CreateEntry(unsafeEntry);
        }
        var destination = Path.Combine(_tempDirectory, "preflight");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ManagedToolArchiveExtractor().ExtractArchiveAsync(archivePath, destination));

        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
    }

    [Fact]
    public async Task ExtractArchive_RejectsDuplicateWindowsPaths()
    {
        var archivePath = Path.Combine(_tempDirectory, "duplicate.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("tool.exe");
            archive.CreateEntry("TOOL.EXE");
        }
        var destination = Path.Combine(_tempDirectory, "duplicate-extracted");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ManagedToolArchiveExtractor().ExtractArchiveAsync(archivePath, destination));

        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
    }

    [Fact]
    public async Task ExtractArchive_RefusesNonemptyDestinationWithoutOverwritingFiles()
    {
        var archivePath = Path.Combine(_tempDirectory, "existing.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("tool.exe");
        }
        var destination = CreateDirectory("existing");
        var existing = Path.Combine(destination, "tool.exe");
        File.WriteAllText(existing, "keep");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ManagedToolArchiveExtractor().ExtractArchiveAsync(archivePath, destination));

        Assert.Equal("keep", File.ReadAllText(existing));
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

    private static void WriteEmbeddedSevenZipFixture(string archivePath, byte? dictionaryProperty = null)
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
        var bytes = Convert.FromBase64String(sevenZipTarArchiveBase64);
        if (dictionaryProperty is { } property)
        {
            // This fixture has a plain 7z next-header with exactly one LZMA2
            // coder (flags 0x21, method 0x21, one-byte property originally 3).
            // Only its dictionary declaration changes. Repair both checksums so
            // failures exercise decoder limits rather than a corrupt header.
            var headerOffset = checked(32 + (int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(12, 8)));
            var headerSize = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(20, 8)));
            var header = bytes.AsSpan(headerOffset, headerSize);
            var index = header.IndexOf(new byte[] { 0x21, 0x21, 0x01, 0x03 });
            Assert.True(index >= 0);
            Assert.Equal(-1, header[(index + 4)..].IndexOf(new byte[] { 0x21, 0x21, 0x01, 0x03 }));
            header[index + 3] = property;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28, 4), FixtureCrc32(header));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), FixtureCrc32(bytes.AsSpan(12, 20)));
        }
        File.WriteAllBytes(archivePath, bytes);
    }

    private static uint FixtureCrc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        return ~crc;
    }

    private sealed class CollectingProgress(List<ManagedToolExtractionProgress> events) : IProgress<ManagedToolExtractionProgress>
    {
        public void Report(ManagedToolExtractionProgress value)
        {
            events.Add(value);
        }
    }
}
