using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Verknüpft Arbeitskopie, Mux-Ausführung und temporäres Aufräumen zu einem robusten Einzellauf.
/// </summary>
public class MuxWorkflowCoordinator
{
    private readonly SeriesEpisodeMuxService _muxService;
    private readonly FileCopyService _fileCopyService;
    private readonly EpisodeCleanupService _cleanupService;

    public MuxWorkflowCoordinator(
        SeriesEpisodeMuxService muxService,
        FileCopyService fileCopyService,
        EpisodeCleanupService cleanupService)
    {
        _muxService = muxService;
        _fileCopyService = fileCopyService;
        _cleanupService = cleanupService;
    }

    /// <summary>
    /// Prüft, ob vor dem eigentlichen Mux-Lauf zunächst eine lokale Arbeitskopie angelegt oder aktualisiert werden muss.
    /// </summary>
    /// <param name="plan">Zu prüfender Mux-Plan.</param>
    /// <returns><see langword="true"/>, wenn die Arbeitskopie vorbereitet werden muss.</returns>
    public virtual bool NeedsWorkingCopyPreparation(SeriesEpisodeMuxPlan plan)
    {
        return plan.WorkingCopy is not null && _fileCopyService.NeedsCopy(plan.WorkingCopy);
    }

    /// <summary>
    /// Erstellt oder aktualisiert die im Plan beschriebene Arbeitskopie und meldet Fortschritt an den Aufrufer zurück.
    /// </summary>
    /// <param name="plan">Mux-Plan mit optionaler Arbeitskopie.</param>
    /// <param name="onUpdate">Optionaler Callback für Fortschrittsmeldungen der Vorbereitung.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    public virtual async Task PrepareWorkingCopyAsync(
        SeriesEpisodeMuxPlan plan,
        Action<WorkingCopyPreparationUpdate>? onUpdate = null,
        CancellationToken cancellationToken = default)
    {
        if (plan.WorkingCopy is null)
        {
            return;
        }

        if (!_fileCopyService.NeedsCopy(plan.WorkingCopy))
        {
            onUpdate?.Invoke(new WorkingCopyPreparationUpdate(100, ReusesExistingCopy: true));
            return;
        }

        onUpdate?.Invoke(new WorkingCopyPreparationUpdate(0, ReusesExistingCopy: false));

        await _fileCopyService.CopyAsync(
            plan.WorkingCopy,
            (copiedBytes, totalBytes) =>
            {
                var progress = totalBytes <= 0
                    ? 0
                    : (int)Math.Round(copiedBytes * 100d / totalBytes);
                onUpdate?.Invoke(new WorkingCopyPreparationUpdate(progress, ReusesExistingCopy: false));
            },
            cancellationToken);

        onUpdate?.Invoke(new WorkingCopyPreparationUpdate(100, ReusesExistingCopy: false));
    }

    /// <summary>
    /// Führt den eigentlichen Mux-Lauf aus, invalidiert danach Probe-Caches und entfernt temporäre Arbeitskopien.
    /// </summary>
    /// <param name="plan">Auszuführender Mux-Plan.</param>
    /// <param name="onOutput">Optionaler Callback für rohe Prozessausgabe.</param>
    /// <param name="onUpdate">Optionaler Callback für strukturierten Mux-Fortschritt.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Exitcode, Warnungsstatus und letzter bekannter Fortschritt.</returns>
    public virtual async Task<MuxExecutionResult> ExecuteMuxAsync(
        SeriesEpisodeMuxPlan plan,
        Action<string>? onOutput = null,
        Action<MuxExecutionUpdate>? onUpdate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _muxService.ExecuteAsync(plan, onOutput, onUpdate, cancellationToken);
        }
        finally
        {
            _muxService.InvalidatePlanOutputs(plan);
            _cleanupService.DeleteTemporaryFile(plan.WorkingCopy?.DestinationFilePath);
        }
    }
}

/// <summary>
/// Fortschrittsmeldung für die Vorbereitung einer lokalen Arbeitskopie.
/// </summary>
public sealed record WorkingCopyPreparationUpdate(int ProgressPercent, bool ReusesExistingCopy);
