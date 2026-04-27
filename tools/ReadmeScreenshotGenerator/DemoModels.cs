using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;

namespace ReadmeScreenshotGenerator;

internal sealed class NoOpRelayCommand : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
    }
}

internal sealed record DemoChoice(string Title, string Description = "")
{
    public string DisplayName => Title;
}

internal sealed record DemoLanguageOption(string Code, string DisplayName);

internal sealed class DemoSingleViewModel
{
    public string MainVideoPath { get; init; } = string.Empty;
    public bool HasPlanSummary { get; init; }
    public Brush OutputTargetBadgeBackground { get; init; } = Brushes.Transparent;
    public Brush OutputTargetBadgeBorderBrush { get; init; } = Brushes.Transparent;
    public string OutputTargetBadgeText { get; init; } = string.Empty;
    public string OutputTargetBadgeTooltip { get; init; } = string.Empty;
    public Brush ManualCheckBadgeBackground { get; init; } = Brushes.Transparent;
    public Brush ManualCheckBadgeBorderBrush { get; init; } = Brushes.Transparent;
    public string ManualCheckBadgeText { get; init; } = string.Empty;
    public string ManualCheckBadgeTooltip { get; init; } = string.Empty;
    public Brush MetadataBadgeBackground { get; init; } = Brushes.Transparent;
    public Brush MetadataBadgeBorderBrush { get; init; } = Brushes.Transparent;
    public string MetadataBadgeText { get; init; } = string.Empty;
    public string MetadataBadgeTooltip { get; init; } = string.Empty;
    public DemoEpisodeUsageSummary? UsageSummary { get; init; }
    public bool HasPendingPlanReview { get; init; }
    public string PrimaryActionablePlanNote { get; init; } = string.Empty;
    public bool HasPlanRefreshProblem { get; init; }
    public string PlanRefreshProblemText { get; init; } = string.Empty;
    public bool RequiresManualCheck { get; init; }
    public string ManualCheckText { get; init; } = string.Empty;
    public string ManualCheckButtonText { get; init; } = string.Empty;
    public string ManualCheckButtonTooltip { get; init; } = string.Empty;
    public string SeriesName { get; set; } = string.Empty;
    public string SeasonNumber { get; set; } = string.Empty;
    public string EpisodeNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool HasMetadataStatus { get; init; }
    public string MetadataStatusText { get; init; } = string.Empty;
    public string MetadataActionButtonText { get; init; } = string.Empty;
    public string MetadataActionButtonTooltip { get; init; } = string.Empty;
    public string AudioDescriptionPath { get; init; } = string.Empty;
    public string AudioDescriptionButtonText { get; init; } = string.Empty;
    public ObservableCollection<string> SubtitlePaths { get; init; } = [];
    public ObservableCollection<string> AttachmentPaths { get; init; } = [];
    public string OutputPath { get; init; } = string.Empty;
    public ObservableCollection<DemoLanguageOption> LanguageOverrideOptions { get; init; } = [];
    public string? VideoLanguageOverride { get; set; }
    public string? AudioLanguageOverride { get; set; }
    public string? OriginalLanguageOverride { get; set; }
    public bool HasOutputTargetStatus { get; init; }
    public string OutputTargetStatusText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public int ProgressValue { get; init; }
    public string PreviewText { get; init; } = string.Empty;
    public ICommand SelectMainVideoCommand { get; init; } = new NoOpRelayCommand();
    public ICommand OpenMainVideoCommand { get; init; } = new NoOpRelayCommand();
    public ICommand ApprovePlanReviewCommand { get; init; } = new NoOpRelayCommand();
    public ICommand TestSelectedSourcesCommand { get; init; } = new NoOpRelayCommand();
    public ICommand OpenTvdbLookupCommand { get; init; } = new NoOpRelayCommand();
    public ICommand RescanCommand { get; init; } = new NoOpRelayCommand();
    public ICommand SelectAudioDescriptionCommand { get; init; } = new NoOpRelayCommand();
    public ICommand OpenAudioDescriptionCommand { get; init; } = new NoOpRelayCommand();
    public ICommand SelectSubtitlesCommand { get; init; } = new NoOpRelayCommand();
    public ICommand OpenSubtitlesCommand { get; init; } = new NoOpRelayCommand();
    public ICommand SelectAttachmentCommand { get; init; } = new NoOpRelayCommand();
    public ICommand OpenAttachmentsCommand { get; init; } = new NoOpRelayCommand();
    public ICommand SelectOutputCommand { get; init; } = new NoOpRelayCommand();
    public ICommand OpenOutputCommand { get; init; } = new NoOpRelayCommand();
    public ICommand CreatePreviewCommand { get; init; } = new NoOpRelayCommand();
    public string CreatePreviewButtonTooltip { get; init; } = string.Empty;
    public ICommand CancelCurrentOperationCommand { get; init; } = new NoOpRelayCommand();
    public string CancelCurrentOperationTooltip { get; init; } = string.Empty;
    public ICommand ExecuteMuxCommand { get; init; } = new NoOpRelayCommand();
    public string ExecuteMuxButtonTooltip { get; init; } = string.Empty;
}

