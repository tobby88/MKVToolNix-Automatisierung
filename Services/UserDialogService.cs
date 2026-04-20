using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Kapselt alle Dateidialoge, Bestätigungen und Shell-Öffnungen an einer Stelle.
/// </summary>
internal sealed class UserDialogService : IUserDialogService
{
    /// <summary>
    /// Öffnet die Auswahl der primären Videodatei im Einzelmodus.
    /// </summary>
    public string? SelectMainVideo(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Video-Datei auswählen",
            Filter = "MP4-Dateien (*.mp4)|*.mp4",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = initialDirectory
        };

        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileName : null;
    }

    /// <summary>
    /// Öffnet die Auswahl einer Audiodeskriptions-Datei.
    /// </summary>
    public string? SelectAudioDescription(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "AD-Datei auswählen",
            Filter = "MP4-Dateien (*.mp4)|*.mp4",
            CheckFileExists = true,
            InitialDirectory = initialDirectory
        };

        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileName : null;
    }

    /// <summary>
    /// Öffnet die Mehrfachauswahl externer Untertiteldateien.
    /// </summary>
    public string[]? SelectSubtitles(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Untertitel auswählen",
            Filter = "Untertitel (*.srt;*.ass;*.vtt)|*.srt;*.ass;*.vtt",
            CheckFileExists = true,
            Multiselect = true,
            InitialDirectory = initialDirectory
        };

        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileNames : null;
    }

    /// <summary>
    /// Öffnet die Mehrfachauswahl von TXT-Anhängen.
    /// </summary>
    public string[]? SelectAttachments(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Metadaten-Anhänge auswählen",
            Filter = "Textdateien (*.txt)|*.txt",
            CheckFileExists = true,
            Multiselect = true,
            InitialDirectory = initialDirectory
        };

        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileNames : null;
    }

    /// <summary>
    /// Öffnet die Auswahl eines MKV-Zielpfads.
    /// </summary>
    public string? SelectOutput(string initialDirectory, string fileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Zieldatei auswählen",
            Filter = "MKV-Dateien (*.mkv)|*.mkv",
            OverwritePrompt = true,
            AddExtension = true,
            DefaultExt = "mkv",
            InitialDirectory = initialDirectory,
            FileName = fileName
        };

        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileName : null;
    }

    /// <summary>
    /// Öffnet einen generischen Ordnerdialog.
    /// </summary>
    public string? SelectFolder(string title, string initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = initialDirectory,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <summary>
    /// Öffnet die Auswahl einer ausführbaren Datei, etwa für mkvmerge oder ffprobe.
    /// </summary>
    public string? SelectExecutable(string title, string filter, string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = initialDirectory
        };

        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileName : null;
    }

    /// <summary>
    /// Öffnet die Auswahl einer einzelnen Datei für generische Import-/Konfigurationsfälle.
    /// </summary>
    public string? SelectFile(string title, string filter, string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = initialDirectory
        };

        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileName : null;
    }

    /// <summary>
    /// Öffnet die Auswahl mehrerer Dateien für generische Import-/Konfigurationsfälle.
    /// </summary>
    public string[]? SelectFiles(string title, string filter, string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = true,
            InitialDirectory = initialDirectory
        };

        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileNames : null;
    }

    /// <summary>
    /// Baut die Drei-Wege-Entscheidung für AD-Korrekturen auf.
    /// </summary>
    public MessageBoxResult AskAudioDescriptionChoice()
    {
        return AskSelectionOrClearChoice(
            title: "AD-Datei korrigieren",
            subject: "AD-Datei",
            selectActionText: "AD-Datei manuell wählen",
            clearActionText: "AD-Datei leeren");
    }

    /// <summary>
    /// Baut die Drei-Wege-Entscheidung für Untertitel-Korrekturen auf.
    /// </summary>
    public MessageBoxResult AskSubtitlesChoice()
    {
        return AskSelectionOrClearChoice(
            title: "Untertitel korrigieren",
            subject: "Untertitel",
            selectActionText: "Untertitel manuell wählen",
            clearActionText: "Untertitel leeren");
    }

    /// <summary>
    /// Baut die Drei-Wege-Entscheidung für Anhangs-Korrekturen auf.
    /// </summary>
    public MessageBoxResult AskAttachmentChoice()
    {
        return AskSelectionOrClearChoice(
            title: "Anhänge korrigieren",
            subject: "Anhänge",
            selectActionText: "Anhänge manuell wählen",
            clearActionText: "Anhänge leeren");
    }

    /// <summary>
    /// Fragt den Start eines Einzel-Muxlaufs ab.
    /// </summary>
    public bool ConfirmMuxStart()
    {
        return MessageBox.Show(
            GetOwner(),
            "Soll der angezeigte MKVToolNix-Aufruf jetzt ausgeführt werden?",
            "Verarbeitung starten",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Fragt den Start eines vollständigen Batch-Laufs mit optionalem Archivkopier-Hinweis ab.
    /// </summary>
    public bool ConfirmBatchExecution(int itemCount, int archiveFileCount, long archiveTotalBytes)
    {
        var lines = new List<string>
        {
            $"{itemCount} Episode(n) werden jetzt nacheinander verarbeitet.",
            string.Empty,
            "Pflichtprüfungen wurden vorher bereits abgearbeitet."
        };

        if (archiveFileCount > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"{archiveFileCount} vorhandene Zieldatei(en) werden vorher lokal kopiert.");
            lines.Add($"Gesamtgröße: {FormatFileSize(archiveTotalBytes)}");
        }

        lines.Add(string.Empty);
        lines.Add("Jetzt komplett starten?");

        return MessageBox.Show(
            GetOwner(),
            string.Join(Environment.NewLine, lines),
            "Batch starten",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Fragt bei aktivem Filter, ob eine Sammelauswahl nur sichtbare oder alle Batch-Zeilen betreffen soll.
    /// </summary>
    public bool ConfirmApplyBatchSelectionToAllItems(bool selectItems)
    {
        var actionLabel = selectItems ? "Alle wählen" : "Alle abwählen";
        var actionVerb = selectItems ? "auswählen" : "abwählen";
        var visibleScopeText = selectItems
            ? "Nein = nur die aktuell sichtbaren Folgen auswählen"
            : "Nein = nur die aktuell sichtbaren Folgen abwählen";
        var fullScopeText = selectItems
            ? "Ja = auch die aktuell ausgeblendeten Folgen auswählen"
            : "Ja = auch die aktuell ausgeblendeten Folgen abwählen";
        var lines = new[]
        {
            $"Ein Filter ist aktiv. \"{actionLabel}\" kann nur die sichtbaren Folgen ändern oder zusätzlich auch die ausgeblendeten.",
            string.Empty,
            fullScopeText + ".",
            visibleScopeText + "."
        };

        return MessageBox.Show(
            GetOwner(),
            string.Join(Environment.NewLine, lines),
            "Filter auf Sammelaktion anwenden",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Fragt das Kopieren einer vorhandenen Zieldatei als lokale Arbeitskopie ab.
    /// </summary>
    public bool ConfirmArchiveCopy(MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux.FileCopyPlan copyPlan)
    {
        return MessageBox.Show(
            GetOwner(),
            $"Die vorhandene Zieldatei muss zuerst lokal kopiert werden:\n{copyPlan.SourceFilePath}\n\nArbeitskopie:\n{copyPlan.DestinationFilePath}\n\nDateigröße: {FormatFileSize(copyPlan.FileSizeBytes)}\n\nJetzt kopieren?",
            "Zieldatei kopieren",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Fragt das Aufräumen der Einzelfolgen-Quelldateien ab.
    /// </summary>
    public bool ConfirmSingleEpisodeCleanup(IReadOnlyList<string> usedFiles, IReadOnlyList<string> unusedFiles)
    {
        var lines = new List<string>
        {
            "Sollen die Quelldateien dieser Episode jetzt in den Papierkorb verschoben werden?"
        };

        if (usedFiles.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Verwendet:");
            lines.AddRange(usedFiles.Select(path => "- " + Path.GetFileName(path)));
        }

        if (unusedFiles.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Nicht verwendet, aber erkannt:");
            lines.AddRange(unusedFiles.Select(path => "- " + Path.GetFileName(path)));
        }

        return MessageBox.Show(
            GetOwner(),
            string.Join(Environment.NewLine, lines),
            "Quelldateien aufräumen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Fragt das spätere Recyceln des Done-Ordners nach einem Batch-Lauf ab.
    /// </summary>
    public bool ConfirmBatchRecycleDoneFiles(int fileCount, string doneDirectory)
    {
        return MessageBox.Show(
            GetOwner(),
            $"{fileCount} Datei(en) dieses Laufs liegen jetzt in:\n{doneDirectory}\n\nSollen sie jetzt gesammelt in den Papierkorb verschoben werden?",
            "Done-Ordner aufräumen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Fragt, ob der Done-Ordner zunächst zur manuellen Kontrolle geöffnet werden soll.
    /// </summary>
    public bool AskOpenDoneDirectory(string doneDirectory)
    {
        return MessageBox.Show(
            GetOwner(),
            $"Der Done-Ordner bleibt vorerst erhalten:\n{doneDirectory}\n\nJetzt zur Kontrolle öffnen?",
            "Done-Ordner öffnen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Fragt die explizite Freigabe eines fachlichen Planhinweises ab.
    /// </summary>
    public bool ConfirmPlanReview(string episodeTitle, string reviewText)
    {
        var lines = new List<string>
        {
            $"Episode: {episodeTitle}",
            string.Empty,
            string.IsNullOrWhiteSpace(reviewText)
                ? "Für diese Episode liegt ein fachlicher Planhinweis vor."
                : reviewText.Trim(),
            string.Empty,
            "Diesen Hinweis als geprüft freigeben?"
        };

        return MessageBox.Show(
            GetOwner(),
            string.Join(Environment.NewLine, lines),
            "Hinweis prüfen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Öffnet Prüfdokumente mit der registrierten Standardanwendung und meldet Shell-Fehler direkt.
    /// </summary>
    /// <remarks>
    /// Der Review-Workflow fragt nur dann nach einer fachlichen Freigabe, wenn mindestens eine Datei
    /// erfolgreich geöffnet werden konnte. So werden kaputte Shell-Zuordnungen oder blockierte Pfade
    /// nicht fälschlich wie erfolgreich geprüfte Quellen behandelt.
    /// </remarks>
    public bool TryOpenFilesWithDefaultApp(IEnumerable<string> filePaths)
    {
        var files = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .ToList();

        if (files.Count == 0)
        {
            ShowWarning("Hinweis", "Es wurden keine prüfbaren Quelldateien gefunden.");
            return false;
        }

        foreach (var filePath in files)
        {
            if (!TryOpenShellPath(
                    filePath,
                    "Hinweis",
                    $"Die Datei konnte nicht mit der Standardanwendung geöffnet werden:{Environment.NewLine}{filePath}"))
            {
                return false;
            }
        }

        return true;
    }

    public void OpenPathWithDefaultApp(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            ShowWarning("Hinweis", "Der Pfad konnte nicht geöffnet werden.");
            return;
        }

        TryOpenShellPath(
            path,
            "Hinweis",
            $"Der Pfad konnte nicht geöffnet werden:{Environment.NewLine}{path}");
    }

    public MessageBoxResult AskSourceReviewResult(string fileName, bool canTryAlternative)
    {
        var noText = canTryAlternative
            ? "Nein = diese Quelle verwerfen und die nächste Alternative prüfen."
            : "Nein = diese Quelle ist nicht in Ordnung.";

        return MessageBox.Show(
            GetOwner(),
            $"Die Quelle wurde zur Prüfung geöffnet:\n{fileName}\n\nJa = Quelle ist in Ordnung und wird freigegeben.\n{noText}\nAbbrechen = vorerst nichts ändern.",
            "Quelle prüfen",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
    }

    public void ShowInfo(string title, string message)
    {
        MessageBox.Show(GetOwner(), message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowWarning(string title, string message)
    {
        MessageBox.Show(GetOwner(), message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void ShowError(string message)
    {
        MessageBox.Show(GetOwner(), message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static Window? GetOwner()
    {
        return Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? Application.Current?.MainWindow;
    }

    /// <summary>
    /// Führt Shell-Öffnungen an einer Stelle aus, damit Fehlerbehandlung und Benutzerhinweise konsistent bleiben.
    /// </summary>
    private bool TryOpenShellPath(string path, string title, string failureMessage)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });

            if (process is not null)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            ShowWarning(title, $"{failureMessage}{Environment.NewLine}{Environment.NewLine}Technische Details: {ex.Message}");
            return false;
        }

        ShowWarning(title, failureMessage);
        return false;
    }

    /// <summary>
    /// Baut die wiederkehrende Drei-Wege-Entscheidung "wählen / leeren / unverändert lassen" konsistent auf.
    /// </summary>
    private static MessageBoxResult AskSelectionOrClearChoice(
        string title,
        string subject,
        string selectActionText,
        string clearActionText)
    {
        var lines = new[]
        {
            $"{subject} anpassen:",
            string.Empty,
            $"Ja: {selectActionText}",
            $"Nein: {clearActionText}",
            "Abbrechen: nichts ändern"
        };

        return MessageBox.Show(
            GetOwner(),
            string.Join(Environment.NewLine, lines),
            title,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["Bytes", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
