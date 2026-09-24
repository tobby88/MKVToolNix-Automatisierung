using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Windows;

/// <summary>Explizite Sichtung und einzelne bestätigte Aktionen auf bekannte Arbeitsreste.</summary>
public partial class RecoveryMaintenanceWindow : Window
{
    private readonly RecoveryMaintenanceService _service;
    private CancellationTokenSource? _scan;
    private bool _busy;
    private string? _root;

    internal RecoveryMaintenanceWindow(AppToolPathSettings settings)
    {
        InitializeComponent();
        _service = new RecoveryMaintenanceService(
            [settings.ManagedMkvToolNix.InstalledPath, settings.ManagedFfprobe.InstalledPath,
             settings.ManagedMediathekView.InstalledPath, settings.FfprobePath, settings.MkvToolNixDirectoryPath,
             settings.MediathekViewPath, PortableAppStorage.ImdbDatabaseFilePath],
            System.IO.Path.Combine(PortableAppStorage.ToolsDirectory, "mediathekview"));
        Closing += (_, e) => { if (_busy) { _scan?.Cancel(); e.Cancel = true; } };
    }

    private async void Tools_Click(object sender, RoutedEventArgs e) => await ScanAsync(PortableAppStorage.ToolsDirectory);
    private async void Imdb_Click(object sender, RoutedEventArgs e) => await ScanAsync(PortableAppStorage.ImdbDataDirectory);
    private async void Choose_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Ordner mit möglichen Arbeitsresten auswählen" };
        if (picker.ShowDialog(this) == true) await ScanAsync(picker.FolderName);
    }

    private async Task ScanAsync(string root)
    {
        using var cancellation = new CancellationTokenSource();
        _scan = cancellation;
        SetBusy(true);
        EntriesGrid.ItemsSource = null;
        StatusText.Text = "Arbeitsreste werden geprüft...";
        try
        {
            var entries = await Task.Run(() => _service.Scan(root, cancellation.Token), cancellation.Token);
            _root = root;
            EntriesGrid.ItemsSource = entries;
            StatusText.Text = $"{entries.Count} bekannte Arbeitsreste. Aktive Installationen und Links sind ausgeschlossen.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Prüfung abgebrochen. Es wurde nichts geändert."; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { _scan = null; SetBusy(false); }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e) => await ApplyAsync(restore: true);
    private async void Recycle_Click(object sender, RoutedEventArgs e) => await ApplyAsync(restore: false);
    private async Task ApplyAsync(bool restore)
    {
        if (EntriesGrid.SelectedItem is not RecoveryEntry entry || _busy) return;
        var question = restore
            ? $"Metadaten aus dieser Sicherung wiederherstellen? Der jetzige Zustand wird zusätzlich gesichert.\n\nZiel: {entry.RestoreTarget}\nSicherung: {entry.Path}"
            : $"Diesen Arbeitsrest in den Papierkorb verschieben? Ordner können Benutzerdaten enthalten.\n\n{entry.Path}";
        if (MessageBox.Show(this, question, "Einzelaktion bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        SetBusy(true);
        try
        {
            var result = await Task.Run(() =>
            {
                if (restore) return _service.RestoreMetadata(entry);
                _service.Recycle(entry);
                return "Arbeitsrest in den Papierkorb verschoben.";
            });
            if (_root is not null) await ScanAsync(_root);
            StatusText.Text = result;
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesGrid.SelectedItem is not RecoveryEntry entry) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.Path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _scan?.Cancel();
    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => UpdateActions();
    private void SetBusy(bool busy)
    {
        _busy = busy;
        ScanButtons.IsEnabled = !busy;
        CancelScanButton.IsEnabled = busy && _scan is not null;
        UpdateActions();
    }
    private void UpdateActions()
    {
        if (OpenButton is null) return;
        OpenButton.IsEnabled = RecycleButton.IsEnabled = !_busy && EntriesGrid.SelectedItem is RecoveryEntry;
        RestoreButton.IsEnabled = !_busy && EntriesGrid.SelectedItem is RecoveryEntry { CanRestore: true };
    }
}
