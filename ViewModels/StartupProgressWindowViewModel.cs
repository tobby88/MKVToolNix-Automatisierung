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
    /// Benutzerlesbarer Fortschrittstext neben dem Balken.
    /// </summary>
    public string ProgressText => IsIndeterminate ? "läuft..." : $"{ProgressPercent:0}%";

    /// <inheritdoc />
    public void Report(ManagedToolStartupProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);

        StatusText = value.StatusText;
        DetailText = string.IsNullOrWhiteSpace(value.DetailText)
            ? "Bitte warten..."
            : value.DetailText!;
        IsIndeterminate = value.IsIndeterminate;
        ProgressPercent = value.ProgressPercent ?? 0d;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
