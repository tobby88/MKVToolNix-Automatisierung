using System.IO;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class DownloadSortServiceTests : IDisposable
{
    private readonly string _rootDirectory;
    private readonly DownloadSortService _service = new();

    public DownloadSortServiceTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-download-sort-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDirectory);
    }

    [Fact]
    public void Scan_MapsAliasSeries_AndPlansLegacyFolderRename()
    {
        var legacyDirectory = Path.Combine(_rootDirectory, "Der Kommissar und");
        Directory.CreateDirectory(legacyDirectory);
        CreateEmptyFile(Path.Combine(legacyDirectory, "Der Kommissar und das Meer-Hoerfassung_ In einem kalten Land.mp4"));

        CreateEmptyFile(Path.Combine(_rootDirectory, "Der Kommissar und der See-Auf dunkler See (S03_E07)-0107674899.mp4"));
        CreateCompanionText(
            Path.Combine(_rootDirectory, "Der Kommissar und der See-Auf dunkler See (S03_E07)-0107674899.txt"),
            topic: "Der Kommissar und der See",
            title: "Auf dunkler See (S03/E07)");

        var result = _service.Scan(_rootDirectory);
        var item = Assert.Single(result.Items);

        Assert.Equal("Der Kommissar und das Meer", item.DetectedSeriesName);
        Assert.Equal("Der Kommissar und das Meer", item.SuggestedFolderName);
        Assert.Equal(DownloadSortItemState.Ready, item.State);
        var renamePlan = Assert.Single(result.FolderRenames);
        Assert.Equal("Der Kommissar und", renamePlan.CurrentFolderName);
        Assert.Equal("Der Kommissar und das Meer", renamePlan.TargetFolderName);
    }

    [Fact]
    public void Scan_UsesContainedAlias_WhenTxtTopicIsGeneric()
    {
        CreateEmptyFile(Path.Combine(_rootDirectory, "Filme-Pettersson und Findus - Findus zieht um-0585373376.mp4"));
        CreateCompanionText(
            Path.Combine(_rootDirectory, "Filme-Pettersson und Findus - Findus zieht um-0585373376.txt"),
            topic: "Filme",
            title: "Pettersson und Findus - Findus zieht um");

        var result = _service.Scan(_rootDirectory);
        var item = Assert.Single(result.Items);

        Assert.Equal("Pettersson und Findus", item.DetectedSeriesName);
        Assert.Equal("Pettersson und Findus", item.SuggestedFolderName);
        Assert.Equal(DownloadSortItemState.Ready, item.State);
    }

    [Fact]
    public void Scan_MapsKnownMucklasSpecialCase_ToPetterssonAndFindusFolder()
    {
        CreateEmptyFile(Path.Combine(_rootDirectory, "Filme-Die Mucklas - Ein neues Abenteuer-1864316966.mp4"));
        CreateCompanionText(
            Path.Combine(_rootDirectory, "Filme-Die Mucklas - Ein neues Abenteuer-1864316966.txt"),
            topic: "Filme",
            title: "Die Mucklas - Ein neues Abenteuer");

        var result = _service.Scan(_rootDirectory);
        var item = Assert.Single(result.Items);

        Assert.Equal("Pettersson und Findus", item.DetectedSeriesName);
        Assert.Equal("Pettersson und Findus", item.SuggestedFolderName);
        Assert.Equal(DownloadSortItemState.Ready, item.State);
    }

    [Fact]
    public void Scan_MapsEditorialTitleWithQuotedSeriesAlias()
    {
        CreateEmptyFile(Path.Combine(_rootDirectory, "hallo deutschland-Antoine Monot in Jubilaeumsstaffel-1234.mp4"));
        CreateCompanionText(
            Path.Combine(_rootDirectory, "hallo deutschland-Antoine Monot in Jubilaeumsstaffel-1234.txt"),
            topic: "hallo deutschland",
            title: "Antoine Monot in Jubilaeumsstaffel - \"Ein Fall für zwei\" feiert 10 Jahre Jubilaeum");

        var result = _service.Scan(_rootDirectory);
        var item = Assert.Single(result.Items);

        Assert.Equal("Ein Fall für Zwei", item.DetectedSeriesName);
        Assert.Equal("Ein Fall für Zwei", item.SuggestedFolderName);
        Assert.Equal(DownloadSortItemState.Ready, item.State);
    }

    [Fact]
    public void Scan_MapsMagazineTitleWithQuotedSokoLeipzigAlias()
    {
        CreateEmptyFile(Path.Combine(_rootDirectory, "Riverboat-Marco Girnth ueber den Abschied von SOKO Leipzig-1234.mp4"));
        CreateCompanionText(
            Path.Combine(_rootDirectory, "Riverboat-Marco Girnth ueber den Abschied von SOKO Leipzig-1234.txt"),
            topic: "Riverboat",
            title: "Marco Girnth ueber den Abschied von \"SOKO Leipzig\"");

        var result = _service.Scan(_rootDirectory);
        var item = Assert.Single(result.Items);

        Assert.Equal("SOKO Leipzig", item.DetectedSeriesName);
        Assert.Equal("SOKO Leipzig", item.SuggestedFolderName);
        Assert.Equal(DownloadSortItemState.Ready, item.State);
    }

    [Fact]
    public void Scan_MapsDieHeilandLongBroadcastName_ToExistingShortFolderName()
    {
        CreateEmptyFile(Path.Combine(_rootDirectory, "Die Heiland - Wir sind Anwalt-Biggis Blond (S04_E27)-0475816257.mp4"));
        CreateCompanionText(
            Path.Combine(_rootDirectory, "Die Heiland - Wir sind Anwalt-Biggis Blond (S04_E27)-0475816257.txt"),
            topic: "Die Heiland - Wir sind Anwalt",
            title: "Biggis Blond (S04/E27)");

        var result = _service.Scan(_rootDirectory);
        var item = Assert.Single(result.Items);

        Assert.Equal("Die Heiland", item.DetectedSeriesName);
        Assert.Equal("Die Heiland", item.SuggestedFolderName);
        Assert.Equal(DownloadSortItemState.Ready, item.State);
    }

    [Fact]
    public void Scan_SortsCandidatesByTargetFolder_BeforeDisplayName()
    {
        CreateEmptyFile(Path.Combine(_rootDirectory, "ZZZ-Folge-1.mp4"));
        CreateCompanionText(
            Path.Combine(_rootDirectory, "ZZZ-Folge-1.txt"),
            topic: "Die Heiland - Wir sind Anwalt",
            title: "ZZZ-Folge");
        CreateEmptyFile(Path.Combine(_rootDirectory, "AAA-Folge-1.mp4"));
        CreateCompanionText(
            Path.Combine(_rootDirectory, "AAA-Folge-1.txt"),
            topic: "SOKO Leipzig",
            title: "AAA-Folge");

        var result = _service.Scan(_rootDirectory);

        Assert.Collection(
            result.Items,
            first =>
            {
                Assert.Equal("Die Heiland", first.SuggestedFolderName);
                Assert.Equal("ZZZ-Folge", first.DisplayName);
            },
            second =>
            {
                Assert.Equal("SOKO Leipzig", second.SuggestedFolderName);
                Assert.Equal("AAA-Folge", second.DisplayName);
            });
    }

    [Fact]
    public void Scan_ReportsIncrementalProgressWhileBuildingCandidates()
    {
        CreateEmptyFile(Path.Combine(_rootDirectory, "ZZZ-Folge-1.mp4"));
        CreateCompanionText(
            Path.Combine(_rootDirectory, "ZZZ-Folge-1.txt"),
            topic: "Die Heiland - Wir sind Anwalt",
            title: "ZZZ-Folge");
        CreateEmptyFile(Path.Combine(_rootDirectory, "AAA-Folge-1.mp4"));
        CreateCompanionText(
            Path.Combine(_rootDirectory, "AAA-Folge-1.txt"),
            topic: "SOKO Leipzig",
            title: "AAA-Folge");
        var progressUpdates = new List<DownloadSortScanProgress>();

        _ = _service.Scan(_rootDirectory, progressUpdates.Add);

        Assert.Contains(progressUpdates, update => update.StatusText.Contains("Lese lose Download-Dateien", StringComparison.Ordinal));
        Assert.Contains(progressUpdates, update => update.StatusText.Contains("Analysiere Paket 1/2", StringComparison.Ordinal));
        Assert.Contains(progressUpdates, update => update.StatusText.Contains("Analysiere Paket 2/2", StringComparison.Ordinal));
        Assert.Equal(100, progressUpdates[^1].ProgressPercent);
        Assert.True(progressUpdates.Select(update => update.ProgressPercent).SequenceEqual(
            progressUpdates.Select(update => update.ProgressPercent).OrderBy(value => value)));
    }

    [Fact]
    public void Scan_MarksCandidateAsReadyWithReplacement_WhenTargetAlreadyContainsComparableSameFile()
    {
        var targetDirectory = Path.Combine(_rootDirectory, "Ostfriesenkrimis");
        Directory.CreateDirectory(targetDirectory);
        CreateFileWithByteLength(Path.Combine(targetDirectory, "Ostfriesenkrimis-Ostfriesensturm (S02_E05)-1759523164.mp4"), length: 100);

        CreateFileWithByteLength(Path.Combine(_rootDirectory, "Ostfriesenkrimis-Ostfriesensturm (S02_E05)-1759523164.mp4"), length: 100);
        CreateCompanionText(
            Path.Combine(_rootDirectory, "Ostfriesenkrimis-Ostfriesensturm (S02_E05)-1759523164.txt"),
            topic: "Ostfriesenkrimis",
            title: "Ostfriesensturm (S02/E05)");

        var result = _service.Scan(_rootDirectory);
        var item = Assert.Single(result.Items);

        Assert.Equal(DownloadSortItemState.ReadyWithReplacement, item.State);
        Assert.Contains("Gleichnamige Zieldatei wird ersetzt.", item.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("Ostfriesenkrimis-Ostfriesensturm", item.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_UsesCompactReplacementNote_WhenMultipleTargetFilesWillBeOverwritten()
    {
        var targetDirectory = Path.Combine(_rootDirectory, "Ostfriesenkrimis");
        var videoFileName = "Ostfriesenkrimis-Ostfriesensturm (S02_E05)-1759523164.mp4";
        var textFileName = "Ostfriesenkrimis-Ostfriesensturm (S02_E05)-1759523164.txt";
        Directory.CreateDirectory(targetDirectory);
        CreateFileWithByteLength(Path.Combine(targetDirectory, videoFileName), length: 100);
        CreateEmptyFile(Path.Combine(targetDirectory, textFileName));

        CreateFileWithByteLength(Path.Combine(_rootDirectory, videoFileName), length: 100);
        CreateCompanionText(
            Path.Combine(_rootDirectory, textFileName),
            topic: "Ostfriesenkrimis",
            title: "Ostfriesensturm (S02/E05)");

        var result = _service.Scan(_rootDirectory);
        var item = Assert.Single(result.Items);

        Assert.Equal(DownloadSortItemState.ReadyWithReplacement, item.State);
        Assert.Contains("Gleichnamige Zieldateien werden ersetzt.", item.Note, StringComparison.Ordinal);
        Assert.DoesNotContain(videoFileName, item.Note, StringComparison.Ordinal);
        Assert.DoesNotContain(textFileName, item.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_MarksCandidateAsConflict_WhenExistingTargetVideoIsSignificantlyLarger()
    {
        var targetDirectory = Path.Combine(_rootDirectory, "Ostfriesenkrimis");
        Directory.CreateDirectory(targetDirectory);
        CreateFileWithByteLength(Path.Combine(targetDirectory, "Ostfriesenkrimis-Ostfriesensturm (S02_E05)-1759523164.mp4"), length: 121);

        CreateFileWithByteLength(Path.Combine(_rootDirectory, "Ostfriesenkrimis-Ostfriesensturm (S02_E05)-1759523164.mp4"), length: 100);
        CreateCompanionText(
            Path.Combine(_rootDirectory, "Ostfriesenkrimis-Ostfriesensturm (S02_E05)-1759523164.txt"),
            topic: "Ostfriesenkrimis",
            title: "Ostfriesensturm (S02/E05)");

        var result = _service.Scan(_rootDirectory);
        var item = Assert.Single(result.Items);

        Assert.Equal(DownloadSortItemState.Conflict, item.State);
        Assert.Contains("deutlich größer", item.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_SplitsDefectiveVideoFromUsableCompanionFiles_WhenTxtSizeShowsIncompleteDownload()
    {
        var videoPath = Path.Combine(_rootDirectory, "Neues aus Büttenwarder-Bildungsschock-0186867506.mp4");
        var textPath = Path.Combine(_rootDirectory, "Neues aus Büttenwarder-Bildungsschock-0186867506.txt");
        var subtitlePath = Path.Combine(_rootDirectory, "Neues aus Büttenwarder-Bildungsschock-0186867506.srt");
        CreateFileWithByteLength(videoPath, length: 1024 * 1024);
        CreateCompanionText(
            textPath,
            topic: "Neues aus Büttenwarder",
            title: "Bildungsschock",
            sizeText: "100,0 MiB");
        CreateEmptyFile(subtitlePath);

        var result = _service.Scan(_rootDirectory);

        var defective = Assert.Single(result.Items);
        Assert.Equal(DownloadSortItemState.Defective, defective.State);
        Assert.Equal("defekt", defective.SuggestedFolderName);
        Assert.Contains(videoPath, defective.FilePaths);
        Assert.Contains(textPath, defective.FilePaths);
        Assert.Contains(subtitlePath, defective.FilePaths);
        Assert.Contains("deutlich kleiner", defective.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_MarksTinyLongDurationVideoAsDefective_WhenTxtHasNoExpectedSize()
    {
        var videoPath = Path.Combine(_rootDirectory, "Der Alte-Der Alte_ Wunschkind-0264348449.mp4");
        var textPath = Path.Combine(_rootDirectory, "Der Alte-Der Alte_ Wunschkind-0264348449.txt");
        CreateFileWithByteLength(videoPath, length: 1024 * 1024);
        CreateCompanionText(
            textPath,
            topic: "Der Alte",
            title: "Der Alte: Wunschkind",
            duration: "00:58:46");

        var result = _service.Scan(_rootDirectory);

        var defective = Assert.Single(result.Items, item => item.State == DownloadSortItemState.Defective);
        Assert.Equal("defekt", defective.SuggestedFolderName);
        Assert.Contains(videoPath, defective.FilePaths);
        Assert.Contains(textPath, defective.FilePaths);
        Assert.Contains("auffällig klein", defective.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_DoesNotPlanRenameForDefectiveFolder()
    {
        var defectiveDirectory = Path.Combine(_rootDirectory, "defekt");
        Directory.CreateDirectory(defectiveDirectory);
        CreateEmptyFile(Path.Combine(defectiveDirectory, "Der Alte-Der Alte_ Wunschkind-0264348449.mp4"));
        CreateCompanionText(
            Path.Combine(defectiveDirectory, "Der Alte-Der Alte_ Wunschkind-0264348449.txt"),
            topic: "Der Alte",
            title: "Der Alte: Wunschkind",
            duration: "00:58:46");

        var result = _service.Scan(_rootDirectory);

        Assert.Empty(result.FolderRenames);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Apply_RenamesLegacyFolder_AndMovesLooseFilesIntoCanonicalDirectory()
    {
        var legacyDirectory = Path.Combine(_rootDirectory, "Der Kommissar und");
        Directory.CreateDirectory(legacyDirectory);
        CreateEmptyFile(Path.Combine(legacyDirectory, "Der Kommissar und das Meer-Hoerfassung_ In einem kalten Land.mp4"));

        var looseVideoPath = Path.Combine(_rootDirectory, "Der Kommissar und der See-Auf dunkler See (S03_E07)-0107674899.mp4");
        var looseTextPath = Path.Combine(_rootDirectory, "Der Kommissar und der See-Auf dunkler See (S03_E07)-0107674899.txt");
        CreateEmptyFile(looseVideoPath);
        CreateCompanionText(looseTextPath, "Der Kommissar und der See", "Auf dunkler See (S03/E07)");

        var scanResult = _service.Scan(_rootDirectory);
        var item = Assert.Single(scanResult.Items);

        var applyResult = _service.Apply(
            _rootDirectory,
            [new DownloadSortMoveRequest(item.DisplayName, item.FilePaths, item.SuggestedFolderName)],
            scanResult.FolderRenames);

        var canonicalDirectory = Path.Combine(_rootDirectory, "Der Kommissar und das Meer");

        Assert.Equal(1, applyResult.MovedGroupCount);
        Assert.Equal(2, applyResult.MovedFileCount);
        Assert.Equal(1, applyResult.RenamedFolderCount);
        Assert.True(Directory.Exists(canonicalDirectory));
        Assert.False(Directory.Exists(legacyDirectory));
        Assert.True(File.Exists(Path.Combine(canonicalDirectory, Path.GetFileName(looseVideoPath))));
        Assert.True(File.Exists(Path.Combine(canonicalDirectory, Path.GetFileName(looseTextPath))));
        Assert.False(File.Exists(looseVideoPath));
        Assert.False(File.Exists(looseTextPath));
    }

    [Fact]
    public void Apply_OverwritesExistingTargetFile_WhenLooseVersionIsComparableOrLarger()
    {
        var targetDirectory = Path.Combine(_rootDirectory, "Ostfriesenkrimis");
        var fileName = "Ostfriesenkrimis-Ostfriesensturm (S02_E05)-1759523164.mp4";
        var targetPath = Path.Combine(targetDirectory, fileName);
        var loosePath = Path.Combine(_rootDirectory, fileName);
        Directory.CreateDirectory(targetDirectory);
        CreateFileWithByteLength(targetPath, length: 80, value: 1);
        CreateFileWithByteLength(loosePath, length: 100, value: 2);

        var scanResult = _service.Scan(_rootDirectory);
        var item = Assert.Single(scanResult.Items);

        var applyResult = _service.Apply(
            _rootDirectory,
            [new DownloadSortMoveRequest(item.DisplayName, item.FilePaths, item.SuggestedFolderName)],
            scanResult.FolderRenames);

        Assert.Equal(1, applyResult.MovedGroupCount);
        Assert.Equal(1, applyResult.MovedFileCount);
        Assert.Equal(0, applyResult.SkippedGroupCount);
        Assert.False(File.Exists(loosePath));
        Assert.True(File.Exists(targetPath));
        Assert.Equal(100, new FileInfo(targetPath).Length);
        Assert.Contains(applyResult.LogLines, line => line.StartsWith("ERSETZT:", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_SkipsReplacement_WhenExistingTargetVideoIsSignificantlyLarger()
    {
        var targetDirectory = Path.Combine(_rootDirectory, "Ostfriesenkrimis");
        var fileName = "Ostfriesenkrimis-Ostfriesensturm (S02_E05)-1759523164.mp4";
        var targetPath = Path.Combine(targetDirectory, fileName);
        var loosePath = Path.Combine(_rootDirectory, fileName);
        Directory.CreateDirectory(targetDirectory);
        CreateFileWithByteLength(targetPath, length: 121, value: 1);
        CreateFileWithByteLength(loosePath, length: 100, value: 2);

        var applyResult = _service.Apply(
            _rootDirectory,
            [new DownloadSortMoveRequest("Ostfriesensturm", [loosePath], "Ostfriesenkrimis")],
            []);

        Assert.Equal(0, applyResult.MovedGroupCount);
        Assert.Equal(0, applyResult.MovedFileCount);
        Assert.Equal(1, applyResult.SkippedGroupCount);
        Assert.True(File.Exists(loosePath));
        Assert.True(File.Exists(targetPath));
        Assert.Equal(121, new FileInfo(targetPath).Length);
        Assert.Contains(applyResult.LogLines, line => line.StartsWith("KONFLIKT:", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_MovesDefectivePackageToDefectiveFolder()
    {
        var videoPath = Path.Combine(_rootDirectory, "Neues aus Büttenwarder-Bildungsschock-0186867506.mp4");
        var textPath = Path.Combine(_rootDirectory, "Neues aus Büttenwarder-Bildungsschock-0186867506.txt");
        var subtitlePath = Path.Combine(_rootDirectory, "Neues aus Büttenwarder-Bildungsschock-0186867506.srt");
        CreateFileWithByteLength(videoPath, length: 1024 * 1024);
        CreateCompanionText(
            textPath,
            topic: "Neues aus Büttenwarder",
            title: "Bildungsschock",
            sizeText: "100,0 MiB");
        CreateEmptyFile(subtitlePath);

        var scanResult = _service.Scan(_rootDirectory);
        var applyResult = _service.Apply(
            _rootDirectory,
            scanResult.Items
                .Where(item => DownloadSortItemStates.IsSortable(item.State))
                .Select(item => new DownloadSortMoveRequest(item.DisplayName, item.FilePaths, item.SuggestedFolderName))
                .ToList(),
            scanResult.FolderRenames);

        Assert.Equal(1, applyResult.MovedGroupCount);
        Assert.Equal(3, applyResult.MovedFileCount);
        Assert.True(File.Exists(Path.Combine(_rootDirectory, "defekt", Path.GetFileName(videoPath))));
        Assert.True(File.Exists(Path.Combine(_rootDirectory, "defekt", Path.GetFileName(textPath))));
        Assert.True(File.Exists(Path.Combine(_rootDirectory, "defekt", Path.GetFileName(subtitlePath))));
        Assert.Contains(applyResult.LogLines, line => line.StartsWith("DEFEKT:", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private static void CreateEmptyFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "content");
    }

    private static void CreateFileWithByteLength(string path, int length, byte value = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat(value, length).ToArray());
    }

    private static void CreateCompanionText(
        string path,
        string topic,
        string title,
        string duration = "00:05:00",
        string? sizeText = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = new List<string>
        {
            $"Thema:       {topic}",
            string.Empty,
            $"Titel:       {title}",
            string.Empty,
            $"Dauer:       {duration}"
        };
        if (!string.IsNullOrWhiteSpace(sizeText))
        {
            lines.Add($"Größe:       {sizeText}");
        }

        File.WriteAllText(path, string.Join(Environment.NewLine, lines));
    }
}
