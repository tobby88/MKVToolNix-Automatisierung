using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

/// <summary>
/// Fassadenservice über Planner und Prozessausführung für Erkennung, Vorschau und echten Mux-Lauf.
/// </summary>
public sealed class SeriesEpisodeMuxService
{
    private readonly SeriesEpisodeMuxPlanner _planner;
    private readonly MuxExecutionService _executionService;
    private readonly MkvMergeOutputParser _outputParser;

    public SeriesEpisodeMuxService(
        SeriesEpisodeMuxPlanner planner,
        MuxExecutionService executionService,
        MkvMergeOutputParser outputParser)
    {
        _planner = planner;
        _executionService = executionService;
        _outputParser = outputParser;
    }

    /// <summary>
    /// Führt die Dateierkennung synchron für eine Hauptquelle aus.
    /// </summary>
    /// <param name="mainVideoPath">Pfad zur ausgewählten Hauptvideodatei.</param>
    /// <returns>Alle automatisch erkannten Episodendateien und Vorschläge.</returns>
    public AutoDetectedEpisodeFiles DetectFromMainVideo(string mainVideoPath)
    {
        return _planner.DetectFromMainVideo(mainVideoPath);
    }

    /// <summary>
    /// Führt die Dateierkennung auf einem STA-Thread aus, damit Shell- und WPF-nahe Aufrufer stabil bleiben.
    /// </summary>
    /// <param name="selectedVideoPath">Pfad zur vom Benutzer ausgewählten Videodatei.</param>
    /// <param name="onProgress">Optionaler Callback für Status- und Fortschrittsmeldungen.</param>
    /// <param name="excludedSourcePaths">Optionaler Satz von Quellpfaden, die bei der Erkennung ignoriert werden sollen.</param>
    /// <returns>Alle automatisch erkannten Episodendateien und Vorschläge.</returns>
    public Task<AutoDetectedEpisodeFiles> DetectFromSelectedVideoAsync(
        string selectedVideoPath,
        Action<DetectionProgressUpdate>? onProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null)
    {
        return RunOnStaThreadAsync(() => _planner.DetectFromMainVideo(selectedVideoPath, onProgress, excludedSourcePaths));
    }

    /// <summary>
    /// Führt die Dateierkennung auf einem STA-Thread mit bereits vorbereitetem Ordnerkontext aus.
    /// </summary>
    /// <param name="selectedVideoPath">Pfad zur vom Benutzer ausgewählten Videodatei.</param>
    /// <param name="directoryContext">Vorbereiteter Ordnerkontext derselben Quelle für wiederholte Scans.</param>
    /// <param name="onProgress">Optionaler Callback für Status- und Fortschrittsmeldungen.</param>
    /// <param name="excludedSourcePaths">Optionaler Satz von Quellpfaden, die bei der Erkennung ignoriert werden sollen.</param>
    /// <returns>Alle automatisch erkannten Episodendateien und Vorschläge.</returns>
    public Task<AutoDetectedEpisodeFiles> DetectFromSelectedVideoAsync(
        string selectedVideoPath,
        SeriesEpisodeMuxPlanner.DirectoryDetectionContext directoryContext,
        Action<DetectionProgressUpdate>? onProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null)
    {
        ArgumentNullException.ThrowIfNull(directoryContext);
        return RunOnStaThreadAsync(() => _planner.DetectFromMainVideo(selectedVideoPath, directoryContext, onProgress, excludedSourcePaths));
    }

    /// <summary>
    /// Bereitet einen Quellordner einmalig für mehrere Erkennungsläufe vor und liefert den wiederverwendbaren Kontext zurück.
    /// </summary>
    /// <param name="sourceDirectory">Ordner mit Episodenquellen und ihren Begleitdateien.</param>
    /// <returns>Wiederverwendbarer Ordnerkontext für Batch-Scans.</returns>
    public SeriesEpisodeMuxPlanner.DirectoryDetectionContext CreateDirectoryDetectionContext(string sourceDirectory)
    {
        return _planner.CreateDirectoryDetectionContext(sourceDirectory);
    }

