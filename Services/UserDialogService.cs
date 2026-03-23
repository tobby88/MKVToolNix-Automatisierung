using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace MkvToolnixAutomatisierung.Services;

public sealed class UserDialogService
{
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

    public MessageBoxResult AskAudioDescriptionChoice()
    {
        return MessageBox.Show(
            GetOwner(),
            "Ja = AD-Datei manuell wählen, Nein = AD-Datei leeren, Abbrechen = nichts ändern.",
            "AD-Datei korrigieren",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
    }

    public MessageBoxResult AskSubtitlesChoice()
    {
        return MessageBox.Show(
            GetOwner(),
            "Ja = Untertitel manuell wählen, Nein = Untertitel leeren, Abbrechen = nichts ändern.",
            "Untertitel korrigieren",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
    }

    public MessageBoxResult AskAttachmentChoice()
    {
        return MessageBox.Show(
            GetOwner(),
            "Ja = Anhänge manuell wählen, Nein = Anhänge leeren, Abbrechen = nichts ändern.",
            "Anhänge korrigieren",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
    }

    public bool ConfirmMuxStart()
    {
        return MessageBox.Show(
            GetOwner(),
            "Soll der angezeigte mkvmerge-Aufruf jetzt ausgeführt werden?",
            "Muxing starten",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

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

    public bool ConfirmArchiveCopy(MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux.FileCopyPlan copyPlan)
    {
        return MessageBox.Show(
            GetOwner(),
            $"Die vorhandene Zieldatei muss zuerst lokal kopiert werden:\n{copyPlan.SourceFilePath}\n\nArbeitskopie:\n{copyPlan.DestinationFilePath}\n\nDateigröße: {FormatFileSize(copyPlan.FileSizeBytes)}\n\nJetzt kopieren?",
            "Zieldatei kopieren",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

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

    public bool ConfirmBatchRecycleDoneFiles(int fileCount, string doneDirectory)
    {
        return MessageBox.Show(
            GetOwner(),
            $"{fileCount} Datei(en) dieses Laufs liegen jetzt in:\n{doneDirectory}\n\nSollen sie jetzt gesammelt in den Papierkorb verschoben werden?",
            "Done-Ordner aufräumen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public bool AskOpenDoneDirectory(string doneDirectory)
    {
        return MessageBox.Show(
            GetOwner(),
            $"Der Done-Ordner bleibt vorerst erhalten:\n{doneDirectory}\n\nJetzt zur Kontrolle öffnen?",
            "Done-Ordner öffnen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public void OpenFilesWithDefaultApp(IEnumerable<string> filePaths)
    {
        var files = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .ToList();

        if (files.Count == 0)
        {
            ShowWarning("Hinweis", "Es wurden keine prüfbaren Quelldateien gefunden.");
            return;
        }

        foreach (var filePath in files)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
    }

    public void OpenPathWithDefaultApp(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            ShowWarning("Hinweis", "Der Pfad konnte nicht geöffnet werden.");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
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
