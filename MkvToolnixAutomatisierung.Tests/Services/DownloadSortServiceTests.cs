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
    public void Scan_ThrowsOperationCanceled_WhenCancellationIsRequested()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _service.Scan(_rootDirectory, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Apply_ThrowsOperationCanceled_BeforeMovingNextGroup()
    {
        var videoPath = Path.Combine(_rootDirectory, "Serie-Folge-1234.mp4");
        CreateEmptyFile(videoPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _service.Apply(
                _rootDirectory,
                [new DownloadSortMoveRequest("Serie-Folge", [videoPath], "Serie")],
                [],
                cancellation.Token));
        Assert.True(File.Exists(videoPath));
        Assert.False(Directory.Exists(Path.Combine(_rootDirectory, "Serie")));
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
    public void Scan_UsesContainedAlias_WhenTxtTopicIsGenericDokuCategory()
    {
        CreateEmptyFile(Path.Combine(_rootDirectory, "Dokus-Pettersson und Findus - Findus zieht um-0585373376.mp4"));
        CreateCompanionText(
            Path.Combine(_rootDirectory, "Dokus-Pettersson und Findus - Findus zieht um-0585373376.txt"),
            topic: "Dokus",
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
    public void Scan_UsesContainedAliasPrefix_WhenFilenameMergesMarieBrandWithEpisodeTitle()
    {
        CreateEmptyFile(Path.Combine(_rootDirectory, "Marie Brand und die verlorenen Kinder-1860682802.mp4"));

        var result = _service.Scan(_rootDirectory);
        var item = Assert.Single(result.Items);

        Assert.Equal("Marie Brand", item.DetectedSeriesName);
        Assert.Equal("Marie Brand", item.SuggestedFolderName);
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
    public void Scan_UsesRegularCompanionState_WhenUsableCompanionFilesRemain()
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

        var item = Assert.Single(result.Items);

        Assert.Equal(DownloadSortItemState.Ready, item.State);
        Assert.Equal("Neues aus Büttenwarder", item.SuggestedFolderName);
        Assert.True(item.ContainsDefectiveFiles);
        Assert.Contains(videoPath, item.FilePaths);
        Assert.Contains(textPath, item.FilePaths);
        Assert.Contains(subtitlePath, item.FilePaths);
        Assert.Equal([videoPath], item.DefectiveFilePaths);
        Assert.Contains("deutlich kleiner", item.Note, StringComparison.Ordinal);
        Assert.Contains("Begleitdateien", item.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_GroupsLanguageSuffixedSidecarsWithMainVideo()
    {
        var videoPath = Path.Combine(_rootDirectory, "Pettersson und Findus-Findus zieht um-0585373376.mp4");
        var textPath = Path.Combine(_rootDirectory, "Pettersson und Findus-Findus zieht um-0585373376.txt");
        var germanSubtitlePath = Path.Combine(_rootDirectory, "Pettersson und Findus-Findus zieht um-0585373376.de.srt");
        var forcedSubtitlePath = Path.Combine(_rootDirectory, "Pettersson und Findus-Findus zieht um-0585373376.de.forced.ass");
        CreateEmptyFile(videoPath);
        CreateCompanionText(
            textPath,
            topic: "Pettersson und Findus",
            title: "Findus zieht um");
        CreateEmptyFile(germanSubtitlePath);
        CreateEmptyFile(forcedSubtitlePath);

        var result = _service.Scan(_rootDirectory);
        var item = Assert.Single(result.Items);

        Assert.Equal(DownloadSortItemState.Ready, item.State);
        Assert.Contains("Pettersson und Findus", item.DisplayName, StringComparison.Ordinal);
        Assert.Contains(videoPath, item.FilePaths);
        Assert.Contains(textPath, item.FilePaths);
        Assert.Contains(germanSubtitlePath, item.FilePaths);
        Assert.Contains(forcedSubtitlePath, item.FilePaths);
    }

    [Fact]
    public void Scan_UsesNeedsReviewStateForMixedDefectivePackage_WhenNoRegularTargetCanBeDerived()
    {
        var videoPath = Path.Combine(_rootDirectory, "Filme-1234.mp4");
        var textPath = Path.Combine(_rootDirectory, "Filme-1234.txt");
        var subtitlePath = Path.Combine(_rootDirectory, "Filme-1234.srt");
        CreateFileWithByteLength(videoPath, length: 1024 * 1024);
        CreateCompanionText(
            textPath,
            topic: "Filme",
            title: "Unsortierbarer Rest",
            sizeText: "100,0 MiB");
        CreateEmptyFile(subtitlePath);

        var result = _service.Scan(_rootDirectory);

        var item = Assert.Single(result.Items);
        Assert.Equal(DownloadSortItemState.NeedsReview, item.State);
        Assert.True(item.ContainsDefectiveFiles);
        Assert.Equal(string.Empty, item.SuggestedFolderName);
        Assert.Equal([videoPath], item.DefectiveFilePaths);
        Assert.Contains("deutlich kleiner", item.Note, StringComparison.Ordinal);
        Assert.Contains("Kein Zielordner erkannt", item.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_UsesReplacementStateForMixedDefectivePackage_WhenHealthyCompanionWouldOverwriteTarget()
    {
        var targetDirectory = Path.Combine(_rootDirectory, "Neues aus Büttenwarder");
        Directory.CreateDirectory(targetDirectory);

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
        CreateCompanionText(
            Path.Combine(targetDirectory, Path.GetFileName(textPath)),
            topic: "Neues aus Büttenwarder",
            title: "Bildungsschock");

        var result = _service.Scan(_rootDirectory);

        var item = Assert.Single(result.Items);
        Assert.Equal(DownloadSortItemState.ReadyWithReplacement, item.State);
        Assert.True(item.ContainsDefectiveFiles);
        Assert.Equal([videoPath], item.DefectiveFilePaths);
        Assert.Contains("Gleichnamige Zieldatei wird ersetzt.", item.Note, StringComparison.Ordinal);
        Assert.Contains("deutlich kleiner", item.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_RoutesOnlyTxtPairCompletelyToDefective_WhenNoOtherCompanionsExist()
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

        var item = Assert.Single(result.Items);
        Assert.Equal(DownloadSortItemState.Defective, item.State);
        Assert.Equal("defekt", item.SuggestedFolderName);
        Assert.Contains(videoPath, item.FilePaths);
        Assert.Contains(textPath, item.FilePaths);
        var defectiveFilePaths = Assert.IsAssignableFrom<IReadOnlyList<string>>(item.DefectiveFilePaths);
        Assert.Equal(2, defectiveFilePaths.Count);
        Assert.Contains(videoPath, defectiveFilePaths);
        Assert.Contains(textPath, defectiveFilePaths);
        Assert.Contains("Alle zugehörigen Dateien", item.Note, StringComparison.Ordinal);
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
    public void Scan_DoesNotPlanRenameForDoneFolder()
    {
        var doneDirectory = Path.Combine(_rootDirectory, "done");
        Directory.CreateDirectory(doneDirectory);
        CreateEmptyFile(Path.Combine(doneDirectory, "SOKO Leipzig-Take Me Out-1234.mp4"));
        CreateCompanionText(
            Path.Combine(doneDirectory, "SOKO Leipzig-Take Me Out-1234.txt"),
            topic: "SOKO Leipzig",
            title: "Take Me Out");

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
    public void Apply_SkipsUnsafeFolderRenamePlan_OutsideDownloadRoot()
    {
        var parentDirectory = Directory.GetParent(_rootDirectory)!.FullName;
        var externalDirectory = Path.Combine(parentDirectory, "mkv-auto-download-sort-external-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(externalDirectory);
        try
        {
            var looseVideoPath = Path.Combine(_rootDirectory, "Pettersson und Findus-Findus zieht um-0585373376.mp4");
            CreateEmptyFile(looseVideoPath);

            var applyResult = _service.Apply(
                _rootDirectory,
                [new DownloadSortMoveRequest("Pettersson und Findus-Findus zieht um", [looseVideoPath], "Pettersson und Findus")],
                [new DownloadSortFolderRenamePlan(externalDirectory, "Pettersson und Findus", "Stale externer Testplan")]);

            Assert.Equal(0, applyResult.RenamedFolderCount);
            Assert.True(Directory.Exists(externalDirectory));
            Assert.Contains(applyResult.LogLines, line => line.Contains("kein sicherer direkter Download-Unterordner", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(externalDirectory))
            {
                Directory.Delete(externalDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Apply_SkipsMoveRequest_WhenSourceIsOutsideDownloadRoot()
    {
        var parentDirectory = Directory.GetParent(_rootDirectory)!.FullName;
        var externalDirectory = Path.Combine(parentDirectory, "mkv-auto-download-sort-external-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(externalDirectory);
        var externalPath = Path.Combine(externalDirectory, "Serie-Folge-1234.mp4");
        CreateEmptyFile(externalPath);
        try
        {
            var applyResult = _service.Apply(
                _rootDirectory,
                [new DownloadSortMoveRequest("Serie-Folge", [externalPath], "Serie")],
                []);

            Assert.Equal(0, applyResult.MovedGroupCount);
            Assert.Equal(0, applyResult.MovedFileCount);
            Assert.Equal(1, applyResult.SkippedGroupCount);
            Assert.True(File.Exists(externalPath));
            Assert.False(File.Exists(Path.Combine(_rootDirectory, "Serie", Path.GetFileName(externalPath))));
            Assert.Contains(applyResult.LogLines, line => line.Contains("nicht direkt im gewaehlten Download-Ordner", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(externalDirectory))
            {
                Directory.Delete(externalDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Apply_SkipsMoveRequest_WhenSourceIsNestedBelowDownloadRoot()
    {
        var nestedDirectory = Path.Combine(_rootDirectory, "Schon einsortiert");
        Directory.CreateDirectory(nestedDirectory);
        var nestedPath = Path.Combine(nestedDirectory, "Serie-Folge-1234.mp4");
        CreateEmptyFile(nestedPath);

        var applyResult = _service.Apply(
            _rootDirectory,
            [new DownloadSortMoveRequest("Serie-Folge", [nestedPath], "Serie")],
            []);

        Assert.Equal(0, applyResult.MovedGroupCount);
        Assert.Equal(0, applyResult.MovedFileCount);
        Assert.Equal(1, applyResult.SkippedGroupCount);
        Assert.True(File.Exists(nestedPath));
        Assert.False(File.Exists(Path.Combine(_rootDirectory, "Serie", Path.GetFileName(nestedPath))));
        Assert.Contains(applyResult.LogLines, line => line.Contains("nicht direkt im gewaehlten Download-Ordner", StringComparison.Ordinal));
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
    public void Apply_SkipsStaleSourceInsteadOfThrowing_WhenTargetConflictExists()
    {
        var targetDirectory = Path.Combine(_rootDirectory, "Ostfriesenkrimis");
        var fileName = "Ostfriesenkrimis-Ostfriesensturm (S02_E05)-1759523164.mp4";
        var targetPath = Path.Combine(targetDirectory, fileName);
        var loosePath = Path.Combine(_rootDirectory, fileName);
        Directory.CreateDirectory(targetDirectory);
        CreateFileWithByteLength(targetPath, length: 100, value: 1);
        CreateFileWithByteLength(loosePath, length: 100, value: 2);
        File.Delete(loosePath);

        var applyResult = _service.Apply(
            _rootDirectory,
            [new DownloadSortMoveRequest("Ostfriesensturm", [loosePath], "Ostfriesenkrimis")],
            []);

        Assert.Equal(0, applyResult.MovedGroupCount);
        Assert.Equal(0, applyResult.MovedFileCount);
        Assert.Equal(1, applyResult.SkippedGroupCount);
        Assert.True(File.Exists(targetPath));
        Assert.Contains(applyResult.LogLines, line => line.Contains("existiert nicht mehr", StringComparison.Ordinal));
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
                .Select(item => new DownloadSortMoveRequest(item.DisplayName, item.FilePaths, item.SuggestedFolderName, item.DefectiveFilePaths))
                .ToList(),
            scanResult.FolderRenames);

        Assert.Equal(1, applyResult.MovedGroupCount);
        Assert.Equal(3, applyResult.MovedFileCount);
        Assert.True(File.Exists(Path.Combine(_rootDirectory, "defekt", Path.GetFileName(videoPath))));
        Assert.True(File.Exists(Path.Combine(_rootDirectory, "Neues aus Büttenwarder", Path.GetFileName(textPath))));
        Assert.True(File.Exists(Path.Combine(_rootDirectory, "Neues aus Büttenwarder", Path.GetFileName(subtitlePath))));
        Assert.False(File.Exists(Path.Combine(_rootDirectory, "defekt", Path.GetFileName(textPath))));
        Assert.False(File.Exists(Path.Combine(_rootDirectory, "defekt", Path.GetFileName(subtitlePath))));
        Assert.Contains(applyResult.LogLines, line => line.StartsWith("DEFEKT:", StringComparison.Ordinal));
        Assert.Contains(applyResult.LogLines, line => line.StartsWith("SORTIERT:", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_SkipsWholeGroup_WhenDefectiveTargetAlreadyExists()
    {
        var defectiveDirectory = Path.Combine(_rootDirectory, "defekt");
        Directory.CreateDirectory(defectiveDirectory);

        var videoPath = Path.Combine(_rootDirectory, "Neues aus Büttenwarder-Bildungsschock-0186867506.mp4");
        var textPath = Path.Combine(_rootDirectory, "Neues aus Büttenwarder-Bildungsschock-0186867506.txt");
        var subtitlePath = Path.Combine(_rootDirectory, "Neues aus Büttenwarder-Bildungsschock-0186867506.srt");
        var existingDefectiveTarget = Path.Combine(defectiveDirectory, Path.GetFileName(videoPath));
        CreateFileWithByteLength(videoPath, length: 1024 * 1024, value: 1);
        CreateFileWithByteLength(existingDefectiveTarget, length: 2048 * 1024, value: 2);
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
                .Select(item => new DownloadSortMoveRequest(item.DisplayName, item.FilePaths, item.SuggestedFolderName, item.DefectiveFilePaths))
                .ToList(),
            scanResult.FolderRenames);

        Assert.Equal(0, applyResult.MovedGroupCount);
        Assert.Equal(0, applyResult.MovedFileCount);
        Assert.Equal(1, applyResult.SkippedGroupCount);
        Assert.True(File.Exists(videoPath));
        Assert.True(File.Exists(textPath));
        Assert.True(File.Exists(subtitlePath));
        Assert.True(File.Exists(existingDefectiveTarget));
        Assert.Equal(2048 * 1024, new FileInfo(existingDefectiveTarget).Length);
        Assert.False(File.Exists(Path.Combine(_rootDirectory, "Neues aus Büttenwarder", Path.GetFileName(textPath))));
        Assert.Contains(applyResult.LogLines, line => line.StartsWith("KONFLIKT:", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_MovesOnlyTxtPairCompletelyToDefectiveFolder()
    {
        var videoPath = Path.Combine(_rootDirectory, "Der Alte-Der Alte_ Wunschkind-0264348449.mp4");
        var textPath = Path.Combine(_rootDirectory, "Der Alte-Der Alte_ Wunschkind-0264348449.txt");
        CreateFileWithByteLength(videoPath, length: 1024 * 1024);
        CreateCompanionText(
            textPath,
            topic: "Der Alte",
            title: "Der Alte: Wunschkind",
            duration: "00:58:46");

        var scanResult = _service.Scan(_rootDirectory);
        var item = Assert.Single(scanResult.Items);

        var applyResult = _service.Apply(
            _rootDirectory,
            [new DownloadSortMoveRequest(item.DisplayName, item.FilePaths, item.SuggestedFolderName, item.DefectiveFilePaths)],
            scanResult.FolderRenames);

        Assert.Equal(1, applyResult.MovedGroupCount);
        Assert.Equal(2, applyResult.MovedFileCount);
        Assert.True(File.Exists(Path.Combine(_rootDirectory, "defekt", Path.GetFileName(videoPath))));
        Assert.True(File.Exists(Path.Combine(_rootDirectory, "defekt", Path.GetFileName(textPath))));
        Assert.Contains(applyResult.LogLines, line => line.StartsWith("DEFEKT:", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_SkipsSidecarOnlyRequest_WhenHealthyLooseVideoStillExists()
    {
        var videoPath = Path.Combine(_rootDirectory, "Stralsund-Außer Kontrolle (S01_E02) (Audiodeskription)-1030257331.mp4");
        var textPath = Path.Combine(_rootDirectory, "Stralsund-Außer Kontrolle (S01_E02) (Audiodeskription)-1030257331.txt");
        CreateFileWithByteLength(videoPath, length: 100 * 1024 * 1024);
        CreateCompanionText(
            textPath,
            topic: "Stralsund",
            title: "Außer Kontrolle (S01/E02)",
            duration: "00:05:00");

        var applyResult = _service.Apply(
            _rootDirectory,
            [new DownloadSortMoveRequest("Stralsund-Außer Kontrolle", [textPath], "Stralsund")],
            []);

        Assert.Equal(0, applyResult.MovedGroupCount);
        Assert.Equal(0, applyResult.MovedFileCount);
        Assert.Equal(1, applyResult.SkippedGroupCount);
        Assert.True(File.Exists(videoPath));
        Assert.True(File.Exists(textPath));
        Assert.False(File.Exists(Path.Combine(_rootDirectory, "Stralsund", Path.GetFileName(textPath))));
        Assert.Contains(applyResult.LogLines, line => line.StartsWith("UEBERSPRUNGEN:", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_SkipsLanguageSuffixedSidecarOnlyRequest_WhenHealthyLooseVideoStillExists()
    {
        var videoPath = Path.Combine(_rootDirectory, "Stralsund-Außer Kontrolle (S01_E02)-1030257331.mp4");
        var subtitlePath = Path.Combine(_rootDirectory, "Stralsund-Außer Kontrolle (S01_E02)-1030257331.de.srt");
        CreateFileWithByteLength(videoPath, length: 100 * 1024 * 1024);
        CreateEmptyFile(subtitlePath);

        var applyResult = _service.Apply(
            _rootDirectory,
            [new DownloadSortMoveRequest("Stralsund-Außer Kontrolle", [subtitlePath], "Stralsund")],
            []);

        Assert.Equal(0, applyResult.MovedGroupCount);
        Assert.Equal(0, applyResult.MovedFileCount);
        Assert.Equal(1, applyResult.SkippedGroupCount);
        Assert.True(File.Exists(videoPath));
        Assert.True(File.Exists(subtitlePath));
        Assert.False(File.Exists(Path.Combine(_rootDirectory, "Stralsund", Path.GetFileName(subtitlePath))));
        Assert.Contains(applyResult.LogLines, line => line.StartsWith("UEBERSPRUNGEN:", StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateTarget_RejectsReservedDefectiveFolder_ForRegularFiles()
    {
        var videoPath = Path.Combine(_rootDirectory, "Serie-Folge-1234.mp4");
        CreateEmptyFile(videoPath);

        var evaluation = _service.EvaluateTarget(
            _rootDirectory,
            [videoPath],
            "defekt",
            []);

        Assert.Equal(DownloadSortItemState.NeedsReview, evaluation.State);
        Assert.Contains("reserviert", evaluation.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_SkipsRegularFiles_WhenTargetFolderIsReservedDefekt()
    {
        var videoPath = Path.Combine(_rootDirectory, "Serie-Folge-1234.mp4");
        CreateEmptyFile(videoPath);

        var applyResult = _service.Apply(
            _rootDirectory,
            [new DownloadSortMoveRequest("Serie-Folge", [videoPath], "defekt")],
            []);

        Assert.Equal(0, applyResult.MovedGroupCount);
        Assert.Equal(0, applyResult.MovedFileCount);
        Assert.Equal(1, applyResult.SkippedGroupCount);
        Assert.True(File.Exists(videoPath));
        Assert.False(File.Exists(Path.Combine(_rootDirectory, "defekt", Path.GetFileName(videoPath))));
        Assert.Contains(applyResult.LogLines, line => line.Contains("reserviert", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_SkipsRegularFiles_WhenTargetFolderIsReservedDone()
    {
        var videoPath = Path.Combine(_rootDirectory, "Serie-Folge-1234.mp4");
        CreateEmptyFile(videoPath);

        var applyResult = _service.Apply(
            _rootDirectory,
            [new DownloadSortMoveRequest("Serie-Folge", [videoPath], "done")],
            []);

        Assert.Equal(0, applyResult.MovedGroupCount);
        Assert.Equal(0, applyResult.MovedFileCount);
        Assert.Equal(1, applyResult.SkippedGroupCount);
        Assert.True(File.Exists(videoPath));
        Assert.False(File.Exists(Path.Combine(_rootDirectory, "done", Path.GetFileName(videoPath))));
        Assert.Contains(applyResult.LogLines, line => line.Contains("reserviert", StringComparison.Ordinal));
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
