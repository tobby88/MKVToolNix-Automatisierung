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

        return Build(plan.OutputFilePath, plan.ContainerTitleEdit, plan.TrackHeaderEdits);
    }

    /// <summary>
    /// Erzeugt eine <c>mkvpropedit</c>-Argumentliste aus bereits geplanten Header-Operationen,
    /// ohne dafür einen vollständigen Mux-Plan vorauszusetzen.
    /// </summary>
    /// <param name="filePath">Zu bearbeitende MKV-Datei.</param>
    /// <param name="containerTitleEdit">Optionale Container-Titelkorrektur.</param>
    /// <param name="trackHeaderEdits">Optionale Track-Header-Korrekturen.</param>
    /// <returns>Argumentliste für <c>mkvpropedit.exe</c>.</returns>
    public static IReadOnlyList<string> Build(
        string filePath,
        ContainerTitleEditOperation? containerTitleEdit,
        IReadOnlyList<TrackHeaderEditOperation> trackHeaderEdits)
    {
        if (containerTitleEdit is null && trackHeaderEdits.Count == 0)
        {
            throw new InvalidOperationException("Für direkte Header-Anpassungen muss mindestens eine Änderung hinterlegt sein.");
        }

        var arguments = new List<string>
        {
            filePath
        };

        if (containerTitleEdit is not null)
        {
            arguments.AddRange(
            [
                "--edit",
                "info",
                "--set",
                $"title={containerTitleEdit.ExpectedTitle}"
            ]);
        }

        foreach (var headerEdit in trackHeaderEdits)
        {
            arguments.AddRange(
            [
                "--edit",
                headerEdit.Selector
            ]);

            foreach (var valueEdit in ResolveValueEdits(headerEdit))
            {
                arguments.AddRange(
                [
                    "--set",
                    $"{valueEdit.PropertyName}={valueEdit.ExpectedMkvPropEditValue}"
                ]);
            }
        }

        return arguments;
    }

    private static IReadOnlyList<TrackHeaderValueEdit> ResolveValueEdits(TrackHeaderEditOperation headerEdit)
    {
        return headerEdit.ValueEdits is { Count: > 0 }
            ? headerEdit.ValueEdits
            :
            [
                new TrackHeaderValueEdit(
                    "name",
                    "Name",
                    headerEdit.CurrentTrackName,
                    headerEdit.ExpectedTrackName,
                    headerEdit.ExpectedTrackName)
            ];
    }
}
