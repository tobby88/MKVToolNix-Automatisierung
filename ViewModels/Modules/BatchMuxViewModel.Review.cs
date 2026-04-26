using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

// Dieser Partial enthält Sammelaktionen rund um Pflichtprüfungen und TVDB-Review im Batch.
internal sealed partial class BatchMuxViewModel
{
    private void EditSelectedAudioDescription()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        var choice = _dialogService.AskAudioDescriptionChoice();
        if (choice == MessageBoxResult.No)
        {
            item.SetAudioDescription(null);
            SetStatus("AD-Datei geleert", ProgressValue);
            RefreshCommands();
            ScheduleSelectedItemPlanSummaryRefresh();
            return;
        }

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var path = _dialogService.SelectAudioDescription(ResolveSelectedItemDirectory(item));
        if (!string.IsNullOrWhiteSpace(path))
        {
            item.SetAudioDescription(path);
            SetStatus("AD-Datei aktualisiert", ProgressValue);
            RefreshCommands();
            ScheduleSelectedItemPlanSummaryRefresh();
        }
    }

    private void EditSelectedSubtitles()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        var choice = _dialogService.AskSubtitlesChoice();
        if (choice == MessageBoxResult.No)
        {
            item.SetSubtitles([]);
            SetStatus("Untertitel geleert", ProgressValue);
            RefreshCommands();
            ScheduleSelectedItemPlanSummaryRefresh();
            return;
        }

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var paths = _dialogService.SelectSubtitles(ResolveSelectedItemDirectory(item));
        if (paths is not null)
        {
            item.SetSubtitles(paths);
            SetStatus("Untertitel aktualisiert", ProgressValue);
            RefreshCommands();
            ScheduleSelectedItemPlanSummaryRefresh();
        }
    }

    private void EditSelectedAttachments()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        var choice = _dialogService.AskAttachmentChoice();
        if (choice == MessageBoxResult.No)
        {
            item.SetAttachments([]);
            SetStatus("Anhänge geleert", ProgressValue);
            RefreshCommands();
            ScheduleSelectedItemPlanSummaryRefresh();
            return;
        }

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var paths = _dialogService.SelectAttachments(ResolveSelectedItemDirectory(item));
        if (paths is not null)
        {
            item.SetAttachments(paths);
            SetStatus("Anhänge aktualisiert", ProgressValue);
            RefreshCommands();
            ScheduleSelectedItemPlanSummaryRefresh();
        }
    }

    private void EditSelectedOutput()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        var path = _dialogService.SelectOutput(
            ResolveSelectedOutputDirectory(item),
            string.IsNullOrWhiteSpace(item.OutputFileName) ? "Ausgabe.mkv" : item.OutputFileName);

        if (!string.IsNullOrWhiteSpace(path))
        {
            item.SetOutputPathWithContext(path, _services.OutputPaths.IsArchivePath(path));
            RefreshOutputTargetCollisions(EpisodeItems);
            SetStatus("Ausgabedatei aktualisiert", ProgressValue);
            RefreshCommands();
            ScheduleSelectedItemPlanSummaryRefresh();
        }
    }

    private void ApproveSelectedPlanReview()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        item.ApprovePlanReview();
        item.RefreshArchivePresence();
        SetStatus("Fachlicher Hinweis freigegeben", ProgressValue);
        RefreshOverview();
        RefreshCommands();
        ScheduleSelectedItemPlanSummaryRefresh();
    }

    private async Task OpenSelectedSourcesAsync()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        OpenInspectableFiles(
            EnumerateVideoFiles(item),
            "Videoquellen geöffnet",
            "Videoquellen konnten nicht geöffnet werden");
        await Task.CompletedTask;
    }

    private void OpenSelectedAudioDescription()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        OpenInspectableFiles(
            [item.AudioDescriptionPath],
            "AD-Quelle geöffnet",
            "AD-Quelle konnte nicht geöffnet werden");
    }

    private void OpenSelectedSubtitles()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        OpenInspectableFiles(
            item.SubtitlePaths,
            "Untertitel geöffnet",
            "Untertitel konnten nicht geöffnet werden");
    }

    private void OpenSelectedAttachments()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        OpenInspectableFiles(
            item.AttachmentPaths,
            "Anhänge geöffnet",
            "Anhänge konnten nicht geöffnet werden");
    }

    private void OpenSelectedOutput()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        OpenInspectableFiles(
            [item.OutputPath],
            "Zieldatei geöffnet",
            "Zieldatei konnte nicht geöffnet werden");
    }

    private void OpenInspectableFiles(
        IEnumerable<string> filePaths,
        string successStatusText,
        string failedStatusText)
    {
        var opened = _dialogService.TryOpenFilesWithDefaultApp(filePaths);
        SetStatus(opened ? successStatusText : failedStatusText, ProgressValue);
    }

    private bool HasSelectedVideoFiles()
    {
        return SelectedEpisodeItem is not null
            && EnumerateVideoFiles(SelectedEpisodeItem).Any(path => !string.IsNullOrWhiteSpace(path));
    }

    private static IEnumerable<string> EnumerateVideoFiles(BatchEpisodeItemViewModel item)
    {
        var paths = item.HasPrimaryVideoSource
            ? new[] { item.MainVideoPath }.Concat(item.AdditionalVideoPaths)
            : item.AdditionalVideoPaths
                .Concat(string.IsNullOrWhiteSpace(item.AudioDescriptionPath)
                    ? Enumerable.Empty<string>()
                    : [item.AudioDescriptionPath]);

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private async Task ReviewSelectedMetadataAsync()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        await ReviewEpisodeMetadataAsync(item, isBatchPreparation: false);
    }

    private async Task RefreshAllComparisonsAsync()
    {
        var cancellationToken = CancellationToken.None;
        var operationStarted = false;
        try
        {
            cancellationToken = BeginBatchOperation(BatchOperationKind.Comparison);
            operationStarted = true;
            await RefreshComparisonPlansAsync(
                EpisodeItems.Where(item => item.HasArchiveComparisonTarget).ToList(),
                automatic: false,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppendLog("ABGEBROCHEN: Archivvergleich durch Benutzer abgebrochen.");
            SetStatus("Archivvergleich abgebrochen", ProgressValue);
        }
        finally
        {
            if (operationStarted)
            {
                CompleteBatchOperation(BatchOperationKind.Comparison);
            }
        }
    }

}
