using System.Text;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Hält zuletzt berechnete Episodenpläne anhand der aktuellen Eingabeeigenschaften wiederverwendbar vor.
/// </summary>
public sealed class EpisodePlanCache
{
    private readonly Dictionary<object, CachedEpisodePlan> _cachedPlans = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Prüft, ob für den angegebenen Besitzer bereits ein zu den aktuellen Eingaben passender Plan im Cache liegt.
    /// </summary>
    /// <param name="owner">Stabile Besitzer-Instanz, zum Beispiel ein ViewModel oder Batch-Eintrag.</param>
    /// <param name="input">Aktuelle Plan-Eingaben des Besitzers.</param>
    /// <param name="plan">Bei Treffer der wiederverwendbare Plan.</param>
    /// <returns><see langword="true"/>, wenn ein passender Plan vorliegt.</returns>
    public bool TryGet(object owner, IEpisodePlanInput input, out SeriesEpisodeMuxPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(input);

        if (_cachedPlans.TryGetValue(owner, out var cachedPlan)
            && string.Equals(cachedPlan.CacheKey, BuildCacheKey(input), StringComparison.Ordinal))
        {
            plan = cachedPlan.Plan;
            return true;
        }

        plan = null;
        return false;
    }

    /// <summary>
    /// Legt einen Plan für den angegebenen Besitzer anhand der aktuellen Eingaben im Cache ab.
    /// </summary>
    /// <param name="owner">Stabile Besitzer-Instanz des Plans.</param>
    /// <param name="input">Eingaben, zu denen der Plan berechnet wurde.</param>
    /// <param name="plan">Berechneter Plan.</param>
    public void Store(object owner, IEpisodePlanInput input, SeriesEpisodeMuxPlan plan)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(plan);

        _cachedPlans[owner] = new CachedEpisodePlan(BuildCacheKey(input), plan);
    }

    /// <summary>
    /// Verwirft den zuletzt gespeicherten Plan eines einzelnen Besitzers.
    /// </summary>
    /// <param name="owner">Besitzer, dessen Plan entfernt werden soll.</param>
    public void Invalidate(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _cachedPlans.Remove(owner);
    }

    /// <summary>
    /// Leert den gesamten Cache.
    /// </summary>
    public void Clear()
    {
        _cachedPlans.Clear();
    }

    private static string BuildCacheKey(IEpisodePlanInput input)
    {
        var builder = new StringBuilder();
        AppendFileValue(builder, input.MainVideoPath);
        AppendFileValue(builder, input.AudioDescriptionPath);
        AppendFileValues(builder, input.SubtitlePaths);
        AppendFileValues(builder, input.AttachmentPaths);
        AppendFileValues(builder, input.ManualAttachmentPaths);
        AppendFileValue(builder, input.OutputPath);
        AppendValue(builder, input.TitleForMux);
        AppendValues(builder, input.ExcludedSourcePaths);
        return builder.ToString();
    }

    private static void AppendFileValues(StringBuilder builder, IEnumerable<string> values)
    {
        builder.Append('[');
        foreach (var value in values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            AppendFileValue(builder, value);
        }

        builder.Append(']');
    }

    private static void AppendValues(StringBuilder builder, IEnumerable<string> values)
    {
        builder.Append('[');
        // Reihenfolge aus der UI soll Cache-Treffer nicht zerstören, solange dieselben Dateien gewählt sind.
        foreach (var value in values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            AppendValue(builder, value);
        }

        builder.Append(']');
    }

    private static void AppendFileValue(StringBuilder builder, string? value)
    {
        AppendValue(builder, value);
        AppendFileState(builder, value);
    }

    private static void AppendValue(StringBuilder builder, string? value)
    {
        // Trennzeichen außerhalb normaler Dateipfade hält den Schlüssel stabil, ohne zusätzliche Escaping-Logik.
        builder.Append(value?.Trim() ?? string.Empty);
        builder.Append('\u001F');
    }

    private static void AppendFileState(StringBuilder builder, string? path)
    {
        // Der UI-nahe Cache soll sich selbst verwerfen, sobald eine relevante Eingabedatei oder das Ziel
        // außerhalb der App verändert wurde.
        if (string.IsNullOrWhiteSpace(path))
        {
            builder.Append("none");
            builder.Append('\u001F');
            return;
        }

        try
        {
            if (!File.Exists(path))
            {
                builder.Append("missing");
                builder.Append('\u001F');
                return;
            }

            var fileInfo = new FileInfo(path);
            builder.Append(fileInfo.Length);
            builder.Append('|');
            builder.Append(fileInfo.LastWriteTimeUtc.Ticks);
        }
        catch (IOException)
        {
            builder.Append("unavailable");
        }
        catch (UnauthorizedAccessException)
        {
            builder.Append("unavailable");
        }

        builder.Append('\u001F');
    }

    private sealed record CachedEpisodePlan(string CacheKey, SeriesEpisodeMuxPlan Plan);
}
