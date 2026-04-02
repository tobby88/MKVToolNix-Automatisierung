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
            ResolveSelectedItemDirectory(item),
            string.IsNullOrWhiteSpace(item.OutputFileName) ? "Ausgabe.mkv" : item.OutputFileName);

        if (!string.IsNullOrWhiteSpace(path))
        {
            item.SetOutputPathWithContext(path, _services.OutputPaths.IsArchivePath(path));
            SetStatus("Ausgabedatei aktualisiert", ProgressValue);
            ScheduleSelectedItemPlanSummaryRefresh();
        }
    }

    private void OpenSelectedSources()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        _ = ReviewEpisodeAsync(item, isBatchPreparation: false);
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
        await RefreshComparisonPlansAsync(
            EpisodeItems.Where(item => item.HasArchiveComparisonTarget).ToList(),
            automatic: false);
    }

}
