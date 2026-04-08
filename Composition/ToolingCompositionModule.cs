using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Verdrahtet die Tool-Locatoren und Probe-Services für mkvmerge und ffprobe.
/// </summary>
internal static class ToolingCompositionModule
{
    /// <summary>
    /// Erstellt die Werkzeug-Services auf Basis der gespeicherten Toolpfade.
    /// </summary>
    public static ToolingServices Create(AppSettingStores stores)
    {
        return new ToolingServices(
            new MkvToolNixLocator(stores.ToolPaths),
            new FfprobeLocator(stores.ToolPaths),
            new MkvMergeProbeService());
    }
}
