using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;

var tests = new Action[]
{
    DetectFromMainVideo_KeepsHyphenInsideSeriesName,
    DetectFromMainVideo_CollectsMatchingSidecarFiles,
    MkvMergeOutputParser_ParsesGermanAndEnglishOutput
};

var executed = 0;

foreach (var test in tests)
{
    test();
    executed++;
}

Console.WriteLine($"Alle Tests erfolgreich: {executed}");

static void DetectFromMainVideo_KeepsHyphenInsideSeriesName()
{
    var testRoot = CreateTestDirectory();

    try
    {
        var mainVideoPath = Path.Combine(testRoot, "A-Team - Urlaub mit Hindernissen (S1_E2).mp4");
        File.WriteAllText(mainVideoPath, "video");

        var planner = CreatePlanner();
        var detected = planner.DetectFromMainVideo(mainVideoPath);

        AssertEqual("A-Team", detected.SeriesName, "Serienname mit Bindestrich");
        AssertEqual("Urlaub mit Hindernissen", detected.SuggestedTitle, "Titel bei Bindestrich-Serie");
        AssertEqual(
            "A-Team - S01E02 - Urlaub mit Hindernissen.mkv",
            Path.GetFileName(detected.SuggestedOutputFilePath),
            "Vorgeschlagener Dateiname");
    }
    finally
    {
        DeleteDirectory(testRoot);
    }
}

static void DetectFromMainVideo_CollectsMatchingSidecarFiles()
{
    var testRoot = CreateTestDirectory();

    try
    {
        var baseName = "Die Sendung - Finale (Staffel 2, Folge 7)";
        var mainVideoPath = Path.Combine(testRoot, $"{baseName}.mp4");
        File.WriteAllText(mainVideoPath, "video");
        File.WriteAllText(Path.Combine(testRoot, $"{baseName} (Audiodeskription).mp4"), "ad");
        File.WriteAllText(Path.Combine(testRoot, $"{baseName}.srt"), "srt");
        File.WriteAllText(Path.Combine(testRoot, $"{baseName}.ass"), "ass");
        File.WriteAllText(Path.Combine(testRoot, $"{baseName}.ttml"), "ttml");
        File.WriteAllText(Path.Combine(testRoot, $"{baseName}.txt"), "txt");

        var planner = CreatePlanner();
        var detected = planner.DetectFromMainVideo(mainVideoPath);

        AssertTrue(!string.IsNullOrWhiteSpace(detected.AudioDescriptionPath), "AD-Datei gefunden");
        AssertEqual(2, detected.SubtitlePaths.Count, "Untertitel ohne TTML");
        AssertTrue(detected.SubtitlePaths.All(path => Path.GetExtension(path) is ".srt" or ".ass"), "Nur unterstuetzte Untertitel");
        AssertTrue(detected.AttachmentPath?.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) == true, "TXT-Anhang gefunden");
    }
    finally
    {
        DeleteDirectory(testRoot);
    }
}

static void MkvMergeOutputParser_ParsesGermanAndEnglishOutput()
{
    var parser = new MkvMergeOutputParser();

    var germanProgress = parser.Parse("Fortschritt: 42%");
    var englishProgress = parser.Parse("Progress: 73%");
    var warning = parser.Parse("Warning: something happened");

    AssertEqual(42, germanProgress.ProgressPercent, "Deutscher Fortschritt");
    AssertEqual(73, englishProgress.ProgressPercent, "Englischer Fortschritt");
    AssertTrue(warning.IsWarning, "Warnung erkannt");
}

static SeriesEpisodeMuxPlanner CreatePlanner()
{
    var probeService = new MkvMergeProbeService();
    return new SeriesEpisodeMuxPlanner(new MkvToolNixLocator(), probeService, new SeriesArchiveService(probeService));
}

static string CreateTestDirectory()
{
    var testRoot = Path.Combine(
        AppContext.BaseDirectory,
        "_tmp_tests",
        Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(testRoot);
    return testRoot;
}

static void DeleteDirectory(string path)
{
    if (Directory.Exists(path))
    {
        Directory.Delete(path, recursive: true);
    }
}

static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: erwartet '{expected}', erhalten '{actual}'.");
    }
}

static void AssertTrue(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException($"{label}: Bedingung nicht erfuellt.");
    }
}
