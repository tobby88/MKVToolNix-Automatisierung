using System.Text.RegularExpressions;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Kleine zentrale Heuristiken für Episodendateinamen, damit Benennung und AD-Erkennung nicht im Projekt verteilt sind.
/// </summary>
internal static class EpisodeFileNameHelper
{
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
        return string.Concat(fileName.Select(character => invalidCharacters.Contains(character) ? '_' : character));
    }
}