internal sealed class DemoBatchViewModel
{
    public bool IsInteractive { get; init; }
    public string SourceDirectory { get; init; } = string.Empty;
    public string OutputDirectory { get; init; } = string.Empty;
    public bool HasOutputDirectoryHint { get; init; }
    public string OutputDirectoryHintText { get; init; } = string.Empty;
    public ObservableCollection<DemoChoice> FilterModes { get; init; } = [];
    public DemoChoice? SelectedFilterMode { get; set; }
    public ObservableCollection<DemoChoice> SortModes { get; init; } = [];
    public DemoChoice? SelectedSortMode { get; set; }
    public ICommand SelectSourceDirectoryCommand { get; init; } = new NoOpRelayCommand();
    public ICommand SelectOutputDirectoryCommand { get; init; } = new NoOpRelayCommand();
    public ICommand RefreshAllComparisonsCommand { get; init; } = new NoOpRelayCommand();
    public string RefreshAllComparisonsTooltip { get; init; } = string.Empty;
    public ObservableCollection<DemoBatchEpisodeItem> EpisodeItemsView { get; init; } = [];
    public DemoBatchEpisodeItem? SelectedEpisodeItem { get; set; }
    public ICommand ApproveSelectedPlanReviewCommand { get; init; } = new NoOpRelayCommand();
    public ICommand ReviewSelectedMetadataCommand { get; init; } = new NoOpRelayCommand();
    public string ReviewSelectedMetadataTooltip { get; init; } = string.Empty;
    public ICommand OpenSelectedSourcesCommand { get; init; } = new NoOpRelayCommand();
    public string OpenSelectedSourcesTooltip { get; init; } = string.Empty;
    public ICommand RedetectSelectedEpisodeCommand { get; init; } = new NoOpRelayCommand();
    public string RedetectSelectedEpisodeTooltip { get; init; } = string.Empty;
    public ICommand EditSelectedAudioDescriptionCommand { get; init; } = new NoOpRelayCommand();
    public ICommand OpenSelectedAudioDescriptionCommand { get; init; } = new NoOpRelayCommand();
    public ICommand EditSelectedSubtitlesCommand { get; init; } = new NoOpRelayCommand();
    public ICommand OpenSelectedSubtitlesCommand { get; init; } = new NoOpRelayCommand();
    public ICommand EditSelectedAttachmentsCommand { get; init; } = new NoOpRelayCommand();
    public ICommand OpenSelectedAttachmentsCommand { get; init; } = new NoOpRelayCommand();
    public ICommand EditSelectedOutputCommand { get; init; } = new NoOpRelayCommand();
    public ICommand OpenSelectedOutputCommand { get; init; } = new NoOpRelayCommand();
    public string BatchLogInfoText { get; init; } = string.Empty;
    public string LogText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public int ProgressValue { get; init; }
    public ICommand CancelBatchOperationCommand { get; init; } = new NoOpRelayCommand();
    public string CancelBatchOperationTooltip { get; init; } = string.Empty;
    public string CancelBatchOperationText { get; init; } = string.Empty;
    public bool CanCancelBatchOperation { get; init; }
    public ICommand ScanDirectoryCommand { get; init; } = new NoOpRelayCommand();
    public string ScanDirectoryTooltip { get; init; } = string.Empty;
    public ICommand SelectAllEpisodesCommand { get; init; } = new NoOpRelayCommand();
    public ICommand DeselectAllEpisodesCommand { get; init; } = new NoOpRelayCommand();
    public ICommand ReviewPendingSourcesCommand { get; init; } = new NoOpRelayCommand();
    public string ReviewPendingSourcesTooltip { get; init; } = string.Empty;
    public ICommand RunBatchCommand { get; init; } = new NoOpRelayCommand();
    public string RunBatchTooltip { get; init; } = string.Empty;
}

