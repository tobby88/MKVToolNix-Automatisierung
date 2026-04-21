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
    public ICommand RefreshAllComparisonsCommand { get; init; } = new NoOpRelayCommand();
    public string RefreshAllComparisonsTooltip { get; init; } = string.Empty;
    public ObservableCollection<DemoBatchEpisodeItem> EpisodeItemsView { get; init; } = [];
    public DemoBatchEpisodeItem? SelectedEpisodeItem { get; set; }
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
    public string Title { get; init; } = string.Empty;
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
    public string SeriesName { get; init; } = string.Empty;
    public string SeasonNumber { get; init; } = string.Empty;
    public string EpisodeNumber { get; init; } = string.Empty;
    public string TitleForMux { get; init; } = string.Empty;
    public string MetadataDisplayText { get; init; } = string.Empty;
    public string MetadataStatusText { get; init; } = string.Empty;
    public string MainVideoDisplayText { get; init; } = string.Empty;
    public string VideoAndAudioDescriptionDisplayText { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
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
}

internal sealed record DemoUsageGroup(
    string CurrentText,
    bool HasRemoved = false,
    string RemovedText = "",
    string RemovedReason = "")
{
    public static readonly DemoUsageGroup Empty = new("Keine Änderung");
}

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
    public ObservableCollection<DemoEmbyItem> Items { get; init; } = [];
    public DemoEmbyItem? SelectedItem { get; set; }
    public ICommand SelectReportCommand { get; init; } = new NoOpRelayCommand();
    public ICommand RunScanCommand { get; init; } = new NoOpRelayCommand();
    public string RunScanTooltip { get; init; } = string.Empty;
    public ICommand RunSyncCommand { get; init; } = new NoOpRelayCommand();
    public string RunSyncTooltip { get; init; } = string.Empty;
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
