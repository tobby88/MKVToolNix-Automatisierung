using System.Windows;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Minimale Dialogoberfläche für den manuellen Quellen-Review, damit der Workflow gezielt testbar bleibt.
/// </summary>
public interface IUserDialogService
{
    /// <summary>
    /// Versucht die angegebenen Dateien mit der Standardanwendung zu öffnen.
    /// </summary>
    /// <remarks>
    /// Der Rückgabewert erlaubt dem Review-Workflow, die Prüfung sauber abzubrechen, wenn das
    /// eigentliche Öffnen fehlschlägt und der Benutzer deshalb nichts verifizieren konnte.
    /// </remarks>
    bool TryOpenFilesWithDefaultApp(IEnumerable<string> filePaths);

    MessageBoxResult AskSourceReviewResult(string fileName, bool canTryAlternative);
}