internal sealed class DemoBatchEpisodeItem
{
    public bool IsSelected { get; set; }
    public string Title { get; set; } = string.Empty;
    public string EpisodeCodeDisplayText { get; init; } = string.Empty;
    public string ArchiveStateText { get; init; } = string.Empty;
    public string ArchiveStateTooltip { get; init; } = string.Empty;
    public Brush ArchiveBadgeBackground { get; init; } = Brushes.Transparent;
    public Brush ArchiveBadgeBorderBrush { get; init; } = Brushes.Transparent;
    public string ReviewHint { get; init; } = string.Empty;
    public string ReviewHintTooltip { get; init; } = string.Empty;
    public Brush ReviewBadgeBackground { get; init; } = Brushes.Transparent;
    public Brush ReviewBadgeBorderBrush { get; init; } = Brushes.Transparent;
    public string Status { get; init; } = string.Empty;
    public string StatusTooltip { get; init; } = string.Empty;
    public Brush StatusBadgeBackground { get; init; } = Brushes.Transparent;
    public Brush StatusBadgeBorderBrush { get; init; } = Brushes.Transparent;
    public string MainVideoFileName { get; init; } = string.Empty;
    public string SeriesName { get; set; } = string.Empty;
    public string SeasonNumber { get; set; } = string.Empty;
    public string EpisodeNumber { get; set; } = string.Empty;
    public string TitleForMux { get; set; } = string.Empty;
    public string MetadataDisplayText { get; init; } = string.Empty;
    public string MetadataStatusText { get; init; } = string.Empty;
    public string MainVideoDisplayText { get; init; } = string.Empty;
    public string VideoAndAudioDescriptionDisplayText { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public ObservableCollection<DemoLanguageOption> LanguageOverrideOptions { get; init; } = [];
    public string? VideoLanguageOverride { get; set; }
    public string? AudioLanguageOverride { get; set; }
    public string? OriginalLanguageOverride { get; set; }
    public string NotesDisplayText { get; init; } = string.Empty;
    public bool HasNotes { get; init; }
    public bool HasActionablePlanNotes { get; init; }
    public string PrimaryActionablePlanNote { get; init; } = string.Empty;
    public IReadOnlyList<string> RequestedSourcePaths { get; init; } = [];
    public IReadOnlyList<string> SubtitlePaths { get; init; } = [];
    public IReadOnlyList<string> AttachmentPaths { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
    public DemoEpisodeUsageSummary? UsageSummary { get; init; }
}

internal sealed class DemoEpisodeUsageSummary
{
    public string ArchiveAction { get; init; } = string.Empty;
    public string ArchiveDetails { get; init; } = string.Empty;
    public DemoUsageGroup MainVideo { get; init; } = DemoUsageGroup.Empty;
    public DemoUsageGroup AdditionalVideos { get; init; } = DemoUsageGroup.Empty;
    public DemoUsageGroup Audio { get; init; } = DemoUsageGroup.Empty;
    public DemoUsageGroup AudioDescription { get; init; } = DemoUsageGroup.Empty;
    public DemoUsageGroup Subtitles { get; init; } = DemoUsageGroup.Empty;
    public DemoUsageGroup Attachments { get; init; } = DemoUsageGroup.Empty;
    public IReadOnlyList<string> Notes { get; init; } = [];
    public bool HasNotes => Notes.Count > 0;
}

internal sealed record DemoUsageGroup(
    string CurrentText,
    bool HasRemoved = false,
    string RemovedText = "",
    string RemovedReason = "")
{
    public static readonly DemoUsageGroup Empty = new("Keine Änderung");

    public IReadOnlyList<DemoUsageItem> CurrentItems => [new(CurrentText, "Neutral")];
}

internal sealed record DemoUsageItem(string Text, string KindName);

internal sealed class DemoDownloadSortViewModel
{
    public bool IsInteractive { get; init; }
    public string SourceDirectory { get; init; } = string.Empty;
    public string SummaryText { get; init; } = string.Empty;
    public ObservableCollection<DemoDownloadItem> Items { get; init; } = [];
    public DemoDownloadItem? SelectedItem { get; set; }
    public ObservableCollection<string> TargetFolderOptions { get; init; } = [];
    public ICommand SelectSourceDirectoryCommand { get; init; } = new NoOpRelayCommand();
    public ICommand ScanCommand { get; init; } = new NoOpRelayCommand();
    public string ScanTooltip { get; init; } = string.Empty;
    public ICommand SelectAllSortableCommand { get; init; } = new NoOpRelayCommand();
    public ICommand DeselectAllCommand { get; init; } = new NoOpRelayCommand();
    public ICommand ApplyTargetFolderToMatchingItemsCommand { get; init; } = new NoOpRelayCommand();
    public ICommand RunSortCommand { get; init; } = new NoOpRelayCommand();
    public string RunSortTooltip { get; init; } = string.Empty;
    public string LogText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public int ProgressValue { get; init; }
}

internal sealed class DemoDownloadItem
{
    public bool IsSelected { get; set; }
    public string DisplayName { get; init; } = string.Empty;
    public string TargetFolderName { get; set; } = string.Empty;
    public Brush StatusBadgeBackground { get; init; } = Brushes.Transparent;
    public Brush StatusBadgeBorderBrush { get; init; } = Brushes.Transparent;
    public string StatusText { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
}

internal sealed class DemoEmbySyncViewModel
{
    public bool IsInteractive { get; init; }
    public string ReportPath { get; init; } = string.Empty;
    public string ReportSelectionSummaryText { get; init; } = string.Empty;
    public string ReportSelectionDetailText { get; init; } = string.Empty;
    public string ReportSelectionTooltip { get; init; } = string.Empty;
    public ObservableCollection<DemoEmbyItem> Items { get; init; } = [];
    public DemoEmbyItem? SelectedItem { get; set; }
    public ICommand SelectReportCommand { get; init; } = new NoOpRelayCommand();
    public ICommand RunScanCommand { get; init; } = new NoOpRelayCommand();
    public string RunScanTooltip { get; init; } = string.Empty;
    public ICommand RunSyncCommand { get; init; } = new NoOpRelayCommand();
    public string RunSyncTooltip { get; init; } = string.Empty;
    public ICommand ReviewPendingProviderIdsCommand { get; init; } = new NoOpRelayCommand();
    public string ReviewPendingProviderIdsTooltip { get; init; } = string.Empty;
    public ICommand SelectAllCommand { get; init; } = new NoOpRelayCommand();
    public ICommand DeselectAllCommand { get; init; } = new NoOpRelayCommand();
    public string LogText { get; init; } = string.Empty;
    public string SummaryText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public int ProgressValue { get; init; }
}

internal sealed class DemoEmbyItem
{
    public bool IsSelected { get; set; }
    public string MediaFileName { get; init; } = string.Empty;
    public string TvdbId { get; set; } = string.Empty;
    public string ImdbId { get; set; } = string.Empty;
    public bool CanEditProviderIds { get; init; }
    public string ProviderIdEditTooltip { get; init; } = string.Empty;
    public bool CanReviewTvdb { get; init; }
    public string TvdbLookupTooltip { get; init; } = string.Empty;
    public bool CanReviewImdb { get; init; }
    public string ImdbLookupTooltip { get; init; } = string.Empty;
    public string StatusTone { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string StatusTooltip { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
}
