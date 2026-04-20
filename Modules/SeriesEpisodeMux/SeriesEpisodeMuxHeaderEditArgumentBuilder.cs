namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

/// <summary>
/// Baut aus einem reinen Header-Edit-Plan die konkrete <c>mkvpropedit</c>-Argumentliste.
/// </summary>
internal static class SeriesEpisodeMuxHeaderEditArgumentBuilder
{
    /// <summary>
    /// Erzeugt die vollständige Argumentliste für direkte Header-Anpassungen an einer vorhandenen Zieldatei.
    /// </summary>
    /// <param name="plan">Vollständig aufgelöster Plan mit direkten Header-Anpassungen.</param>
    /// <returns>Argumentliste für <c>mkvpropedit.exe</c>.</returns>
    public static IReadOnlyList<string> Build(SeriesEpisodeMuxPlan plan)
    {
        if (!plan.HasHeaderEdits)
        {
            throw new InvalidOperationException("Für diesen Plan sind keine direkten Header-Anpassungen hinterlegt.");
        }

        var arguments = new List<string>
        {
            plan.OutputFilePath
        };

        if (plan.ContainerTitleEdit is not null)
        {
            arguments.AddRange(
            [
                "--edit",
                "info",
                "--set",
                $"title={plan.ContainerTitleEdit.ExpectedTitle}"
            ]);
        }

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
