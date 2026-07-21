using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.ViewModels;

/// <summary>
/// Sichtbarer Startstatus der App, solange Werkzeuge geprüft oder aktualisiert werden.
/// </summary>
internal sealed class StartupProgressWindowViewModel : INotifyPropertyChanged, IProgress<ManagedToolStartupProgress>
{
    private string _statusText = "Werkzeuge werden vorbereitet...";
    private string _detailText = "Initialisiere den Startvorgang.";
    private string _progressLabel = "Gesamt";
    private double _progressPercent;
    private bool _isIndeterminate = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Hauptstatus der laufenden Startaktion.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Ergänzender Detailtext, z. B. Werkzeugname oder Bytestand.
    /// </summary>
    public string DetailText
    {
        get => _detailText;
        private set
        {
            if (_detailText == value)
            {
                return;
            }

            _detailText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Prozentfortschritt für determinate Schritte.
    /// </summary>
    public double ProgressPercent
    {
        get => _progressPercent;
        private set
        {
            if (Math.Abs(_progressPercent - value) < 0.01d)
            {
                return;
            }

            _progressPercent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressText));
        }
    }

    /// <summary>
    /// Kennzeichnet, ob der Fortschrittsbalken determiniert angezeigt werden kann.
    /// </summary>
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set
        {
            if (_isIndeterminate == value)
            {
                return;
            }

            _isIndeterminate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressText));
        }
    }

    /// <summary>
    /// Bezeichnet den Vorgang, dessen Prozentwert der Balken aktuell misst.
    /// </summary>
    public string ProgressLabel
    {
        get => _progressLabel;
        private set
        {
            if (_progressLabel == value)
            {
                return;
            }

            _progressLabel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressText));
        }
    }

    /// <summary>
    /// Benutzerlesbarer Fortschritt des aktuell bezeichneten Vorgangs neben dem Balken.
    /// Eine feste Nachkommastelle verhindert Breitenwechsel zwischen ganzzahligen und gebrochenen Werten.
    /// </summary>
    public string ProgressText => IsIndeterminate ? "läuft..." : $"{ProgressLabel} {ProgressPercent:0.0}%";

    /// <inheritdoc />
    public void Report(ManagedToolStartupProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);

        StatusText = value.StatusText;
        DetailText = string.IsNullOrWhiteSpace(value.DetailText)
            ? "Bitte warten..."
            : value.DetailText!;
        ProgressLabel = string.IsNullOrWhiteSpace(value.ProgressLabel)
            ? "Fortschritt"
            : value.ProgressLabel;
        IsIndeterminate = value.IsIndeterminate;
        ProgressPercent = value.ProgressPercent ?? 0d;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
