using System.Text.RegularExpressions;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Kleine zentrale Heuristiken für Episodendateinamen, damit Benennung und AD-Erkennung nicht im Projekt verteilt sind.
/// </summary>
internal static class EpisodeFileNameHelper
{
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
        return int.TryParse(value, out var number) && number >= 0
            ? number.ToString("00")
            : "xx";
    }

    public static string BuildEpisodeFileName(string seriesName, string seasonNumber, string episodeNumber, string title)
    {
        return SanitizeFileName(
            $"{seriesName} - S{NormalizeEpisodeNumber(seasonNumber)}E{NormalizeEpisodeNumber(episodeNumber)} - {title}.mkv");
    }

    public static string SanitizeFileName(string fileName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(fileName.Select(character => invalidCharacters.Contains(character) ? '_' : character));
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
        var sanitized = string.Concat(value.Select(character =>
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
        return ReservedDeviceNames.Contains(value)
            ? value + "_"
            : value;
    }
}
