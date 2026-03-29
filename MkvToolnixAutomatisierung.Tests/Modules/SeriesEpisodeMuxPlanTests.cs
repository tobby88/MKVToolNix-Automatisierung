using System.IO;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Modules;

public sealed class SeriesEpisodeMuxPlanTests
{
    [Fact]
    public void BuildArguments_UsesHearingImpairedFlags_ForDefaultExternalSubtitles()
    {
        var subtitlePath = CreateTempFile("subtitle-hi.srt");
        var plan = CreatePlan(
        [
            new SubtitleFile(subtitlePath, new SubtitleKind("SRT", 1))
        ]);

        var arguments = plan.BuildArguments();

        Assert.Contains("0:Deutsch (hörgeschädigte) - SRT", arguments);
        AssertContainsSequence(arguments, "--hearing-impaired-flag", "0:yes");
    }

    [Fact]
    public void BuildArguments_UsesStandardFlags_WhenSubtitleAccessibilityWasDetected()
    {
        var subtitlePath = CreateTempFile("subtitle-standard.srt");
        var plan = CreatePlan(
        [
            new SubtitleFile(subtitlePath, new SubtitleKind("SRT", 1))
            {
                Accessibility = SubtitleAccessibility.Standard
            }
        ]);

        var arguments = plan.BuildArguments();

        Assert.Contains("0:Deutsch - SRT", arguments);
        AssertContainsSequence(arguments, "--hearing-impaired-flag", "0:no");
    }

    private static SeriesEpisodeMuxPlan CreatePlan(IReadOnlyList<SubtitleFile> subtitleFiles)
    {
        return new SeriesEpisodeMuxPlan(
            mkvMergePath: @"C:\Tools\mkvmerge.exe",
            outputFilePath: @"C:\Temp\output.mkv",
            title: "Pilot",
            videoSources:
            [
                new VideoSourcePlan(@"C:\Temp\video.mkv", 0, "Deutsch - FHD - H.264", IsDefaultTrack: true)
            ],
            primaryAudioFilePath: @"C:\Temp\video.mkv",
            primaryAudioTrackId: 1,
            primarySourceAudioTrackIds: [1],
            primarySourceSubtitleTrackIds: [],
            includePrimarySourceAttachments: false,
            attachmentSourcePath: null,
            audioDescriptionFilePath: null,
            audioDescriptionTrackId: null,
            subtitleFiles: subtitleFiles,
            attachmentFilePaths: [],
            preservedAttachmentNames: [],
            workingCopy: null,
            metadata: new EpisodeTrackMetadata("Deutsch - AC-3", "Deutsch (sehbehinderte) - AC-3"),
            notes: []);
    }

    private static string CreateTempFile(string fileName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-plan-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "subtitle");
        return path;
    }

    private static void AssertContainsSequence(IReadOnlyList<string> values, string expectedKey, string expectedValue)
    {
        for (var index = 0; index < values.Count - 1; index++)
        {
            if (values[index] == expectedKey && values[index + 1] == expectedValue)
            {
                return;
            }
        }

        throw new Xunit.Sdk.XunitException($"Sequenz '{expectedKey}', '{expectedValue}' wurde nicht gefunden.");
    }
}
