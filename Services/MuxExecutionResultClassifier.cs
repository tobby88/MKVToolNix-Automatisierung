using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Bewertet Werkzeug-Exitcodes einheitlich für Einzel- und Batch-Mux.
/// </summary>
internal static class MuxExecutionResultClassifier
{
    /// <summary>
    /// Ordnet das rohe MKVToolNix-Ergebnis einer UI- und Log-tauglichen Kategorie zu.
    /// </summary>
    /// <param name="result">Exitcode und Warnungsstatus des Werkzeuglaufs.</param>
    /// <param name="outputSnapshotBeforeRun">Dateizustand der Ausgabedatei direkt vor dem Lauf.</param>
    /// <param name="outputPath">Erwarteter Ausgabepfad des Plans.</param>
    /// <returns>Die fachliche Bewertung des Werkzeuglaufs.</returns>
    public static MuxExecutionOutcomeKind Classify(
        MuxExecutionResult result,
        FileStateSnapshot? outputSnapshotBeforeRun,
        string outputPath)
    {
        if (result.ExitCode == 0 && !result.HasWarning)
        {
            return MuxExecutionOutcomeKind.Success;
        }

        if ((result.ExitCode == 0 && result.HasWarning)
            || (result.ExitCode == 1 && WasOutputCreatedOrChanged(outputSnapshotBeforeRun, outputPath)))
        {
            return MuxExecutionOutcomeKind.Warning;
        }

        return MuxExecutionOutcomeKind.Error;
    }

    private static bool WasOutputCreatedOrChanged(FileStateSnapshot? beforeRun, string outputPath)
    {
        var afterRun = FileStateSnapshot.TryCreate(outputPath);
        return afterRun is not null && !afterRun.Equals(beforeRun);
    }
}

/// <summary>
/// Fachliche Bewertung eines MKVToolNix-Laufs nach Exitcode, Warnungen und Ausgabezustand.
/// </summary>
internal enum MuxExecutionOutcomeKind
{
    /// <summary>
    /// Der Lauf war vollständig erfolgreich.
    /// </summary>
    Success,

    /// <summary>
    /// Der Lauf hat eine verwendbare Ausgabe erzeugt oder geändert, aber Warnungen gemeldet.
    /// </summary>
    Warning,

    /// <summary>
    /// Der Lauf hat keine belastbare Ausgabe erzeugt.
    /// </summary>
    Error
}
