using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

public sealed class SeriesEpisodeMuxPlanner
{
    private static readonly HashSet<string> SupportedSubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt",
        ".ass",
        ".vtt"
    };

    private readonly MkvToolNixLocator _locator;
    private readonly MkvMergeProbeService _probeService;

    public SeriesEpisodeMuxPlanner(MkvToolNixLocator locator, MkvMergeProbeService probeService)
    {
        _locator = locator;
        _probeService = probeService;
    }

    public AutoDetectedEpisodeFiles DetectFromMainVideo(string mainVideoPath)
    {
        if (!File.Exists(mainVideoPath))
        {
            throw new FileNotFoundException($"Hauptvideo nicht gefunden: {mainVideoPath}");
        }

        if (LooksLikeAudioDescription(mainVideoPath))
        {
            throw new InvalidOperationException("Bitte die normale Episoden-Datei auswählen, nicht die Audiodeskriptions-Datei.");
        }

        var directory = Path.GetDirectoryName(mainVideoPath)
            ?? throw new InvalidOperationException("Der Ordner der Hauptdatei konnte nicht bestimmt werden.");

        var episodeKey = BuildEpisodeKey(mainVideoPath);
        var parsedName = ParseEpisodeName(mainVideoPath);
        var filesInDirectory = Directory.GetFiles(directory);
        var relatedFiles = filesInDirectory
            .Where(path => string.Equals(BuildEpisodeKey(path), episodeKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var audioDescriptionPath = relatedFiles
            .Where(path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            .Where(LooksLikeAudioDescription)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var subtitlePaths = relatedFiles
            .Where(path => SupportedSubtitleExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var exactAttachment = Path.ChangeExtension(mainVideoPath, ".txt");
        var attachmentPath = File.Exists(exactAttachment)
            ? exactAttachment
            : relatedFiles
                .Where(path => Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                .Where(path => !LooksLikeAudioDescription(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

        return new AutoDetectedEpisodeFiles(
            MainVideoPath: mainVideoPath,
            AudioDescriptionPath: audioDescriptionPath,
            SubtitlePaths: subtitlePaths,
            AttachmentPath: attachmentPath,
            SuggestedOutputFilePath: BuildSuggestedOutputPath(directory, parsedName),
            SuggestedTitle: parsedName.Title,
            SeriesName: parsedName.SeriesName,
            SeasonNumber: parsedName.SeasonNumber,
            EpisodeNumber: parsedName.EpisodeNumber);
    }

    public async Task<SeriesEpisodeMuxPlan> CreatePlanAsync(SeriesEpisodeMuxRequest request)
    {
        ValidateRequest(request);

        var subtitleFiles = request.SubtitlePaths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new SubtitleFile(path, SubtitleKind.FromExtension(Path.GetExtension(path))))
            .ToList();

        var mkvMergePath = _locator.FindMkvMergePath();
        var metadata = await _probeService.ReadPrimaryVideoMetadataAsync(mkvMergePath, request.MainVideoPath);
        int? audioDescriptionTrackId = string.IsNullOrWhiteSpace(request.AudioDescriptionPath)
            ? null
            : await _probeService.ReadFirstAudioTrackIdAsync(mkvMergePath, request.AudioDescriptionPath);

        return new SeriesEpisodeMuxPlan(
            mkvMergePath,
            request.OutputFilePath,
            request.Title,
            request.MainVideoPath,
            metadata.VideoTrackId,
            metadata.AudioTrackId,
            request.AudioDescriptionPath,
            audioDescriptionTrackId,
            subtitleFiles,
            request.AttachmentPath,
            BuildTrackMetadata(metadata));
    }

    private static void ValidateRequest(SeriesEpisodeMuxRequest request)
    {
        if (!File.Exists(request.MainVideoPath))
        {
            throw new FileNotFoundException($"Hauptvideo nicht gefunden: {request.MainVideoPath}");
        }

        if (!string.IsNullOrWhiteSpace(request.AudioDescriptionPath) && !File.Exists(request.AudioDescriptionPath))
        {
            throw new FileNotFoundException($"AD-Datei nicht gefunden: {request.AudioDescriptionPath}");
        }

        if (!string.IsNullOrWhiteSpace(request.AttachmentPath) && !File.Exists(request.AttachmentPath))
        {
            throw new FileNotFoundException($"Text-Anhang nicht gefunden: {request.AttachmentPath}");
        }

        foreach (var subtitlePath in request.SubtitlePaths)
        {
            if (!File.Exists(subtitlePath))
            {
                throw new FileNotFoundException($"Untertiteldatei nicht gefunden: {subtitlePath}");
            }
        }

        var outputDirectory = Path.GetDirectoryName(request.OutputFilePath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            throw new DirectoryNotFoundException($"Ausgabeordner nicht gefunden: {outputDirectory}");
        }
    }

    private static EpisodeNameParts ParseEpisodeName(string filePath)
    {
        var normalizedName = NormalizeNameForParsing(Path.GetFileNameWithoutExtension(filePath));
        var splitIndex = normalizedName.IndexOf(" - ", StringComparison.Ordinal);

        if (splitIndex < 0)
        {
            return new EpisodeNameParts("Unbekannte Serie", normalizedName, "xx", "xx");
        }

        var seriesName = normalizedName[..splitIndex].Trim();
        var titlePart = normalizedName[(splitIndex + 3)..].Trim();
        var seasonNumber = "xx";
        var episodeNumber = "xx";

        var shortPatternMatch = Regex.Match(titlePart, @"\(S(?<season>\d{1,2})_E(?<episode>\d{1,2})\)", RegexOptions.IgnoreCase);
        if (shortPatternMatch.Success)
        {
            seasonNumber = shortPatternMatch.Groups["season"].Value.PadLeft(2, '0');
            episodeNumber = shortPatternMatch.Groups["episode"].Value.PadLeft(2, '0');
            titlePart = titlePart.Replace(shortPatternMatch.Value, string.Empty).Trim();
        }
        else
        {
            var longPatternMatch = Regex.Match(titlePart, @"\(Staffel\s*(?<season>\d{1,2})\s*,\s*Folge\s*(?<episode>\d{1,2})\)", RegexOptions.IgnoreCase);
            if (longPatternMatch.Success)
            {
                seasonNumber = longPatternMatch.Groups["season"].Value.PadLeft(2, '0');
                episodeNumber = longPatternMatch.Groups["episode"].Value.PadLeft(2, '0');
                titlePart = titlePart.Replace(longPatternMatch.Value, string.Empty).Trim();
            }
        }

        titlePart = Regex.Replace(titlePart, @"\s+", " ").Trim();
        titlePart = string.IsNullOrWhiteSpace(titlePart) ? "Unbekannter Titel" : titlePart;

        return new EpisodeNameParts(seriesName, titlePart, seasonNumber, episodeNumber);
    }

    private static string BuildSuggestedOutputPath(string directory, EpisodeNameParts parsedName)
    {
        var fileName = $"{parsedName.SeriesName} - S{parsedName.SeasonNumber}E{parsedName.EpisodeNumber} - {parsedName.Title}.mkv";
        return Path.Combine(directory, SanitizeFileName(fileName));
    }

    private static string BuildEpisodeKey(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        name = Regex.Replace(name, @"-\d+$", string.Empty);
        name = Regex.Replace(name, @"\(\s*Audiodeskrip[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\bAudiodeskription\b", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\bAD\b", string.Empty, RegexOptions.IgnoreCase);
        name = NormalizeSeparators(name);
        name = Regex.Replace(name, @"\s+", " ").Trim();
        return name.ToLowerInvariant();
    }

    private static string NormalizeNameForParsing(string name)
    {
        name = Regex.Replace(name, @"-\d+$", string.Empty);
        name = Regex.Replace(name, @"\(\s*Audiodeskrip[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\bAudiodeskription\b", string.Empty, RegexOptions.IgnoreCase);
        name = NormalizeSeparators(name);
        name = Regex.Replace(name, @"\s+", " ").Trim();

        var firstHyphenIndex = name.IndexOf('-', StringComparison.Ordinal);
        if (firstHyphenIndex >= 0)
        {
            name = name[..firstHyphenIndex] + " - " + name[(firstHyphenIndex + 1)..];
        }

        return Regex.Replace(name, @"\s+", " ").Trim();
    }

    private static string NormalizeSeparators(string value)
    {
        var normalized = value.Replace("–", "-").Replace("—", "-");
        normalized = Regex.Replace(normalized, @"\s*-\s*$", string.Empty);
        return normalized;
    }

    private static bool LooksLikeAudioDescription(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return fileName.Contains("audiodeskrip", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(fileName, @"(?:^|[^a-z])AD(?:[^a-z]|$)", RegexOptions.IgnoreCase);
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(fileName.Select(character => invalidCharacters.Contains(character) ? '_' : character));
    }

    private static EpisodeTrackMetadata BuildTrackMetadata(MediaTrackMetadata metadata)
    {
        var videoTrackName = $"Deutsch - {metadata.ResolutionLabel.Value} - {metadata.VideoCodecLabel}";

        return new EpisodeTrackMetadata(
            VideoTrackName: videoTrackName,
            AudioTrackName: "Deutsch - AAC",
            AudioDescriptionTrackName: "Deutsch (sehbehinderte) - AAC");
    }

    private sealed record EpisodeNameParts(string SeriesName, string Title, string SeasonNumber, string EpisodeNumber);
}