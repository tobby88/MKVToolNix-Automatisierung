using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

// Dieser Partial übersetzt sichtbare Batch-Zeilen in konkrete Mux-Pläne.
internal sealed partial class BatchMuxViewModel
{
    private async Task RefreshComparisonPlansAsync(
        IReadOnlyList<BatchEpisodeItemViewModel> items,
        bool automatic,
        CancellationToken cancellationToken = default)
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
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[index];
                item.SetStatus(BatchEpisodeStatusKind.Running);
                SetStatus(
                    automatic
                        ? $"Vergleiche vorhandene Bibliotheksdateien... {index + 1}/{items.Count}"
                        : $"Aktualisiere Vergleiche... {index + 1}/{items.Count}",
                    automatic
                        // Die Anzeige soll abgeschlossene Vergleiche abbilden. Sonst erreicht die
                        // letzte Episode bereits vor Ende ihres Vergleichs kurzzeitig 100 %.
                        ? ScaleProgress(CalculatePercent(index, items.Count), AutomaticCompareProgressStart, 100)
                        : CalculatePercent(index, items.Count));

                try
                {
                    await RefreshComparisonForItemAsync(item, cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    item.RefreshArchivePresence();
                    throw;
                }
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

    private async Task RefreshComparisonForItemAsync(
        BatchEpisodeItemViewModel item,
        bool preserveCurrentPresentation = false,
        Func<bool>? shouldSkipPresentationUpdate = null,
        CancellationToken cancellationToken = default)
    {
        var comparisonInputVersion = item.ComparisonInputVersion;

        bool ShouldSkipPresentationUpdate()
        {
            return shouldSkipPresentationUpdate?.Invoke() == true
                || item.ComparisonInputVersion != comparisonInputVersion;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (ShouldSkipPresentationUpdate())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.MainVideoPath)
            || string.IsNullOrWhiteSpace(item.OutputPath)
            || string.IsNullOrWhiteSpace(item.TitleForMux))
        {
            if (ShouldSkipPresentationUpdate())
            {
                return;
            }

            item.SetPlanSummary(string.Empty);
            item.SetPlanNotes([]);
            return;
        }

        TryRedirectSupplementalOnlyItemToExistingArchive(item);

        if (preserveCurrentPresentation)
        {
            if (ShouldSkipPresentationUpdate())
            {
                return;
            }

            // Das Anklicken eines Eintrags soll die letzte gültige Anzeige beibehalten, bis die
            // aktualisierte Planung fertig vorliegt. Sonst springt der Status kurz zurück.
            item.RefreshArchivePresence(item.StatusKind);
        }
        else
        {
            if (ShouldSkipPresentationUpdate())
            {
                return;
            }

            item.RefreshArchivePresence();
        }

        var outputExists = item.ArchiveState == EpisodeArchiveState.Existing;
        var hasArchiveComparisonTarget = item.HasArchiveComparisonTarget;

        if (!preserveCurrentPresentation)
        {
            if (ShouldSkipPresentationUpdate())
            {
                return;
            }

            item.SetPlanSummary(hasArchiveComparisonTarget
                ? "Zielvergleich wird berechnet..."
                : "Verwendungsplan wird berechnet...");
            item.SetPlanNotes([]);
            item.SetUsageSummary(EpisodeUsageSummary.CreatePending(
                hasArchiveComparisonTarget ? "Zielvergleich wird berechnet" : "Verwendungsplan wird berechnet",
                hasArchiveComparisonTarget ? Path.GetFileName(item.OutputPath) : "Neue MKV wird erstellt"));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = await GetOrBuildPlanForItemAsync(item, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldSkipPresentationUpdate())
            {
                return;
            }

            item.SetPlanSummary(plan.BuildCompactSummaryText());
            item.SetPlanNotes(plan.Notes);
            item.SetUsageSummary(plan.BuildUsageSummary());

            var requiresPlanReview = item.HasActionablePlanNotes;

            if (requiresPlanReview)
            {
                item.SetStatus(BatchEpisodeStatusKind.ReviewPending);
            }
            else if (hasArchiveComparisonTarget)
            {
                item.SetStatus(plan.SkipMux ? BatchEpisodeStatusKind.UpToDate : BatchEpisodeStatusKind.Ready);
            }
            else
            {
                item.SetStatus(BatchEpisodeStatusKind.Ready);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ShouldSkipPresentationUpdate())
            {
                return;
            }

            item.SetPlanSummary("Plan konnte noch nicht berechnet werden: " + ex.Message);
            item.SetPlanNotes([]);
            item.SetUsageSummary(EpisodeUsageSummary.CreatePending("Plan konnte nicht berechnet werden", ex.Message));
            item.SetStatus(BatchEpisodeStatusKind.Warning);
        }
    }

    private async Task<SeriesEpisodeMuxPlan> GetOrBuildPlanForItemAsync(
        BatchEpisodeItemViewModel item,
        CancellationToken cancellationToken = default)
    {
        var cachedPlan = await _planCache.TryGetAsync(item, item, cancellationToken);
        if (cachedPlan is not null)
        {
            return cachedPlan;
        }

        var plan = await BuildFreshPlanForItemAsync(item, cancellationToken);
        await _planCache.StoreAsync(item, item, plan, cancellationToken);
        return plan;
    }

    private async Task<SeriesEpisodeMuxPlan> BuildFreshPlanForItemAsync(
        BatchEpisodeItemViewModel item,
        CancellationToken cancellationToken = default)
    {
        return await _services.EpisodePlans.BuildPlanAsync(item, cancellationToken);
    }

