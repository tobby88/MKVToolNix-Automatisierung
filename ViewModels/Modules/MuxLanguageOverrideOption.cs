namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Auswahlwert für manuelle Sprachkorrekturen im Mux-Review.
/// </summary>
internal sealed record MuxLanguageOverrideOption(string Code, string DisplayName);

/// <summary>
/// Stellt die bewusst unterstützten manuellen Sprachoptionen bereit.
/// </summary>
internal static class MuxLanguageOverrideOptions
{
    public static IReadOnlyList<MuxLanguageOverrideOption> All { get; } =
    [
        new(string.Empty, "Automatisch"),
        new("de", "Deutsch"),
        new("en", "English"),
        new("nds", "Plattdüütsch")
    ];
}
