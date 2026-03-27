using System.Reflection;
using System.Windows;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class SingleEpisodeMuxViewModelTests
{
    public SingleEpisodeMuxViewModelTests()
    {
        if (Application.Current is null)
        {
            _ = new Application();
        }
    }

    [Fact]
    public void ShouldPreserveManualTitle_ReturnsTrue_ForSameDetectionSeedPath()
    {
        var viewModel = CreateViewModel();
        SetPrivateField(viewModel, "_title", "Manueller Titel");
        SetPrivateField(viewModel, "_lastSuggestedTitle", "Automatischer Titel");
        SetPrivateField(viewModel, "_detectionSeedPath", @"C:\Temp\episode.mp4");

        var result = InvokeShouldPreserveManualTitle(viewModel, @"C:\Temp\episode.mp4");

        Assert.True(result);
    }

    [Fact]
    public void ShouldPreserveManualTitle_ReturnsFalse_ForNewSelection()
    {
        var viewModel = CreateViewModel();
        SetPrivateField(viewModel, "_title", "Manueller Titel");
        SetPrivateField(viewModel, "_lastSuggestedTitle", "Automatischer Titel");
        SetPrivateField(viewModel, "_detectionSeedPath", @"C:\Temp\episode-alt.mp4");
        SetPrivateField(viewModel, "_mainVideoPath", @"C:\Temp\episode-alt.mp4");

        var result = InvokeShouldPreserveManualTitle(viewModel, @"C:\Temp\episode-neu.mp4");

        Assert.False(result);
    }

    [Fact]
    public void ShouldPreserveManualTitle_ReturnsFalse_WhenTitleMatchesLastSuggestion()
    {
        var viewModel = CreateViewModel();
        SetPrivateField(viewModel, "_title", "Titel aus Erkennung");
        SetPrivateField(viewModel, "_lastSuggestedTitle", "Titel aus Erkennung");
        SetPrivateField(viewModel, "_detectionSeedPath", @"C:\Temp\episode.mp4");

        var result = InvokeShouldPreserveManualTitle(viewModel, @"C:\Temp\episode.mp4");

        Assert.False(result);
    }

    [Fact]
    public void AudioDescriptionButtonText_ReturnsCorrectionText_WhenMainVideoExists()
    {
        var viewModel = CreateViewModel();
        SetPrivateField(viewModel, "_mainVideoPath", @"C:\Temp\episode.mp4");

        Assert.Equal("AD korrigieren", viewModel.AudioDescriptionButtonText);
    }

    [Fact]
    public void SubtitleDisplayText_ReturnsOnlyFileNames()
    {
        var viewModel = CreateViewModel();
        SetPrivateField(viewModel, "_subtitlePaths", new List<string>
        {
            @"C:\Temp\untertitel-a.srt",
            @"D:\Andere\untertitel-b.ass"
        });

        Assert.Equal(
            "untertitel-a.srt" + Environment.NewLine + "untertitel-b.ass",
            viewModel.SubtitleDisplayText);
    }

    [Fact]
    public void AttachmentDisplayText_ReturnsOnlyFileNames()
    {
        var viewModel = CreateViewModel();
        SetPrivateField(viewModel, "_attachmentPaths", new List<string>
        {
            @"C:\Temp\infos-a.txt",
            @"D:\Andere\infos-b.txt"
        });

        Assert.Equal(
            "infos-a.txt" + Environment.NewLine + "infos-b.txt",
            viewModel.AttachmentDisplayText);
    }

    private static SingleEpisodeMuxViewModel CreateViewModel()
    {
        var metadataService = new EpisodeMetadataLookupService(
            new AppMetadataStore(new AppSettingsStore()),
            new TvdbClient());

        return new SingleEpisodeMuxViewModel(
            new AppServices(
                SeriesEpisodeMux: null!,
                EpisodePlans: null!,
                BatchScan: null!,
                Archive: null!,
                OutputPaths: null!,
                CleanupFiles: null!,
                EpisodeMetadata: metadataService,
                FileCopy: null!,
                Cleanup: null!,
                MuxWorkflow: null!,
                BatchLogs: null!),
            new UserDialogService());
    }

    private static bool InvokeShouldPreserveManualTitle(SingleEpisodeMuxViewModel viewModel, string selectedVideoPath)
    {
        var method = typeof(SingleEpisodeMuxViewModel).GetMethod(
            "ShouldPreserveManualTitle",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        return Assert.IsType<bool>(method!.Invoke(viewModel, [selectedVideoPath]));
    }

    private static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? target.GetType().BaseType?.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
