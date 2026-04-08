using System.Text;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Hält zuletzt berechnete Episodenpläne anhand der aktuellen Eingabeeigenschaften wiederverwendbar vor.
/// </summary>
internal sealed class EpisodePlanCache
{
    private static readonly HashSet<string> DetectionRelevantExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".txt",
        ".srt",
        ".ass",
        ".vtt",
        ".ttml"
    };

    private readonly Dictionary<object, CachedEpisodePlan> _cachedPlans = new(ReferenceEqualityComparer.Instance);
    private readonly object _syncRoot = new();

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

        var cacheKey = BuildCacheKey(CreateSnapshot(input));
        lock (_syncRoot)
        {
            if (_cachedPlans.TryGetValue(owner, out var cachedPlan)
                && string.Equals(cachedPlan.CacheKey, cacheKey, StringComparison.Ordinal))
            {
                plan = cachedPlan.Plan;
                return true;
            }
        }

        plan = null;
        return false;
    }

    /// <summary>
    /// Prüft cachefähige Pläne, ohne den potenziell teuren Ordnerzustand auf dem UI-Thread zu lesen.
    /// </summary>
    /// <param name="owner">Stabile Besitzer-Instanz, zum Beispiel ein ViewModel oder Batch-Eintrag.</param>
    /// <param name="input">Aktuelle Plan-Eingaben des Besitzers.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal für die Hintergrund-Key-Erzeugung.</param>
    /// <returns>Der wiederverwendbare Plan oder <see langword="null"/>, wenn kein Treffer vorliegt.</returns>
    public async Task<SeriesEpisodeMuxPlan?> TryGetAsync(
        object owner,
        IEpisodePlanInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(input);

        var cacheKey = await BuildCacheKeyAsync(CreateSnapshot(input), cancellationToken);
        lock (_syncRoot)
        {
            if (_cachedPlans.TryGetValue(owner, out var cachedPlan)
                && string.Equals(cachedPlan.CacheKey, cacheKey, StringComparison.Ordinal))
            {
                return cachedPlan.Plan;
            }

            return null;
        }
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

        var cacheKey = BuildCacheKey(CreateSnapshot(input));
        lock (_syncRoot)
        {
            _cachedPlans[owner] = new CachedEpisodePlan(cacheKey, plan);
        }
    }

    /// <summary>
    /// Speichert einen Plan, ohne den vollständigen Quellordner synchron auf dem UI-Thread zu scannen.
    /// </summary>
    /// <param name="owner">Stabile Besitzer-Instanz des Plans.</param>
    /// <param name="input">Eingaben, zu denen der Plan berechnet wurde.</param>
    /// <param name="plan">Berechneter Plan.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal für die Hintergrund-Key-Erzeugung.</param>
    public async Task StoreAsync(
        object owner,
        IEpisodePlanInput input,
        SeriesEpisodeMuxPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(plan);

        var cacheKey = await BuildCacheKeyAsync(CreateSnapshot(input), cancellationToken);
        lock (_syncRoot)
        {
            _cachedPlans[owner] = new CachedEpisodePlan(cacheKey, plan);
        }
    }

    /// <summary>
    /// Verwirft den zuletzt gespeicherten Plan eines einzelnen Besitzers.
    /// </summary>
    /// <param name="owner">Besitzer, dessen Plan entfernt werden soll.</param>
    public void Invalidate(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_syncRoot)
        {
            _cachedPlans.Remove(owner);
        }
    }

    /// <summary>
    /// Leert den gesamten Cache.
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            _cachedPlans.Clear();
        }
    }

    private static EpisodePlanCacheKeyInput CreateSnapshot(IEpisodePlanInput input)
    {
        return new EpisodePlanCacheKeyInput(
            input.HasPrimaryVideoSource,
            input.MainVideoPath,
            input.AudioDescriptionPath,
            input.SubtitlePaths.ToList(),
            input.AttachmentPaths.ToList(),
            input.ManualAttachmentPaths.ToList(),
            input.OutputPath,
            input.TitleForMux,
            input.ExcludedSourcePaths.ToList(),
            input.PlannedVideoPaths.ToList(),
            input.DetectionNotes.ToList());
    }

    private static Task<string> BuildCacheKeyAsync(
        EpisodePlanCacheKeyInput input,
        CancellationToken cancellationToken)
    {
        if (!RequiresDetectionDirectoryScan(input))
        {
            return Task.FromResult(BuildCacheKey(input, cancellationToken));
        }

        return Task.Run(() => BuildCacheKey(input, cancellationToken), cancellationToken);
    }

    private static string BuildCacheKey(
        EpisodePlanCacheKeyInput input,
        CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();
        AppendValue(builder, input.HasPrimaryVideoSource ? "primary-video-present" : "archive-primary-required");
        AppendFileValue(builder, input.MainVideoPath);
        AppendPlanSourceState(builder, input, cancellationToken);
        AppendFileValue(builder, input.AudioDescriptionPath);
        AppendFileValues(builder, input.SubtitlePaths);
        AppendFileValues(builder, input.AttachmentPaths);
        AppendFileValues(builder, input.ManualAttachmentPaths);
        AppendFileValue(builder, input.OutputPath);
        AppendValue(builder, input.TitleForMux);
        AppendValues(builder, input.ExcludedSourcePaths);
        AppendFileValues(builder, input.PlannedVideoPaths);
        AppendValues(builder, input.DetectionNotes);
        return builder.ToString();
    }

    private static void AppendPlanSourceState(
        StringBuilder builder,
        EpisodePlanCacheKeyInput input,
        CancellationToken cancellationToken)
    {
        if (!RequiresDetectionDirectoryScan(input))
        {
            builder.Append("planned-videos-fixed");
            builder.Append('\u001F');
            return;
        }

        AppendDetectionDirectoryState(builder, input.MainVideoPath, cancellationToken);
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

    private static void AppendDetectionDirectoryState(
        StringBuilder builder,
        string? mainVideoPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceDirectory = string.IsNullOrWhiteSpace(mainVideoPath)
            ? null
            : Path.GetDirectoryName(mainVideoPath);
        AppendValue(builder, sourceDirectory);

        // Dieser teure Fallback ist nur noch nötig, wenn die spätere Planerzeugung tatsächlich erneut
        // über eine frische Detection des Quellordners laufen würde. Sobald das UI bereits konkrete
        // PlannedVideoPaths und DetectionNotes liefert, reicht deren eigener Dateistand im Cache-Schlüssel.
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            builder.Append("no-source-directory");
            builder.Append('\u001F');
            return;
        }

        try
        {
            if (!Directory.Exists(sourceDirectory))
            {
                builder.Append("missing-directory");
                builder.Append('\u001F');
                return;
            }

            builder.Append('[');
            foreach (var filePath in Directory.EnumerateFiles(sourceDirectory)
                .Where(IsDetectionRelevantFile)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendFileValue(builder, filePath);
            }

            builder.Append(']');
        }
        catch (IOException)
        {
            builder.Append("unavailable-directory");
        }
        catch (UnauthorizedAccessException)
        {
            builder.Append("unavailable-directory");
        }

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

    private static bool IsDetectionRelevantFile(string path)
    {
        return DetectionRelevantExtensions.Contains(Path.GetExtension(path));
    }

    private static bool RequiresDetectionDirectoryScan(EpisodePlanCacheKeyInput input)
    {
        return input.HasPrimaryVideoSource
            && input.PlannedVideoPaths.Count == 0;
    }

    private sealed record CachedEpisodePlan(string CacheKey, SeriesEpisodeMuxPlan Plan);

    private sealed record EpisodePlanCacheKeyInput(
        bool HasPrimaryVideoSource,
        string MainVideoPath,
        string? AudioDescriptionPath,
        IReadOnlyList<string> SubtitlePaths,
        IReadOnlyList<string> AttachmentPaths,
        IReadOnlyList<string> ManualAttachmentPaths,
        string OutputPath,
        string TitleForMux,
        IReadOnlyCollection<string> ExcludedSourcePaths,
        IReadOnlyList<string> PlannedVideoPaths,
        IReadOnlyList<string> DetectionNotes);
}
