using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Kapselt die Entscheidung, wann ein manuell geänderter Episodentitel bei neuer Erkennung erhalten bleiben soll.
/// </summary>
internal static class SingleEpisodeManualTitlePolicy
{
    /// <summary>
    /// Behaelt einen manuellen Titel nur dann, wenn die Auswahl weiterhin dieselbe Episode betrifft.
    /// </summary>
    /// <param name="currentTitle">Aktuell sichtbarer Titel im Editor.</param>
    /// <param name="lastSuggestedTitle">Zuletzt automatisch vorgeschlagener Titel.</param>
    /// <param name="detectionSeedPath">Datei, von der die letzte Erkennung ausging.</param>
    /// <param name="mainVideoPath">Aktuell ausgewählte Hauptquelle.</param>
    /// <param name="selectedVideoPath">Neu ausgewählte Datei für die Erkennung.</param>
    /// <returns><see langword="true"/>, wenn der manuelle Titel erhalten bleiben soll.</returns>
    public static bool ShouldPreserve(
        string? currentTitle,
        string? lastSuggestedTitle,
        string? detectionSeedPath,
        string? mainVideoPath,
        string selectedVideoPath)
    {
        if (string.IsNullOrWhiteSpace(currentTitle)
            || string.Equals(currentTitle, lastSuggestedTitle, StringComparison.Ordinal))
        {
            return false;
        }

        return PathComparisonHelper.AreSamePath(selectedVideoPath, detectionSeedPath)
            || PathComparisonHelper.AreSamePath(selectedVideoPath, mainVideoPath);
    }
}
