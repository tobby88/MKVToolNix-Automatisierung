using System.Windows;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Abstraktion für alle Benutzerdialoge und Shell-Öffnungen der App.
/// </summary>
/// <remarks>
/// Die ViewModels kennen damit nur noch die fachliche Dialogoberfläche statt der konkreten WPF-Implementierung.
/// Das vereinfacht Tests und hält UI-nahe Details an einer zentralen Stelle.
/// </remarks>
internal interface IUserDialogService
{
    string? SelectMainVideo(string initialDirectory);

    string? SelectAudioDescription(string initialDirectory);

    string[]? SelectSubtitles(string initialDirectory);

    string[]? SelectAttachments(string initialDirectory);

    string? SelectOutput(string initialDirectory, string fileName);

    string? SelectFolder(string title, string initialDirectory);

    string? SelectExecutable(string title, string filter, string initialDirectory);

    string? SelectFile(string title, string filter, string initialDirectory);

    MessageBoxResult AskAudioDescriptionChoice();

    MessageBoxResult AskSubtitlesChoice();

    MessageBoxResult AskAttachmentChoice();

    bool ConfirmMuxStart();

    bool ConfirmBatchExecution(int itemCount, int archiveFileCount, long archiveTotalBytes);

    bool ConfirmArchiveCopy(MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux.FileCopyPlan copyPlan);

    bool ConfirmSingleEpisodeCleanup(IReadOnlyList<string> usedFiles, IReadOnlyList<string> unusedFiles);

    bool ConfirmBatchRecycleDoneFiles(int fileCount, string doneDirectory);

    bool AskOpenDoneDirectory(string doneDirectory);

    /// <summary>
    /// Fragt nach einer expliziten Freigabe für fachliche Planhinweise, die vor dem Muxen geprüft werden müssen.
    /// </summary>
    /// <param name="episodeTitle">Lesbarer Episodentitel für die Dialogzuordnung.</param>
    /// <param name="reviewText">Konkreter Hinweistext aus der Planerstellung.</param>
    /// <returns><see langword="true"/>, wenn der Hinweis als geprüft gelten soll.</returns>
    bool ConfirmPlanReview(string episodeTitle, string reviewText);

    /// <summary>
    /// Versucht die angegebenen Dateien mit der Standardanwendung zu öffnen.
    /// </summary>
    /// <remarks>
    /// Der Rückgabewert erlaubt dem Review-Workflow, die Prüfung sauber abzubrechen, wenn das
    /// eigentliche Öffnen fehlschlägt und der Benutzer deshalb nichts verifizieren konnte.
    /// </remarks>
    bool TryOpenFilesWithDefaultApp(IEnumerable<string> filePaths);

    void OpenPathWithDefaultApp(string path);

    MessageBoxResult AskSourceReviewResult(string fileName, bool canTryAlternative);

    void ShowInfo(string title, string message);

    void ShowWarning(string title, string message);

    void ShowError(string message);
}