    /// <summary>
    /// Erstellt aus einer UI-Anfrage einen vollständig aufgelösten Mux-Plan.
    /// </summary>
    /// <param name="request">Aktuelle Nutzereingaben für Video, Untertitel, AD und Zielpfad.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Der fertige Mux-Plan inklusive Archivintegration.</returns>
    public Task<SeriesEpisodeMuxPlan> CreatePlanAsync(
        SeriesEpisodeMuxRequest request,
        CancellationToken cancellationToken = default)
    {
        return _planner.CreatePlanAsync(request, cancellationToken);
    }

    /// <summary>
    /// Formatiert einen Mux-Plan als mehrzeiligen Vorschautext für die GUI.
    /// </summary>
    /// <param name="plan">Der zuvor berechnete Mux-Plan.</param>
    /// <returns>Lesbare Zusammenfassung des geplanten Aufrufs.</returns>
    public string BuildPreviewText(SeriesEpisodeMuxPlan plan)
    {
        return plan.BuildPreviewText();
    }

    /// <summary>
    /// Verwirft gecachte Probe-Ergebnisse für Ausgabedatei und optionale Arbeitskopie eines Plans.
    /// </summary>
    /// <param name="plan">Der Plan, dessen Ausgabepfade invalidiert werden sollen.</param>
    public void InvalidatePlanOutputs(SeriesEpisodeMuxPlan? plan)
    {
        if (plan is null)
        {
            return;
        }

        _planner.InvalidateProbeCaches(
        [
            plan.OutputFilePath,
            plan.WorkingCopy?.DestinationFilePath
        ]);
    }

    /// <summary>
    /// Führt den eigentlichen mkvmerge-Prozess aus und übersetzt dessen Konsolenausgabe in Fortschrittsereignisse.
    /// </summary>
    /// <param name="plan">Der auszuführende Mux-Plan.</param>
    /// <param name="onOutput">Optionaler Callback für rohe Prozessausgabe.</param>
    /// <param name="onUpdate">Optionaler Callback für strukturierten Fortschritt und Warnungen.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Exitcode, Warnungsstatus und letzter bekannter Fortschritt des Prozesses.</returns>
    public async Task<MuxExecutionResult> ExecuteAsync(
        SeriesEpisodeMuxPlan plan,
        Action<string>? onOutput = null,
        Action<MuxExecutionUpdate>? onUpdate = null,
        CancellationToken cancellationToken = default)
    {
        var hadWarning = false;
        int? latestProgressPercent = null;

        onUpdate?.Invoke(new MuxExecutionUpdate(0, false));

        var exitCode = await _executionService.ExecuteAsync(
            plan.MkvMergePath,
            plan.BuildArguments(),
            line =>
            {
                onOutput?.Invoke(line);

                var parsedOutput = _outputParser.Parse(line);
                if (parsedOutput.ProgressPercent is null && !parsedOutput.IsWarning)
                {
                    return;
                }

                if (parsedOutput.ProgressPercent is int progressPercent)
                {
                    latestProgressPercent = progressPercent;
                }

                if (parsedOutput.IsWarning)
                {
                    hadWarning = true;
                }

                onUpdate?.Invoke(new MuxExecutionUpdate(latestProgressPercent, hadWarning));
            },
            cancellationToken);

        return new MuxExecutionResult(exitCode, hadWarning, latestProgressPercent);
    }

    private static Task<T> RunOnStaThreadAsync<T>(Func<T> action)
    {
        var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completionSource.SetResult(action());
            }
            catch (Exception ex)
            {
                completionSource.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return completionSource.Task;
    }
}

/// <summary>
/// Fortschrittssignal während eines laufenden mkvmerge-Prozesses.
/// </summary>
public sealed record MuxExecutionUpdate(int? ProgressPercent, bool HasWarning);

/// <summary>
/// Verdichtetes Ergebnis eines abgeschlossenen mkvmerge-Prozesses.
/// </summary>
public sealed record MuxExecutionResult(int ExitCode, bool HasWarning, int? LastProgressPercent);
