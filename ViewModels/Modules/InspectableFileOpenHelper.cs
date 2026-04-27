using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Gemeinsamer UI-Helfer für "Quelle prüfen"/Datei-Öffnen-Aktionen in Einzel- und Batch-Mux.
/// Die eigentliche Plattformaktion bleibt im Dialogservice; der Helfer vereinheitlicht nur
/// Erfolgsauswertung und Statusmeldung.
/// </summary>
internal static class InspectableFileOpenHelper
{
    /// <summary>
    /// Öffnet die angegebenen Dateien über den Dialogservice und schreibt danach den passenden Status.
    /// </summary>
    public static void Open(
        IUserDialogService dialogService,
        IEnumerable<string> filePaths,
        Action<string, int> setStatus,
        int currentProgressValue,
        string successStatusText,
        string failedStatusText)
    {
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(setStatus);

        var opened = dialogService.TryOpenFilesWithDefaultApp(filePaths);
        setStatus(opened ? successStatusText : failedStatusText, currentProgressValue);
    }
}
