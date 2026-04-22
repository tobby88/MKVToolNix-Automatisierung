using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

// Dieser Partial verbindet das allgemeine Review-Workflow-Objekt mit den Batch-Zeilen.
internal sealed partial class BatchMuxViewModel
{
    /// <summary>
    /// Startet alle noch offenen Pflichtprüfungen für die aktuell ausgewählten Batch-Episoden.
    /// </summary>
    private async Task ReviewPendingSourcesAsync()
    {
        var selectedItems = EpisodeItems.Where(item => item.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Bitte zuerst mindestens eine Episode für den Batch auswählen.");
            return;
        }

        var readyItems = selectedItems
            .Where(item => !item.HasErrorStatus)
            .ToList();

        if (readyItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Es gibt keine gültigen Episoden für den Batch.");
            return;
        }

        var approved = await EnsurePendingChecksApprovedAsync(readyItems);
        if (approved)
        {
            _dialogService.ShowInfo("Hinweis", "Alle offenen Quellen-, TVDB- und Hinweisprüfungen wurden abgeschlossen.");
        }
    }

    /// <summary>
    /// Führt die manuelle Quellenprüfung für einen einzelnen Batch-Eintrag aus.
    /// </summary>
    private async Task<bool> ReviewEpisodeAsync(BatchEpisodeItemViewModel item, bool isBatchPreparation)
    {
        return await _reviewWorkflow.ReviewManualSourceAsync(
            item,
            SetStatus,
            ProgressValue,
            isBatchPreparation
                ? $"Prüfe Quelle für '{item.Title}'..."
                : "Prüfe Quelle...",
            "Quellenprüfung abgebrochen",
            isBatchPreparation
                ? $"Quellenprüfung für '{item.Title}' konnte nicht geöffnet werden"
                : "Quellenprüfung konnte nicht geöffnet werden",
            isBatchPreparation
                ? $"Quelle für '{item.Title}' freigegeben"
                : "Quelle freigegeben",
            isBatchPreparation
                ? $"Alternative Quelle für '{item.Title}' gewählt"
                : "Auf alternative Quelle umgestellt",
            tentativeExclusions => ApplyDetectionToItemAsync(item, item.DetectionSeedPath, tentativeExclusions));
    }

    /// <summary>
    /// Führt die TVDB-/Metadatenprüfung für einen einzelnen Batch-Eintrag aus und aktualisiert
    /// anschließend bei Bedarf Archivvergleich und Detaildarstellung.
    /// </summary>
    private async Task<bool> ReviewEpisodeMetadataAsync(BatchEpisodeItemViewModel item, bool isBatchPreparation)
    {
        // Die explizite Detailaktion im Batch soll den TVDB-Dialog immer wieder öffnen können.
        // Nur die automatische Pflichtprüfungs-Schleife filtert weiterhin separat auf offene Fälle.
        var episodeChanged = false;
        var outcome = await _reviewWorkflow.ReviewMetadataAsync(
            item,
            SetStatus,
            ProgressValue,
            isBatchPreparation
                ? $"Prüfe TVDB-Zuordnung für '{item.Title}'..."
                : "Prüfe TVDB-Zuordnung...",
            "TVDB-Prüfung abgebrochen",
            isBatchPreparation
                ? $"Lokale Erkennung für '{item.Title}' freigegeben"
                : "Lokale Erkennung freigegeben",
            isBatchPreparation
                ? $"TVDB-Zuordnung für '{item.Title}' freigegeben"
                : "TVDB-Zuordnung freigegeben",
            () =>
            {
                episodeChanged = true;
                // Eine ältere, bereits geplante Detailaktualisierung könnte noch mit dem alten
                // Episodencode oder Archivziel in die UI zurückschreiben. Vor dem neuen Vergleich
                // wird sie deshalb explizit verworfen.
                CancelSelectedItemPlanSummaryRefresh(invalidateInFlightRefreshes: true);
                _planCache.Invalidate(item);
                RefreshAutomaticOutputPath(item);
            });

        // Eine manuelle TVDB- oder lokale Metadatenkorrektur kann Zielpfad, Titel und damit auch
        // Archivhinweise verändern. Der Pflichtcheck darf danach nicht mit einer alten Vorschau
        // weiterlaufen, sonst verschwinden Hinweise wie "Mehrfachfolge prüfen" bis zur nächsten
        // manuellen Detailaktualisierung.
        if (outcome != EpisodeMetadataReviewOutcome.Cancelled && episodeChanged)
        {
            await RefreshComparisonForItemAsync(item, preserveCurrentPresentation: false);
        }
        else if (ReferenceEquals(SelectedEpisodeItem, item))
        {
            ScheduleSelectedItemPlanSummaryRefresh();
        }

        return outcome != EpisodeMetadataReviewOutcome.Cancelled;
    }

    /// <summary>
    /// Prüft, ob für mindestens einen ausgewählten Eintrag noch Pflichtprüfungen offen sind.
    /// </summary>
    private bool CanReviewPendingSources()
    {
        return !_isBusy && EpisodeItems.Any(item => item.IsSelected && item.HasPendingChecks);
    }

