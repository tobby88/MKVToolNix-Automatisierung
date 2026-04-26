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
        new("nds", "Plattdüütsch"),
        new("da", "Dansk"),
        new("es", "Español"),
        new("fi", "Suomi"),
        new("fr", "Français"),
        new("it", "Italiano"),
        new("nl", "Nederlands"),
        new("no", "Norsk"),
        new("pl", "Polski"),
        new("pt", "Português"),
        new("ru", "Русский"),
        new("sv", "Svenska"),
        new("tr", "Türkçe"),
        new("uk", "Українська"),
        new("ja", "日本語"),
        new("ko", "한국어"),
        new("zh", "中文")
    ];
}
