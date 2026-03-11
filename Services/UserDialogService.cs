using System.Windows;
using Microsoft.Win32;

namespace MkvToolnixAutomatisierung.Services;

public sealed class UserDialogService
{
    public string? SelectMainVideo(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Normale Episoden-Datei auswaehlen",
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
            Title = "AD-Datei auswaehlen",
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
            Title = "Untertitel auswaehlen",
            Filter = "Untertitel (*.srt;*.ass;*.vtt)|*.srt;*.ass;*.vtt",
            CheckFileExists = true,
            Multiselect = true,
            InitialDirectory = initialDirectory
        };

        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileNames : null;
    }

    public string? SelectAttachment(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Text-Anhang auswaehlen",
            Filter = "Textdateien (*.txt)|*.txt",
            CheckFileExists = true,
            InitialDirectory = initialDirectory
        };

        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileName : null;
    }

    public string? SelectOutput(string initialDirectory, string fileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Zieldatei auswaehlen",
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

    public MessageBoxResult AskAudioDescriptionChoice()
    {
        return MessageBox.Show(
            GetOwner(),
            "Ja = AD-Datei manuell waehlen, Nein = AD-Datei leeren, Abbrechen = nichts aendern.",
            "AD-Datei korrigieren",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
    }

    public MessageBoxResult AskSubtitlesChoice()
    {
        return MessageBox.Show(
            GetOwner(),
            "Ja = Untertitel manuell waehlen, Nein = Untertitel leeren, Abbrechen = nichts aendern.",
            "Untertitel korrigieren",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
    }

    public MessageBoxResult AskAttachmentChoice()
    {
        return MessageBox.Show(
            GetOwner(),
            "Ja = Anhang manuell waehlen, Nein = Anhang leeren, Abbrechen = nichts aendern.",
            "Anhang korrigieren",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
    }

    public bool ConfirmMuxStart()
    {
        return MessageBox.Show(
            GetOwner(),
            "Soll der angezeigte mkvmerge-Aufruf jetzt ausgefuehrt werden?",
            "Muxing starten",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public bool ConfirmBatchStart(int itemCount)
    {
        return MessageBox.Show(
            GetOwner(),
            $"Sollen {itemCount} Episoden jetzt nacheinander verarbeitet werden?",
            "Batch starten",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
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
}