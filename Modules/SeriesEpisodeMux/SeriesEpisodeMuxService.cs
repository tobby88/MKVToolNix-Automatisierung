using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

/// <summary>
/// Fassadenservice über Planner und Prozessausführung für Erkennung, Vorschau und echte MKVToolNix-Läufe.
/// </summary>
public sealed class SeriesEpisodeMuxService
{
    private readonly SeriesEpisodeMuxPlanner _planner;
    private readonly MuxExecutionService _executionService;
    private readonly MkvMergeOutputParser _outputParser;

    /// <summary>
    /// Initialisiert die Fassade für Erkennung, Planerzeugung und Mux-Ausführung.
    /// </summary>
    /// <param name="planner">Fachlicher Planer für Detection und Mux-Pläne.</param>
    /// <param name="executionService">Prozessdienst zum Starten der benötigten MKVToolNix-Werkzeuge.</param>
    /// <param name="outputParser">Übersetzer für rohe Prozessausgabe in strukturierte Updates.</param>
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
    /// <param name="cancellationToken">Optionales Abbruchsignal für den aufrufenden Task.</param>
    /// <returns>Alle automatisch erkannten Episodendateien und Vorschläge.</returns>
    public Task<AutoDetectedEpisodeFiles> DetectFromSelectedVideoAsync(
        string selectedVideoPath,
        Action<DetectionProgressUpdate>? onProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null,
        CancellationToken cancellationToken = default)
    {
        var progressCallback = CreateCancelableProgressCallback(onProgress, cancellationToken);
        return RunOnStaThreadAsync(
            token => _planner.DetectFromMainVideo(selectedVideoPath, progressCallback, excludedSourcePaths, token),
            cancellationToken);
    }

    /// <summary>
    /// Führt die Dateierkennung auf einem STA-Thread mit bereits vorbereitetem Ordnerkontext aus.
    /// </summary>
    /// <param name="selectedVideoPath">Pfad zur vom Benutzer ausgewählten Videodatei.</param>
    /// <param name="directoryContext">Vorbereiteter Ordnerkontext derselben Quelle für wiederholte Scans.</param>
    /// <param name="onProgress">Optionaler Callback für Status- und Fortschrittsmeldungen.</param>
    /// <param name="excludedSourcePaths">Optionaler Satz von Quellpfaden, die bei der Erkennung ignoriert werden sollen.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal für den aufrufenden Task.</param>
    /// <returns>Alle automatisch erkannten Episodendateien und Vorschläge.</returns>
    public Task<AutoDetectedEpisodeFiles> DetectFromSelectedVideoAsync(
        string selectedVideoPath,
        SeriesEpisodeMuxPlanner.DirectoryDetectionContext directoryContext,
        Action<DetectionProgressUpdate>? onProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directoryContext);
        var progressCallback = CreateCancelableProgressCallback(onProgress, cancellationToken);
        return RunOnStaThreadAsync(
            token => _planner.DetectFromMainVideo(selectedVideoPath, directoryContext, progressCallback, excludedSourcePaths, token),
            cancellationToken);
    }

    /// <summary>
    /// Bereitet einen Quellordner einmalig für mehrere Erkennungsläufe vor und liefert den wiederverwendbaren Kontext zurück.
    /// </summary>
    /// <param name="sourceDirectory">Ordner mit Episodenquellen und ihren Begleitdateien.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal für die vorbereitende Ordneranalyse.</param>
    /// <returns>Wiederverwendbarer Ordnerkontext für Batch-Scans.</returns>
    public SeriesEpisodeMuxPlanner.DirectoryDetectionContext CreateDirectoryDetectionContext(
        string sourceDirectory,
        CancellationToken cancellationToken = default)
    {
        return _planner.CreateDirectoryDetectionContext(sourceDirectory, cancellationToken);
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
    /// Führt den zum Plan passenden MKVToolNix-Prozess aus und übersetzt dessen Konsolenausgabe in Fortschrittsereignisse.
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
        if (plan.HasHeaderEdits)
        {
            return await ExecuteTrackHeaderEditAsync(plan, onOutput, onUpdate, cancellationToken);
        }

        var hadWarning = false;
        int? latestProgressPercent = null;

        onUpdate?.Invoke(new MuxExecutionUpdate(0, false));

        var exitCode = await _executionService.ExecuteAsync(
            plan.ExecutionToolPath,
            plan.BuildArguments(),
            plan.ExecutionToolDisplayName,
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

    private async Task<MuxExecutionResult> ExecuteTrackHeaderEditAsync(
        SeriesEpisodeMuxPlan plan,
        Action<string>? onOutput,
        Action<MuxExecutionUpdate>? onUpdate,
        CancellationToken cancellationToken)
    {
        var hadWarning = false;

        onUpdate?.Invoke(new MuxExecutionUpdate(0, false));
        var exitCode = await _executionService.ExecuteAsync(
            plan.ExecutionToolPath,
            plan.BuildArguments(),
            plan.ExecutionToolDisplayName,
            line =>
            {
                onOutput?.Invoke(line);
                if (line.Contains("warning", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("warnung", StringComparison.OrdinalIgnoreCase))
                {
                    hadWarning = true;
                }
            },
            cancellationToken);

        onUpdate?.Invoke(new MuxExecutionUpdate(100, hadWarning));
        return new MuxExecutionResult(exitCode, hadWarning, 100);
    }

    private static Action<DetectionProgressUpdate>? CreateCancelableProgressCallback(
        Action<DetectionProgressUpdate>? onProgress,
        CancellationToken cancellationToken)
    {
        if (onProgress is null)
        {
            return null;
        }

        return update =>
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                onProgress(update);
            }
        };
    }

    private static Task<T> RunOnStaThreadAsync<T>(Func<CancellationToken, T> action, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration cancellationRegistration = default;
        if (cancellationToken.CanBeCanceled)
        {
            cancellationRegistration = cancellationToken.Register(() => completionSource.TrySetCanceled(cancellationToken));
        }

        var thread = new Thread(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completionSource.TrySetCanceled(cancellationToken);
                    return;
                }

                completionSource.TrySetResult(action(cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completionSource.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
            }
            finally
            {
                cancellationRegistration.Dispose();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return completionSource.Task;
    }
}

/// <summary>
/// Fortschrittssignal während eines laufenden MKVToolNix-Prozesses.
/// </summary>
public sealed record MuxExecutionUpdate(int? ProgressPercent, bool HasWarning);

/// <summary>
/// Verdichtetes Ergebnis eines abgeschlossenen MKVToolNix-Prozesses.
/// </summary>
public sealed record MuxExecutionResult(int ExitCode, bool HasWarning, int? LastProgressPercent);
