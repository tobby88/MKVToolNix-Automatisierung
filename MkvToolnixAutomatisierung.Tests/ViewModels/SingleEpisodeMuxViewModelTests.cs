using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class SingleEpisodeMuxViewModelTests
{
    public SingleEpisodeMuxViewModelTests()
    {
        ViewModelTestContext.EnsureApplication();
    }

    [Fact]
    public void ShouldPreserveManualTitle_ReturnsTrue_ForSameDetectionSeedPath()
    {
        var result = SingleEpisodeManualTitlePolicy.ShouldPreserve(
            currentTitle: "Manueller Titel",
            lastSuggestedTitle: "Automatischer Titel",
            detectionSeedPath: @"C:\Temp\episode.mp4",
            mainVideoPath: @"C:\Temp\episode-alt.mp4",
            selectedVideoPath: @"C:\Temp\episode.mp4");

        Assert.True(result);
    }

    [Fact]
    public void ShouldPreserveManualTitle_ReturnsFalse_ForNewSelection()
    {
        var result = SingleEpisodeManualTitlePolicy.ShouldPreserve(
            currentTitle: "Manueller Titel",
            lastSuggestedTitle: "Automatischer Titel",
            detectionSeedPath: @"C:\Temp\episode-alt.mp4",
            mainVideoPath: @"C:\Temp\episode-alt.mp4",
            selectedVideoPath: @"C:\Temp\episode-neu.mp4");

        Assert.False(result);
    }

    [Fact]
    public void ShouldPreserveManualTitle_ReturnsFalse_WhenTitleMatchesLastSuggestion()
    {
        var result = SingleEpisodeManualTitlePolicy.ShouldPreserve(
            currentTitle: "Titel aus Erkennung",
            lastSuggestedTitle: "Titel aus Erkennung",
            detectionSeedPath: @"C:\Temp\episode.mp4",
            mainVideoPath: @"C:\Temp\episode.mp4",
            selectedVideoPath: @"C:\Temp\episode.mp4");

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
        viewModel.SetSubtitles(
        [
            @"C:\Temp\untertitel-a.srt",
            @"D:\Andere\untertitel-b.ass"
        ]);

        Assert.Equal(
            "untertitel-a.srt" + Environment.NewLine + "untertitel-b.ass",
            viewModel.SubtitleDisplayText);
    }

    [Fact]
    public void AttachmentDisplayText_ReturnsOnlyFileNames()
    {
        var viewModel = CreateViewModel();
        viewModel.SetAttachments(
        [
            @"C:\Temp\infos-a.txt",
            @"D:\Andere\infos-b.txt"
        ]);

        Assert.Equal(
            "infos-a.txt" + Environment.NewLine + "infos-b.txt",
            viewModel.AttachmentDisplayText);
    }

    private static SingleEpisodeMuxViewModel CreateViewModel()
    {
        return new SingleEpisodeMuxViewModel(
            ViewModelTestContext.CreateAppServices(),
            new UserDialogService());
    }

    private static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        var field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? target.GetType().BaseType?.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
