using System.Text.RegularExpressions;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Bündelt serienbezogene Ausnahmen für das Matroska-Flag <c>Originalsprache</c>.
/// </summary>
internal static partial class SeriesOriginalLanguageRules
{
    private const string NoSingleOriginalLanguageSeries = "Der Kommissar und das Meer";

    /// <summary>
    /// Bestimmt den erwarteten Wert für <c>--original-flag</c> bzw. <c>flag-original</c>.
    /// </summary>
    /// <remarks>
    /// Standardfall: Die Spur ist original, wenn ihre Sprache der TVDB-Originalsprache entspricht.
    /// Sonderfall: Bei <c>Der Kommissar und das Meer</c> wurden mehrere Rollensprachen als
    /// Originalton verwendet und die jeweils anderen Rollen synchronisiert. Deshalb bekommt dort
    /// bewusst keine Spur das Originalsprache-Flag. Die Fortsetzung <c>Der Kommissar und der See</c>
    /// ist davon nicht betroffen.
    /// </remarks>
    /// <param name="trackLanguageCode">Sprachcode der Spur, z. B. <c>de</c>, <c>en</c> oder <c>nds</c>.</param>
    /// <param name="seriesOriginalLanguage">Originalsprache laut TVDB oder gespeicherten Metadaten.</param>
    /// <param name="seriesContext">Serienname, Dateiname oder vollständiger MKV-Pfad.</param>
    /// <returns><c>yes</c> oder <c>no</c> für mkvmerge.</returns>
    public static string ResolveOriginalFlag(
        string? trackLanguageCode,
        string? seriesOriginalLanguage,
        string? seriesContext = null)
    {
        if (SuppressesOriginalLanguageFlag(seriesContext))
        {
            return "no";
        }

        if (string.IsNullOrWhiteSpace(seriesOriginalLanguage))
        {
            return "yes";
        }

        var normalizedOriginal = NormalizeOriginalLanguageCode(seriesOriginalLanguage);
        var normalizedTrack = string.IsNullOrWhiteSpace(trackLanguageCode)
            ? "de"
            : NormalizeOriginalLanguageCode(trackLanguageCode);
        return string.Equals(normalizedTrack, normalizedOriginal, StringComparison.Ordinal) ? "yes" : "no";
    }

    /// <summary>
    /// Liefert den erwarteten booleschen Headerwert für vorhandene Archivspuren.
    /// </summary>
    public static bool? BuildExpectedOriginalFlag(
        string? trackLanguageCode,
        string? seriesOriginalLanguage,
        string? seriesContext = null)
    {
        if (SuppressesOriginalLanguageFlag(seriesContext))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(seriesOriginalLanguage))
        {
            return null;
        }

        return string.Equals(
            ResolveOriginalFlag(trackLanguageCode, seriesOriginalLanguage),
            "yes",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool SuppressesOriginalLanguageFlag(string? seriesContext)
    {
        return ExtractSeriesCandidates(seriesContext)
            .Select(NormalizeSeriesName)
            .Any(candidate => string.Equals(
                candidate,
                NormalizeSeriesName(NoSingleOriginalLanguageSeries),
                StringComparison.Ordinal));
    }

    private static IEnumerable<string> ExtractSeriesCandidates(string? seriesContext)
    {
        if (string.IsNullOrWhiteSpace(seriesContext))
        {
            yield break;
        }

        var trimmed = seriesContext.Trim();
        yield return trimmed;

        var stem = Path.GetFileNameWithoutExtension(trimmed);
        if (!string.IsNullOrWhiteSpace(stem))
        {
            yield return stem;

            var match = EpisodeFileNamePattern().Match(stem);
            if (match.Success)
            {
                yield return match.Groups["series"].Value;
            }
        }

        var directoryName = Path.GetFileName(Path.GetDirectoryName(trimmed));
        if (!string.IsNullOrWhiteSpace(directoryName))
        {
            yield return directoryName;
        }

        var parentDirectory = Path.GetDirectoryName(Path.GetDirectoryName(trimmed));
        var parentDirectoryName = string.IsNullOrWhiteSpace(parentDirectory)
            ? null
            : Path.GetFileName(parentDirectory);
        if (!string.IsNullOrWhiteSpace(parentDirectoryName))
        {
            yield return parentDirectoryName;
        }
    }

    private static string NormalizeSeriesName(string value)
    {
        return WhitespacePattern()
            .Replace(value.Trim().ToLowerInvariant(), " ");
    }

    private static string NormalizeOriginalLanguageCode(string languageCode)
    {
        var normalized = languageCode.Trim().ToLowerInvariant().Replace('_', '-');
        return MediaLanguageHelper.TryNormalizeKnownMuxLanguageCode(normalized) ?? normalized;
    }

    [GeneratedRegex(@"^(?<series>.+?)\s+-\s+S(?:\d{2,4}|xx)E(?:\d{2}(?:-E?\d{2})?|xx)", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeFileNamePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
