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

    [Fact]
    public void BuildArguments_UsesConfiguredLanguageCodes_ForVideoAudioAndSubtitles()
    {
        var subtitlePath = CreateTempFile("subtitle-en.srt");
        var plan = new SeriesEpisodeMuxPlan(
            mkvMergePath: @"C:\Tools\mkvmerge.exe",
            outputFilePath: @"C:\Temp\output.mkv",
            title: "Pilot",
            videoSources:
            [
                new VideoSourcePlan(
                    @"C:\Temp\video.mkv",
                    0,
                    "English - FHD - H.264",
                    IsDefaultTrack: true,
                    LanguageCode: "en")
            ],
            audioSources:
            [
                new AudioSourcePlan(
                    @"C:\Temp\video.mkv",
                    1,
                    "English - AC-3",
                    IsDefaultTrack: true,
                    LanguageCode: "en")
            ],
            primarySourceAudioTrackIds: [1],
            primarySourceSubtitleTrackIds: [],
            primarySourceAttachmentIds: null,
            includePrimarySourceAttachments: false,
            attachmentSourcePath: null,
            attachmentSourceAttachmentIds: null,
            audioDescriptionFilePath: null,
            audioDescriptionTrackId: null,
            audioDescriptionTrackName: "Plattdüütsch (sehbehinderte) - AC-3",
            audioDescriptionLanguageCode: "nds",
            subtitleFiles:
            [
                new SubtitleFile(subtitlePath, new SubtitleKind("SRT", 1), LanguageCode: "en")
                {
                    Accessibility = SubtitleAccessibility.Standard
                }
            ],
            attachmentFilePaths: [],
            preservedAttachmentNames: [],
            usageComparison: ArchiveUsageComparison.Empty,
            workingCopy: null,
            notes: []);

        var arguments = plan.BuildArguments();

        AssertContainsSequence(arguments, "--language", "0:en");
        AssertContainsSequence(arguments, "--language", "1:en");
        Assert.Contains("0:English - SRT", arguments);
    }

    [Fact]
    public void BuildArguments_UsesMkvPropEditForDirectTrackHeaderEdits()
    {
        var plan = new SeriesEpisodeMuxPlan(
            mkvMergePath: @"C:\Tools\mkvmerge.exe",
            outputFilePath: @"C:\Temp\output.mkv",
            title: "Pilot",
            videoSources:
            [
                new VideoSourcePlan(@"C:\Temp\output.mkv", 0, "Deutsch - FHD - H.264", IsDefaultTrack: true)
            ],
            audioSources:
            [
                new AudioSourcePlan(@"C:\Temp\output.mkv", 1, "Deutsch - E-AC-3", IsDefaultTrack: true)
            ],
            primarySourceAudioTrackIds: [1],
            primarySourceSubtitleTrackIds: [],
            primarySourceAttachmentIds: null,
            includePrimarySourceAttachments: false,
            attachmentSourcePath: null,
            attachmentSourceAttachmentIds: null,
            audioDescriptionFilePath: null,
            audioDescriptionTrackId: null,
            audioDescriptionTrackName: null,
            audioDescriptionLanguageCode: null,
            subtitleFiles: [],
            attachmentFilePaths: [],
            preservedAttachmentNames: [],
            usageComparison: ArchiveUsageComparison.Empty,
            workingCopy: null,
            mkvPropEditPath: @"C:\Tools\mkvpropedit.exe",
            trackHeaderEdits:
            [
                new TrackHeaderEditOperation("track:2", "Audio 1", "Alter Audiotitel", "Deutsch - E-AC-3")
            ],
            notes: []);

        var arguments = plan.BuildArguments();

        Assert.Equal(@"C:\Temp\output.mkv", arguments[0]);
        AssertContainsSequence(arguments, "--edit", "track:2", "--set", "name=Deutsch - E-AC-3");
        Assert.Equal("mkvpropedit", plan.ExecutionToolDisplayName);
    }

    [Fact]
    public void BuildArguments_UsesMkvPropEditForDirectTrackHeaderValueEdits()
    {
        var plan = new SeriesEpisodeMuxPlan(
            mkvMergePath: @"C:\Tools\mkvmerge.exe",
            outputFilePath: @"C:\Temp\output.mkv",
            title: "Pilot",
            videoSources:
            [
                new VideoSourcePlan(@"C:\Temp\output.mkv", 0, "Deutsch - FHD - H.264", IsDefaultTrack: true)
            ],
            audioSources:
            [
                new AudioSourcePlan(@"C:\Temp\output.mkv", 1, "Deutsch - E-AC-3", IsDefaultTrack: true)
            ],
            primarySourceAudioTrackIds: [1],
            primarySourceSubtitleTrackIds: [],
            primarySourceAttachmentIds: null,
            includePrimarySourceAttachments: false,
            attachmentSourcePath: null,
            attachmentSourceAttachmentIds: null,
            audioDescriptionFilePath: null,
            audioDescriptionTrackId: null,
            audioDescriptionTrackName: null,
            audioDescriptionLanguageCode: null,
            subtitleFiles: [],
            attachmentFilePaths: [],
            preservedAttachmentNames: [],
            usageComparison: ArchiveUsageComparison.Empty,
            workingCopy: null,
            mkvPropEditPath: @"C:\Tools\mkvpropedit.exe",
            trackHeaderEdits:
            [
                new TrackHeaderEditOperation(
                    "track:3",
                    "Untertitel 2",
                    "Deutsch (hörgeschädigte) - SRT",
                    "Deutsch (hörgeschädigte) - SRT",
                    [
                        new TrackHeaderValueEdit(
                            "flag-hearing-impaired",
                            "Hörgeschädigt",
                            "nein",
                            "ja",
                            "1")
                    ])
            ],
            notes: []);

        var arguments = plan.BuildArguments();

        AssertContainsSequence(arguments, "--edit", "track:3", "--set", "flag-hearing-impaired=1");
    }

    [Fact]
    public void BuildArguments_UsesMkvPropEditForContainerTitleNormalization()
    {
        var plan = new SeriesEpisodeMuxPlan(
            mkvMergePath: @"C:\Tools\mkvmerge.exe",
            outputFilePath: @"C:\Temp\output.mkv",
            title: "Pilot",
            videoSources:
            [
                new VideoSourcePlan(@"C:\Temp\output.mkv", 0, "Deutsch - FHD - H.264", IsDefaultTrack: true)
            ],
            audioSources:
            [
                new AudioSourcePlan(@"C:\Temp\output.mkv", 1, "Deutsch - E-AC-3", IsDefaultTrack: true)
            ],
            primarySourceAudioTrackIds: [1],
            primarySourceSubtitleTrackIds: [],
            primarySourceAttachmentIds: null,
            includePrimarySourceAttachments: false,
            attachmentSourcePath: null,
            attachmentSourceAttachmentIds: null,
            audioDescriptionFilePath: null,
            audioDescriptionTrackId: null,
            audioDescriptionTrackName: null,
            audioDescriptionLanguageCode: null,
            subtitleFiles: [],
            attachmentFilePaths: [],
            preservedAttachmentNames: [],
            usageComparison: ArchiveUsageComparison.Empty,
            workingCopy: null,
            mkvPropEditPath: @"C:\Tools\mkvpropedit.exe",
            containerTitleEdit: new ContainerTitleEditOperation("Alter Titel", "Pilot"),
            notes: []);

        var arguments = plan.BuildArguments();

        Assert.Equal(@"C:\Temp\output.mkv", arguments[0]);
        AssertContainsSequence(arguments, "--edit", "info", "--set", "title=Pilot");
        Assert.True(plan.HasHeaderEdits);
        Assert.False(plan.HasTrackHeaderEdits);
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
            audioSources:
            [
                new AudioSourcePlan(@"C:\Temp\video.mkv", 1, "Deutsch - AC-3", IsDefaultTrack: true)
            ],
            primarySourceAudioTrackIds: [1],
            primarySourceSubtitleTrackIds: [],
            primarySourceAttachmentIds: null,
            includePrimarySourceAttachments: false,
            attachmentSourcePath: null,
            attachmentSourceAttachmentIds: null,
            audioDescriptionFilePath: null,
            audioDescriptionTrackId: null,
            audioDescriptionTrackName: null,
            audioDescriptionLanguageCode: null,
            subtitleFiles: subtitleFiles,
            attachmentFilePaths: [],
            preservedAttachmentNames: [],
            usageComparison: ArchiveUsageComparison.Empty,
            workingCopy: null,
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

    private static void AssertContainsSequence(IReadOnlyList<string> values, params string[] expectedSequence)
    {
        for (var index = 0; index <= values.Count - expectedSequence.Length; index++)
        {
            var matches = true;
            for (var offset = 0; offset < expectedSequence.Length; offset++)
            {
                if (!string.Equals(values[index + offset], expectedSequence[offset], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return;
            }
        }

        throw new Xunit.Sdk.XunitException($"Sequenz '{string.Join("', '", expectedSequence)}' wurde nicht gefunden.");
    }
}