    private void TryRedirectSupplementalOnlyItemToExistingArchive(BatchEpisodeItemViewModel item)
    {
        if (item.HasPrimaryVideoSource
            || item.HasArchiveComparisonTarget
            || !item.UsesAutomaticOutputPath)
        {
            return;
        }

        var existingArchiveOutputPath = _services.OutputPaths.TryResolveExistingArchiveOutputPath(
            OutputDirectory,
            item.SeriesName,
            item.SeasonNumber,
            item.EpisodeNumber,
            item.TitleForMux);
        if (string.IsNullOrWhiteSpace(existingArchiveOutputPath)
            || PathComparisonHelper.AreSamePath(existingArchiveOutputPath, item.OutputPath))
        {
            return;
        }

        // Einträge mit nur Zusatzmaterial sollen einen bereits vorhandenen Bibliothekstreffer
        // wieder als Hauptquellenbasis nutzen können. Das korrigiert veraltete automatische
        // Batch-Ziele, ohne bewusst manuell gesetzte Custom-Ausgaben umzubiegen.
        item.SetAutomaticOutputPathWithContext(
            existingArchiveOutputPath,
            _services.OutputPaths.IsArchivePath(existingArchiveOutputPath));
        _planCache.Invalidate(item);
    }

    private async Task<List<BatchExecutionWorkItem>> BuildExecutionWorkItemsAsync(
        IReadOnlyList<BatchEpisodeItemViewModel> readyItems,
        BatchRunProgressTracker progressTracker,
        Action<string> appendBatchRunLog,
        CancellationToken cancellationToken = default)
    {
        var executablePlans = new List<BatchExecutionWorkItem>();

        for (var index = 0; index < readyItems.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = readyItems[index];
            progressTracker.ReportPlanning(index + 1, readyItems.Count);

            try
            {
                var plan = await GetOrBuildPlanForItemAsync(item, cancellationToken);
                var cleanupFiles = BuildBatchCleanupFileList(item, plan);
                if (plan.SkipMux)
                {
                    item.SetStatus(BatchEpisodeStatusKind.UpToDate);
                    appendBatchRunLog($"SKIP: {item.MainVideoFileName} -> {plan.SkipReason}");

                    if (cleanupFiles.Count == 0)
                    {
                        continue;
                    }

                    executablePlans.Add(new BatchExecutionWorkItem(item, plan, cleanupFiles));
                    continue;
                }

                executablePlans.Add(new BatchExecutionWorkItem(item, plan, cleanupFiles));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                item.SetStatus(BatchEpisodeStatusKind.Error);
                appendBatchRunLog($"PLAN-FEHLER: {item.MainVideoFileName} -> {ex.Message}");
            }
        }

        return executablePlans;
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
        return PreferredDownloadDirectoryHelper.GetPreferredMediathekDownloadsDirectory();
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

        var outputPath = BuildOutputPath(item);
        item.SetAutomaticOutputPathWithContext(outputPath, _services.OutputPaths.IsArchivePath(outputPath));
    }

    private void ScheduleSelectedItemPlanSummaryRefresh()
    {
        CancelSelectedItemPlanSummaryRefresh();
        if (_isSelectedItemPlanSummaryFrozen)
        {
            return;
        }

        _selectedPlanSummaryRefresh.Schedule(RefreshSelectedItemPlanSummaryAsync);
        SelectedItemPlanSummaryRefreshTask = _selectedPlanSummaryRefresh.CurrentTask;
    }

    private async Task RefreshSelectedItemPlanSummaryAsync(int version, CancellationToken cancellationToken)
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.MainVideoPath)
            || string.IsNullOrWhiteSpace(item.OutputPath)
            || string.IsNullOrWhiteSpace(item.TitleForMux))
        {
            item.SetPlanSummary(string.Empty);
            item.SetPlanNotes([]);
            return;
        }

        bool ShouldSkipPresentationUpdate()
        {
            return _isSelectedItemPlanSummaryFrozen
                || !_selectedPlanSummaryRefresh.IsCurrent(version)
                || !ReferenceEquals(SelectedEpisodeItem, item);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RefreshComparisonForItemAsync(
                item,
                preserveCurrentPresentation: true,
                shouldSkipPresentationUpdate: ShouldSkipPresentationUpdate,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || ShouldSkipPresentationUpdate())
        {
        }
        catch (Exception ex)
        {
            if (ShouldSkipPresentationUpdate())
            {
                return;
            }

            item.SetPlanSummary("Plan konnte noch nicht berechnet werden: " + ex.Message);
            item.SetPlanNotes([]);
            item.SetUsageSummary(EpisodeUsageSummary.CreatePending("Plan konnte nicht berechnet werden", ex.Message));
        }
    }

    /// <summary>
    /// Bricht geplante oder laufende Detail-Refreshes für den ausgewählten Batch-Eintrag ab.
    /// Auf Wunsch wird zusätzlich die laufende Versionsnummer angehoben, damit Ergebnisse
    /// bereits gestarteter Hintergrundberechnungen beim Abschluss nicht mehr in die UI
    /// zurückgeschrieben werden.
    /// </summary>
    /// <param name="invalidateInFlightRefreshes">
    /// <see langword="true"/>, wenn auch bereits gestartete Refreshes ihre Ergebnisse verwerfen sollen.
    /// </param>
    private void CancelSelectedItemPlanSummaryRefresh(bool invalidateInFlightRefreshes = false)
    {
        _selectedPlanSummaryRefresh.Cancel(invalidateInFlightRefreshes);
        SelectedItemPlanSummaryRefreshTask = null;
    }

}
