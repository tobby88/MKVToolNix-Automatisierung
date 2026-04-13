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
    public void Scan_MarksCandidateAsReady_WhenTargetAlreadyContainsComparableSameFile()
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

        Assert.Equal(DownloadSortItemState.Ready, item.State);
        Assert.Contains("wird ersetzt", item.Note, StringComparison.Ordinal);
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

    private static void CreateCompanionText(string path, string topic, string title)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            string.Join(
                Environment.NewLine,
                $"Thema:       {topic}",
                string.Empty,
                $"Titel:       {title}",
                string.Empty,
                "Dauer:       01:28:34"));
    }
}
