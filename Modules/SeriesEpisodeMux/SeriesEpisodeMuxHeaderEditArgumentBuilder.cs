namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

/// <summary>
/// Baut aus einem reinen Header-Edit-Plan die konkrete <c>mkvpropedit</c>-Argumentliste.
/// </summary>
internal static class SeriesEpisodeMuxHeaderEditArgumentBuilder
{
    /// <summary>
    /// Erzeugt die vollständige Argumentliste für direkte Tracknamen-Anpassungen an einer vorhandenen Zieldatei.
    /// </summary>
    /// <param name="plan">Vollständig aufgelöster Plan mit direkten Header-Anpassungen.</param>
    /// <returns>Argumentliste für <c>mkvpropedit.exe</c>.</returns>
    public static IReadOnlyList<string> Build(SeriesEpisodeMuxPlan plan)
    {
        if (!plan.HasTrackHeaderEdits)
        {
            throw new InvalidOperationException("Für diesen Plan sind keine direkten Header-Anpassungen hinterlegt.");
        }

        var arguments = new List<string>
        {
            plan.OutputFilePath
        };

        foreach (var headerEdit in plan.TrackHeaderEdits)
        {
            arguments.AddRange(
            [
                "--edit",
                headerEdit.Selector,
                "--set",
                $"name={headerEdit.ExpectedTrackName}"
            ]);
        }

        return arguments;
    }
}