    /// <summary>
    /// Arbeitet Quellenprüfung, TVDB-Prüfung und fachliche Planhinweise der Reihe nach ab.
    /// Der Ablauf ist bewusst sequentiell, damit der Benutzer jeden Eintrag kontrolliert freigeben kann.
    /// </summary>
    private async Task<bool> EnsurePendingChecksApprovedAsync(
        IReadOnlyList<BatchEpisodeItemViewModel> readyItems,
        CancellationToken cancellationToken = default)
    {
        SetStatus("Pflichtprüfungen werden vorbereitet...", 0);
        var processedAnyChecks = false;
        while (true)
        {
            var pendingSourceItems = readyItems
                .Where(item => item.RequiresManualCheck && !item.IsManualCheckApproved)
                .ToList();
            if (pendingSourceItems.Count > 0)
            {
                processedAnyChecks = true;
                foreach (var item in pendingSourceItems)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SelectedEpisodeItem = item;
                    var approved = await ReviewEpisodeAsync(item, isBatchPreparation: true);
                    if (!approved)
                    {
                        return false;
                    }
                }

                continue;
            }

            var pendingMetadataItems = readyItems
                .Where(item => item.RequiresMetadataReview && !item.IsMetadataReviewApproved)
                .ToList();
            if (pendingMetadataItems.Count > 0)
            {
                processedAnyChecks = true;
                foreach (var item in pendingMetadataItems)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SelectedEpisodeItem = item;
                    var approved = await ReviewEpisodeMetadataAsync(item, isBatchPreparation: true);
                    if (!approved)
                    {
                        return false;
                    }
                }

                continue;
            }

            var pendingPlanReviewItems = readyItems
                .Where(item => item.HasPendingPlanReview)
                .ToList();
            if (pendingPlanReviewItems.Count > 0)
            {
                processedAnyChecks = true;
                foreach (var item in pendingPlanReviewItems)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SelectedEpisodeItem = item;
                    if (!_dialogService.ConfirmPlanReview(item.Title, item.PrimaryActionablePlanNote))
                    {
                        SetStatus("Hinweisprüfung abgebrochen", ProgressValue);
                        return false;
                    }

                    item.ApprovePlanReview();
                    item.RefreshArchivePresence();
                }

                RefreshOverview();
                RefreshCommands();
                SetStatus("Fachliche Hinweise freigegeben", ProgressValue);
                continue;
            }

            if (!processedAnyChecks)
            {
                SetStatus("Keine offenen Pflichtprüfungen", ProgressValue);
            }

            return true;
        }
    }

    /// <summary>
    /// Bestimmt das Startverzeichnis für "Ausgabe ändern" bevorzugt aus dem aktuell gewählten Zielpfad.
    /// </summary>
    private static string ResolveSelectedOutputDirectory(BatchEpisodeItemViewModel item)
    {
        var outputDirectory = Path.GetDirectoryName(item.OutputPath);
        var existingOutputDirectory = ResolveNearestExistingDirectory(outputDirectory);
        if (!string.IsNullOrWhiteSpace(existingOutputDirectory))
        {
            return existingOutputDirectory;
        }

        return ResolveSelectedItemDirectory(item);
    }

    /// <summary>
    /// Läuft von einem möglicherweise noch nicht existierenden Zielpfad nach oben bis zum ersten vorhandenen Ordner.
    /// </summary>
    private static string? ResolveNearestExistingDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        var directory = new DirectoryInfo(directoryPath);
        while (directory is not null)
        {
            if (directory.Exists)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>
    /// Fallback-Verzeichnis aus bekannten Quell- oder Zielpfaden des aktuell gewählten Eintrags.
    /// </summary>
    private static string ResolveSelectedItemDirectory(BatchEpisodeItemViewModel item)
    {
        var paths = item.SourceFilePaths
            .Concat([item.RequestedMainVideoPath, item.OutputPath])
            .Where(path => !string.IsNullOrWhiteSpace(path));

        foreach (var path in paths)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    /// <summary>
    /// Wählt alle sichtbaren oder optional alle Batch-Episoden aus, abhängig vom aktiven Filter und der Dialogentscheidung.
    /// </summary>
    private void SelectAllEpisodes()
    {
        var includeHiddenItems = SelectedFilterMode.Key != BatchEpisodeFilterMode.All
            && _dialogService.ConfirmApplyBatchSelectionToAllItems(selectItems: true);
        var changedCount = includeHiddenItems
            ? _episodeCollection.SelectAllItems()
            : _episodeCollection.SelectAllVisible();
        SetStatus(
            SelectedFilterMode.Key == BatchEpisodeFilterMode.All || includeHiddenItems
                ? $"Alle Episoden ausgewählt ({changedCount} geändert)"
                : $"Gefilterte Episoden ausgewählt ({changedCount} geändert)",
            ProgressValue);
    }

    /// <summary>
    /// Hebt die Auswahl aller sichtbaren oder optional aller Batch-Episoden auf.
    /// </summary>
    private void DeselectAllEpisodes()
    {
        var includeHiddenItems = SelectedFilterMode.Key != BatchEpisodeFilterMode.All
            && _dialogService.ConfirmApplyBatchSelectionToAllItems(selectItems: false);
        var changedCount = includeHiddenItems
            ? _episodeCollection.DeselectAllItems()
            : _episodeCollection.DeselectAllVisible();
        SetStatus(
            SelectedFilterMode.Key == BatchEpisodeFilterMode.All || includeHiddenItems
                ? $"Auswahl geleert ({changedCount} geändert)"
                : $"Gefilterte Auswahl geleert ({changedCount} geändert)",
            ProgressValue);
    }

    /// <summary>
    /// Schaltet die Auswahl des aktuell markierten Batch-Eintrags um.
    /// </summary>
    private void ToggleSelectedEpisodeSelection()
    {
        if (_isBusy || SelectedEpisodeItem is not BatchEpisodeItemViewModel item)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
    }

}
