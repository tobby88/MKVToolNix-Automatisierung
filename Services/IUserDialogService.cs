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
    /// <summary>
    /// Öffnet die Auswahl einer primären Videoquelle oder eines subtitle-only-Einstiegs.
    /// </summary>
    string? SelectMainVideo(string initialDirectory);

    /// <summary>
    /// Öffnet die Auswahl einer AD-Quelle.
    /// </summary>
    string? SelectAudioDescription(string initialDirectory);

    /// <summary>
    /// Öffnet die Mehrfachauswahl externer Untertiteldateien.
    /// </summary>
    string[]? SelectSubtitles(string initialDirectory);

    /// <summary>
    /// Öffnet die Mehrfachauswahl von TXT-Anhängen.
    /// </summary>
    string[]? SelectAttachments(string initialDirectory);

    /// <summary>
    /// Öffnet die Auswahl des Zielpfads für eine MKV-Ausgabe.
    /// </summary>
    string? SelectOutput(string initialDirectory, string fileName);

    /// <summary>
    /// Öffnet einen Ordnerdialog.
    /// </summary>
    string? SelectFolder(string title, string initialDirectory);

    /// <summary>
    /// Öffnet die Auswahl einer ausführbaren Datei.
    /// </summary>
    string? SelectExecutable(string title, string filter, string initialDirectory);

    /// <summary>
    /// Öffnet die Auswahl einer einzelnen Datei.
    /// </summary>
    string? SelectFile(string title, string filter, string initialDirectory);

    /// <summary>
    /// Öffnet die Auswahl mehrerer Dateien.
    /// </summary>
    string[]? SelectFiles(string title, string filter, string initialDirectory);

    /// <summary>
    /// Fragt, ob eine AD manuell gesetzt oder geleert werden soll.
    /// </summary>
    MessageBoxResult AskAudioDescriptionChoice();

    /// <summary>
    /// Fragt, ob Untertitel manuell gesetzt oder geleert werden sollen.
    /// </summary>
    MessageBoxResult AskSubtitlesChoice();

    /// <summary>
    /// Fragt, ob Anhänge manuell gesetzt oder geleert werden sollen.
    /// </summary>
    MessageBoxResult AskAttachmentChoice();

    /// <summary>
    /// Fragt die explizite Freigabe zum Start eines Einzel-Muxlaufs ab.
    /// </summary>
    bool ConfirmMuxStart();

    /// <summary>
    /// Fragt die explizite Freigabe zum Start des Batch-Laufs ab.
    /// </summary>
    bool ConfirmBatchExecution(int itemCount, int archiveFileCount, long archiveTotalBytes);

    /// <summary>
    /// Fragt bei aktivem Batch-Filter, ob eine Auswahlaktion nur gefilterte oder alle Einträge betreffen soll.
    /// </summary>
    /// <param name="selectItems"><see langword="true"/> für Auswählen, <see langword="false"/> für Abwählen.</param>
    /// <returns><see langword="true"/>, wenn die Aktion auf alle Batch-Einträge erweitert werden soll.</returns>
    bool ConfirmApplyBatchSelectionToAllItems(bool selectItems);

    /// <summary>
    /// Fragt das Kopieren einer vorhandenen Zieldatei als Arbeitskopie ab.
    /// </summary>
    bool ConfirmArchiveCopy(MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux.FileCopyPlan copyPlan);

    /// <summary>
    /// Fragt das Aufräumen der Einzelfolgen-Quelldateien ab.
    /// </summary>
    bool ConfirmSingleEpisodeCleanup(IReadOnlyList<string> usedFiles, IReadOnlyList<string> unusedFiles);

    /// <summary>
    /// Fragt, ob der Done-Ordner eines Batch-Laufs gesammelt in den Papierkorb verschoben werden soll.
    /// </summary>
    bool ConfirmBatchRecycleDoneFiles(int fileCount, string doneDirectory);

    /// <summary>
    /// Fragt, ob der Done-Ordner zur manuellen Kontrolle geöffnet werden soll.
    /// </summary>
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

    /// <summary>
    /// Öffnet einen beliebigen Datei- oder Ordnerpfad mit der Standardanwendung.
    /// </summary>
    void OpenPathWithDefaultApp(string path);

    /// <summary>
    /// Fragt das Ergebnis einer manuellen Quellenprüfung ab.
    /// </summary>
    MessageBoxResult AskSourceReviewResult(string fileName, bool canTryAlternative);

    /// <summary>
    /// Zeigt eine reine Informationsmeldung.
    /// </summary>
    void ShowInfo(string title, string message);

    /// <summary>
    /// Zeigt eine Warnmeldung.
    /// </summary>
    void ShowWarning(string title, string message);

    /// <summary>
    /// Zeigt eine Fehlermeldung.
    /// </summary>
    void ShowError(string message);
}
