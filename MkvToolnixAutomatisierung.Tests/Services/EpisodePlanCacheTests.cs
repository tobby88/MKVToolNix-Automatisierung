using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class EpisodePlanCacheTests
{
    [Fact]
    public void TryGet_ReturnsStoredPlan_WhenInputsAreUnchanged()
    {
        var cache = new EpisodePlanCache();
        var owner = new object();
        var input = new StubPlanInput();
        var plan = CreatePlan("cached");

        cache.Store(owner, input, plan);

        var found = cache.TryGet(owner, input, out var cachedPlan);

        Assert.True(found);
        Assert.Same(plan, cachedPlan);
    }

    [Fact]
    public void TryGet_ReturnsFalse_WhenRelevantInputChanged()
    {
        var cache = new EpisodePlanCache();
        var owner = new object();
        var input = new StubPlanInput();
        cache.Store(owner, input, CreatePlan("cached"));

        input.TitleForMux = "Neuer Titel";

        var found = cache.TryGet(owner, input, out _);

        Assert.False(found);
    }

    [Fact]
    public void TryGet_TreatsEquivalentPathSetsAsSamePlan()
    {
        var cache = new EpisodePlanCache();
        var owner = new object();
        var initialInput = new StubPlanInput
        {
            SubtitlePaths = [@"C:\Temp\b.ass", @"C:\Temp\a.srt"],
            AttachmentPaths = [@"C:\Temp\meta-2.txt", @"C:\Temp\meta-1.txt"],
            ExcludedSourcePaths = [@"C:\Temp\source-2.mp4", @"C:\Temp\source-1.mp4"]
        };
        cache.Store(owner, initialInput, CreatePlan("cached"));

        var equivalentInput = new StubPlanInput
        {
            SubtitlePaths = [@"C:\Temp\a.srt", @"C:\Temp\b.ass"],
            AttachmentPaths = [@"C:\Temp\meta-1.txt", @"C:\Temp\meta-2.txt"],
            ExcludedSourcePaths = [@"C:\Temp\source-1.mp4", @"C:\Temp\source-2.mp4"]
        };

        var found = cache.TryGet(owner, equivalentInput, out _);

        Assert.True(found);
    }

    private static SeriesEpisodeMuxPlan CreatePlan(string title)
    {
        return SeriesEpisodeMuxPlan.CreateSkip(
            mkvMergePath: @"C:\Tools\mkvmerge.exe",
            outputFilePath: @"C:\Temp\output.mkv",
            title: title,
            skipReason: "skip",
            notes: []);
    }

    private sealed class StubPlanInput : IEpisodePlanInput
    {
        public string MainVideoPath { get; init; } = @"C:\Temp\episode.mp4";

        public string? AudioDescriptionPath { get; init; } = @"C:\Temp\episode-ad.mp4";

        public IReadOnlyList<string> SubtitlePaths { get; set; } = [@"C:\Temp\episode.srt"];

        public IReadOnlyList<string> AttachmentPaths { get; set; } = [@"C:\Temp\episode.txt"];

        public string OutputPath { get; init; } = @"C:\Temp\output.mkv";

        public string TitleForMux { get; set; } = "Pilot";

        public IReadOnlyCollection<string> ExcludedSourcePaths { get; set; } = [@"C:\Temp\alt.mp4"];
    }
}
