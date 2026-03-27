using System.Windows;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Minimale Dialogoberfläche für den manuellen Quellen-Review, damit der Workflow gezielt testbar bleibt.
/// </summary>
public interface IUserDialogService
{
    void OpenFilesWithDefaultApp(IEnumerable<string> filePaths);

    MessageBoxResult AskSourceReviewResult(string fileName, bool canTryAlternative);
}
