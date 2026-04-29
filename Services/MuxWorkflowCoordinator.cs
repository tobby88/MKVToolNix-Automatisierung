using System.IO;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Verknüpft Arbeitskopie, MKVToolNix-Ausführung und temporäres Aufräumen zu einem robusten Einzellauf.
/// </summary>
internal interface IMuxWorkflowCoordinator
{
    /// <summary>
    /// Prüft, ob vor dem Muxen eine Arbeitskopie vorbereitet werden muss.
    /// </summary>
    bool NeedsWorkingCopyPreparation(SeriesEpisodeMuxPlan plan);

    /// <summary>
    /// Erstellt oder aktualisiert die Arbeitskopie des Plans.
    /// </summary>
    Task PrepareWorkingCopyAsync(
        SeriesEpisodeMuxPlan plan,
        Action<WorkingCopyPreparationUpdate>? onUpdate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Führt den zum Plan passenden MKVToolNix-Lauf aus.
    /// </summary>
    Task<MuxExecutionResult> ExecuteMuxAsync(
        SeriesEpisodeMuxPlan plan,
        Action<string>? onOutput = null,
        Action<MuxExecutionUpdate>? onUpdate = null,
        CancellationToken cancellationToken = default,
        MuxWorkflowTemporaryCleanup temporaryCleanup = MuxWorkflowTemporaryCleanup.DeleteWorkingCopy);
}

/// <summary>
/// Standardworkflow für Arbeitskopie, MKVToolNix-Ausführung und temporäres Aufräumen.
/// </summary>
internal sealed class MuxWorkflowCoordinator : IMuxWorkflowCoordinator
{
    private readonly SeriesEpisodeMuxService _muxService;
    private readonly IFileCopyService _fileCopyService;
    private readonly IEpisodeCleanupService _cleanupService;

    public MuxWorkflowCoordinator(
        SeriesEpisodeMuxService muxService,
        IFileCopyService fileCopyService,
        IEpisodeCleanupService cleanupService)
    {
        _muxService = muxService;
        _fileCopyService = fileCopyService;
        _cleanupService = cleanupService;
    }

    /// <summary>
    /// Prüft, ob vor dem eigentlichen Lauf zunächst eine lokale Arbeitskopie angelegt oder aktualisiert werden muss.
    /// </summary>
    /// <param name="plan">Zu prüfender Mux-Plan.</param>
    /// <returns><see langword="true"/>, wenn die Arbeitskopie vorbereitet werden muss.</returns>
    public bool NeedsWorkingCopyPreparation(SeriesEpisodeMuxPlan plan)
    {
        return plan.WorkingCopy is not null && _fileCopyService.NeedsCopy(plan.WorkingCopy);
    }

    /// <summary>
    /// Erstellt oder aktualisiert die im Plan beschriebene Arbeitskopie und meldet Fortschritt an den Aufrufer zurück.
    /// </summary>
    /// <param name="plan">Mux-Plan mit optionaler Arbeitskopie.</param>
    /// <param name="onUpdate">Optionaler Callback für Fortschrittsmeldungen der Vorbereitung.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    public async Task PrepareWorkingCopyAsync(
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
    /// Führt den zum Plan passenden Lauf aus, invalidiert danach Probe-Caches und räumt die temporäre Arbeitskopie je nach Aufrufermodus auf.
    /// </summary>
    /// <param name="plan">Auszuführender Mux-Plan.</param>
    /// <param name="onOutput">Optionaler Callback für rohe Prozessausgabe.</param>
    /// <param name="onUpdate">Optionaler Callback für strukturierten Mux-Fortschritt.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <param name="temporaryCleanup">
    /// Legt fest, ob die Arbeitskopie sofort gelöscht wird oder für einen nachgelagerten Cleanup-Schritt erhalten bleibt.
    /// Der Single-Modus nutzt weiterhin das direkte Löschen; der Batch-Modus verschiebt Arbeitskopien zusammen mit den
    /// ursprünglichen Quellen in den Done-Ordner, damit der abschließende Papierkorb-Schritt alle Laufartefakte bündelt.
    /// </param>
    /// <returns>Exitcode, Warnungsstatus und letzter bekannter Fortschritt.</returns>
    public async Task<MuxExecutionResult> ExecuteMuxAsync(
        SeriesEpisodeMuxPlan plan,
        Action<string>? onOutput = null,
        Action<MuxExecutionUpdate>? onUpdate = null,
        CancellationToken cancellationToken = default,
        MuxWorkflowTemporaryCleanup temporaryCleanup = MuxWorkflowTemporaryCleanup.DeleteWorkingCopy)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureOutputDirectoryExists(plan);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureWorkingCopyCurrent(plan);
            return await _muxService.ExecuteAsync(plan, onOutput, onUpdate, cancellationToken);
        }
        finally
        {
            _muxService.InvalidatePlanOutputs(plan);
            if (temporaryCleanup == MuxWorkflowTemporaryCleanup.DeleteWorkingCopy)
            {
                _cleanupService.DeleteTemporaryFile(plan.WorkingCopy?.DestinationFilePath);
            }
        }
    }

    private static void EnsureOutputDirectoryExists(SeriesEpisodeMuxPlan plan)
    {
        var outputDirectory = Path.GetDirectoryName(plan.OutputFilePath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new DirectoryNotFoundException($"Ausgabeziel konnte nicht bestimmt werden: {plan.OutputFilePath}");
        }

        // Zielordner erst unmittelbar vor dem echten Werkzeuglauf anlegen,
        // damit Vorschau und Planaktualisierung keine Dateisystem-Seiteneffekte auslösen.
        Directory.CreateDirectory(outputDirectory);
    }

    private static void EnsureWorkingCopyCurrent(SeriesEpisodeMuxPlan plan)
    {
        if (plan.WorkingCopy is null || plan.WorkingCopy.IsReusable)
        {
            return;
        }

        throw new InvalidOperationException(
            "Die vorbereitete Arbeitskopie passt nicht mehr zur aktuellen Archivdatei. Bitte den Eintrag neu analysieren und die Arbeitskopie erneut erstellen.");
    }
}

/// <summary>
/// Fortschrittsmeldung für die Vorbereitung einer lokalen Arbeitskopie.
/// </summary>
internal sealed record WorkingCopyPreparationUpdate(int ProgressPercent, bool ReusesExistingCopy);

/// <summary>
/// Beschreibt, wer nach einem Werkzeuglauf für temporäre Arbeitskopien verantwortlich ist.
/// </summary>
internal enum MuxWorkflowTemporaryCleanup
{
    /// <summary>
    /// Die Arbeitskopie wird direkt im Workflow-Finalizer gelöscht. Das ist der Standard für Einzellauf und Tests.
    /// </summary>
    DeleteWorkingCopy,

    /// <summary>
    /// Die Arbeitskopie bleibt erhalten und muss vom Aufrufer bewusst verschoben oder gelöscht werden.
    /// </summary>
    KeepWorkingCopy
}
