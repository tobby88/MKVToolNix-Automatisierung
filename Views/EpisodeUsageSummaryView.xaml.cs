using System.Windows.Controls;

namespace MkvToolnixAutomatisierung.Views;

/// <summary>
/// Kapselt die read-only Darstellung der geplanten Verwendung, damit Einzel- und Batch-Modus
/// dieselbe UI-Struktur teilen und Detailänderungen nur an einer Stelle gepflegt werden müssen.
/// </summary>
public partial class EpisodeUsageSummaryView : UserControl
{
    public EpisodeUsageSummaryView()
    {
        InitializeComponent();
    }
}
