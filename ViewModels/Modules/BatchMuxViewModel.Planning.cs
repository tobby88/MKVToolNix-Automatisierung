using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

// Dieser Partial übersetzt sichtbare Batch-Zeilen in konkrete Mux-Pläne.
public sealed partial class BatchMuxViewModel
{
    private async Task RefreshComparisonPlansAsync(
        IReadOnlyList<BatchEpisodeItemViewModel> items,
        bool automatic)
    {
        if (items.Count == 0)
        {
            SetStatus(
                automatic
                    ? "Scan abgeschlossen - keine vorhandenen Bibliotheksdateien zum Vergleichen"
                    : "Keine vorhandenen Bibliotheksdateien ausgewählt",
                automatic ? 100 : ProgressValue);
            return;
        }

        try
        {
            SetBusy(true);

            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                item.SetStatus(BatchEpisodeStatusKind.Running);
                SetStatus(
                    automatic
                        ? $"Vergleiche vorhandene Bibliotheksdateien... {index + 1}/{items.Count}"
                        : $"Aktualisiere Vergleiche... {index + 1}/{items.Count}",
                    automatic
                        ? ScaleProgress(CalculatePercent(index + 1, items.Count), AutomaticCompareProgressStart, 100)
                        : CalculatePercent(index + 1, items.Count));

                await RefreshComparisonForItemAsync(item);
            }

            SetStatus(
                automatic
                    ? $"Scan und Zielvergleiche abgeschlossen ({items.Count} vorhandene Bibliotheksdatei(en) geprüft)"
                    : $"Vergleiche aktualisiert ({items.Count} vorhandene Bibliotheksdatei(en) geprüft)",
                100);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshComparisonForItemAsync(BatchEpisodeItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(item.MainVideoPath)
            || string.IsNullOrWhiteSpace(item.OutputPath)
            || string.IsNullOrWhiteSpace(item.TitleForMux))
        {
            item.SetPlanSummary(string.Empty);
            return;
        }

        item.RefreshArchivePresence();
        var outputExists = item.ArchiveState == EpisodeArchiveState.Existing;

        item.SetPlanSummary(outputExists
            ? "Zielvergleich wird berechnet..."
            : "Verwendungsplan wird berechnet...");
        item.SetUsageSummary(EpisodeUsageSummary.CreatePending(
            outputExists ? "Zielvergleich wird berechnet" : "Verwendungsplan wird berechnet",
            outputExists ? Path.GetFileName(item.OutputPath) : "Neue MKV wird erstellt"));

        try
        {
            var plan = await BuildPlanForItemAsync(item);
            item.SetPlanSummary(plan.BuildCompactSummaryText());
            item.SetUsageSummary(plan.BuildUsageSummary());

            if (outputExists)
            {
                item.SetStatus(plan.SkipMux ? BatchEpisodeStatusKind.UpToDate : BatchEpisodeStatusKind.Ready);
            }
            else
            {
                item.SetStatus(BatchEpisodeStatusKind.Ready);
            }
        }
        catch (Exception ex)
        {
            item.SetPlanSummary("Plan konnte noch nicht berechnet werden: " + ex.Message);
            item.SetUsageSummary(EpisodeUsageSummary.CreatePending("Plan konnte nicht berechnet werden", ex.Message));
            item.SetStatus(BatchEpisodeStatusKind.Warning);
        }
    }

    private async Task<SeriesEpisodeMuxPlan> BuildPlanForItemAsync(BatchEpisodeItemViewModel item)
    {
        return await _services.EpisodePlans.BuildPlanAsync(item);
    }

