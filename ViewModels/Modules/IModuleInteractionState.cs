using System.ComponentModel;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Meldet der Shell, ob ein Modul gerade einen zusammenhängenden Vorgang ausführt.
/// Währenddessen bleiben Konfiguration und Modulwahl gesperrt, damit keine zweite
/// Ansicht dieselben Dateien oder die zugrunde liegenden Zugangsdaten verändert.
/// </summary>
internal interface IModuleInteractionState : INotifyPropertyChanged
{
    /// <summary>Gibt an, ob das Modul neue Benutzervorgänge annehmen kann.</summary>
    bool IsInteractive { get; }
}
