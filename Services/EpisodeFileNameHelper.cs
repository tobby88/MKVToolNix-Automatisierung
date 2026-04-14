using System.Text.RegularExpressions;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Kleine zentrale Heuristiken für Episodendateinamen, damit Benennung und AD-Erkennung nicht im Projekt verteilt sind.
/// </summary>
internal static class EpisodeFileNameHelper
{
    private static readonly Regex EpisodeRangePattern = new(
        @"^\s*(?:E)?(?<start>\d{1,4})\s*-\s*(?:E)?(?<end>\d{1,4})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Windows behandelt Gerätedateinamen unabhängig von der Schreibweise als reserviert.
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    public static bool LooksLikeAudioDescription(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return fileName.Contains("audiodeskrip", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(fileName, @"(?:^|[^a-z])AD(?:[^a-z]|$)", RegexOptions.IgnoreCase);
    }

    public static string NormalizeEpisodeNumber(string? value)
    {
        // Doppelfolgen werden projektweit als "05-E06" geführt, damit die bestehende
        // "SxxEyy"-Benennung mit minimalen Eingriffen zu "S2014E05-E06" erweiterbar bleibt.
        if (TryNormalizeEpisodeRange(value, out var normalizedRange))
        {
            return normalizedRange;
        }

        return int.TryParse(value, out var number) && number >= 0
            ? number.ToString("00")
            : "xx";
    }

    public static string NormalizeSeasonNumber(string? value)
    {
        return int.TryParse(value, out var number) && number >= 0
            ? number.ToString("00")
            : "xx";
    }

    public static bool IsEpisodeRange(string? value)
    {
        return TryNormalizeEpisodeRange(value, out _);
    }

    public static string BuildEpisodeCode(string seasonNumber, string episodeNumber)
    {
        return $"S{NormalizeSeasonNumber(seasonNumber)}E{NormalizeEpisodeNumber(episodeNumber)}";
    }

    public static string BuildEpisodeFileName(string seriesName, string seasonNumber, string episodeNumber, string title)
    {
        return SanitizeFileName(
            $"{seriesName} - {BuildEpisodeCode(seasonNumber, episodeNumber)} - {title}.mkv");
    }

    public static string SanitizeFileName(string fileName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var fileNameWithReadableColons = Regex.Replace(fileName, @"\s*:\s*", " - ");
        var sanitized = string.Concat(fileNameWithReadableColons.Select(character => invalidCharacters.Contains(character) ? '_' : character));
        var trimmedValue = sanitized.TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(trimmedValue))
        {
            return "_";
        }

        var extension = Path.GetExtension(trimmedValue);
        var stem = Path.GetFileNameWithoutExtension(trimmedValue).TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "_";
        }

        return AppendReservedNameSuffixIfNeeded(stem) + extension;
    }

    public static string SanitizePathSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var valueWithReadableColons = Regex.Replace(value, @"\s*:\s*", " - ");
        var sanitized = string.Concat(valueWithReadableColons.Select(character =>
            invalidCharacters.Contains(character)
            || character == Path.DirectorySeparatorChar
            || character == Path.AltDirectorySeparatorChar
                ? '_'
                : character));
        var trimmedValue = sanitized.TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(trimmedValue))
        {
            return "_";
        }

        return AppendReservedNameSuffixIfNeeded(trimmedValue);
    }

    private static string AppendReservedNameSuffixIfNeeded(string value)
    {
        var extension = Path.GetExtension(value);
        var stem = string.IsNullOrWhiteSpace(extension)
            ? value
            : Path.GetFileNameWithoutExtension(value);
        var reservedNameProbe = stem.TrimEnd(' ', '.');
        if (!ReservedDeviceNames.Contains(reservedNameProbe))
        {
            return value;
        }

        return string.IsNullOrWhiteSpace(extension)
            ? value + "_"
            : reservedNameProbe + "_" + extension;
    }

    private static bool TryNormalizeEpisodeRange(string? value, out string normalizedRange)
    {
        normalizedRange = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Akzeptiert sowohl "05-E06" als auch benutzerfreundliche Kurzformen wie "5-6".
        var match = EpisodeRangePattern.Match(value);
        if (!match.Success)
        {
            return false;
        }

        var normalizedStart = NormalizeSeasonNumber(match.Groups["start"].Value);
        var normalizedEnd = NormalizeSeasonNumber(match.Groups["end"].Value);
        if (normalizedStart == "xx" || normalizedEnd == "xx")
        {
            return false;
        }

        normalizedRange = normalizedStart == normalizedEnd
            ? normalizedStart
            : $"{normalizedStart}-E{normalizedEnd}";
        return true;
    }
}