    private async Task<List<BatchExecutionWorkItem>> BuildExecutionWorkItemsAsync(
        IReadOnlyList<BatchEpisodeItemViewModel> readyItems,
        BatchRunProgressTracker progressTracker,
        Action<string> appendBatchRunLog)
    {
        var executablePlans = new List<BatchExecutionWorkItem>();

        for (var index = 0; index < readyItems.Count; index++)
        {
            var item = readyItems[index];
            progressTracker.ReportPlanning(index + 1, readyItems.Count);

            try
            {
                var plan = await BuildPlanForItemAsync(item);
                if (plan.SkipMux)
                {
                    item.SetStatus(BatchEpisodeStatusKind.UpToDate);
                    appendBatchRunLog($"SKIP: {item.MainVideoFileName} -> {plan.SkipReason}");
                    continue;
                }

                executablePlans.Add(new BatchExecutionWorkItem(item, plan, BuildBatchCleanupFileList(item, plan)));
            }
            catch (Exception ex)
            {
                item.SetStatus(BatchEpisodeStatusKind.Error);
                appendBatchRunLog($"PLAN-FEHLER: {item.MainVideoFileName} -> {ex.Message}");
            }
        }

        return executablePlans;
    }

    private string BuildOutputPath(AutoDetectedEpisodeFiles detected)
    {
        var fallbackDirectory = Path.GetDirectoryName(detected.MainVideoPath) ?? SourceDirectory;
        return BuildAutomaticOutputPath(
            fallbackDirectory,
            detected.SeriesName,
            detected.SeasonNumber,
            detected.EpisodeNumber,
            detected.SuggestedTitle);
    }

    private string BuildOutputPath(BatchEpisodeItemViewModel item)
    {
        var fallbackDirectory = Path.GetDirectoryName(item.MainVideoPath) ?? SourceDirectory;
        return BuildAutomaticOutputPath(
            fallbackDirectory,
            item.SeriesName,
            item.SeasonNumber,
            item.EpisodeNumber,
            item.TitleForMux);
    }

    private string BuildAutomaticOutputPath(
        string fallbackDirectory,
        string seriesName,
        string seasonNumber,
        string episodeNumber,
        string title)
    {
        return _services.OutputPaths.BuildOutputPath(
            fallbackDirectory,
            seriesName,
            seasonNumber,
            episodeNumber,
            title,
            OutputDirectory);
    }

    private static string GetPreferredSourceDirectory()
    {
        var downloadsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        var preferredDirectory = PreferredDownloadsSubPath.Aggregate(downloadsDirectory, Path.Combine);

        return Directory.Exists(preferredDirectory)
            ? preferredDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void RefreshAutomaticOutputPaths()
    {
        using (EpisodeItemsView.DeferRefresh())
        {
            foreach (var item in EpisodeItems)
            {
                RefreshAutomaticOutputPath(item);
            }
        }
    }

    private void RefreshAutomaticOutputPath(BatchEpisodeItemViewModel item)
    {
        if (!item.UsesAutomaticOutputPath)
        {
            return;
        }

        item.SetAutomaticOutputPath(BuildOutputPath(item));
    }

    private void ScheduleSelectedItemPlanSummaryRefresh()
    {
        _selectedPlanSummaryRefreshCts?.Cancel();
        _selectedPlanSummaryRefreshCts?.Dispose();

        var cancellationSource = new CancellationTokenSource();
        _selectedPlanSummaryRefreshCts = cancellationSource;

        _ = RefreshSelectedItemPlanSummaryDebouncedAsync(cancellationSource.Token);
    }

    private async Task RefreshSelectedItemPlanSummaryDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(200, cancellationToken);
            await RefreshSelectedItemPlanSummaryAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshSelectedItemPlanSummaryAsync()
    {
        var item = SelectedEpisodeItem;
        var version = Interlocked.Increment(ref _selectedPlanSummaryVersion);
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.MainVideoPath)
            || string.IsNullOrWhiteSpace(item.OutputPath)
            || string.IsNullOrWhiteSpace(item.TitleForMux))
        {
            item.SetPlanSummary(string.Empty);
            return;
        }

        try
        {
            await RefreshComparisonForItemAsync(item);
            if (version != _selectedPlanSummaryVersion || !ReferenceEquals(SelectedEpisodeItem, item))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            if (version != _selectedPlanSummaryVersion || !ReferenceEquals(SelectedEpisodeItem, item))
            {
                return;
            }

            item.SetPlanSummary("Plan konnte noch nicht berechnet werden: " + ex.Message);
            item.SetUsageSummary(EpisodeUsageSummary.CreatePending("Plan konnte nicht berechnet werden", ex.Message));
        }
    }

}
