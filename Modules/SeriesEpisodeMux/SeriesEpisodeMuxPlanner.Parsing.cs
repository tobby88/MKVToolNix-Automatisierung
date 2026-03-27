using System.Text;
using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

public sealed partial class SeriesEpisodeMuxPlanner
{
    private EpisodeIdentity ParseEpisodeIdentity(string filePath, TextMetadata textMetadata)
    {
        var fileNameParts = ParseEpisodeName(filePath);
        var txtTitleParts = ParseTitleDetails(textMetadata.Title);

        var seriesName = !string.IsNullOrWhiteSpace(textMetadata.Topic)
            ? NormalizeSeriesName(textMetadata.Topic!)
            : NormalizeSeriesName(fileNameParts.SeriesName);

        var title = !string.IsNullOrWhiteSpace(txtTitleParts.Title)
            ? txtTitleParts.Title
            : fileNameParts.Title;

        var seasonNumber = fileNameParts.SeasonNumber != "xx"
            ? fileNameParts.SeasonNumber
            : txtTitleParts.SeasonNumber;

        var episodeNumber = fileNameParts.EpisodeNumber != "xx"
            ? fileNameParts.EpisodeNumber
            : txtTitleParts.EpisodeNumber;

        title = string.IsNullOrWhiteSpace(title) ? "Unbekannter Titel" : title;
        seriesName = string.IsNullOrWhiteSpace(seriesName) ? "Unbekannte Serie" : seriesName;

        return new EpisodeIdentity(
            seriesName,
            title,
            seasonNumber,
            episodeNumber,
            BuildIdentityKey(seriesName, title));
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
        var titleDetails = ParseTitleDetails(normalizedName[(splitIndex + 3)..]);
        return new EpisodeNameParts(seriesName, titleDetails.Title, titleDetails.SeasonNumber, titleDetails.EpisodeNumber);
    }

    private static string NormalizeSeriesName(string rawSeriesName)
    {
        var normalized = NormalizeNameForParsing(rawSeriesName);
        normalized = RemoveEditorialLabels(normalized);
        return normalized.Trim();
    }

    private static TitleDetails ParseTitleDetails(string? rawTitle)
    {
        var titlePart = NormalizeEpisodeTitle(rawTitle);
        var seasonNumber = "xx";
        var episodeNumber = "xx";

        var episodeMatch = FindEpisodePattern(rawTitle);
        if (episodeMatch is not null)
        {
            seasonNumber = episodeMatch.Groups["season"].Value.PadLeft(2, '0');
            episodeNumber = episodeMatch.Groups["episode"].Value.PadLeft(2, '0');
        }

        titlePart = string.IsNullOrWhiteSpace(titlePart) ? "Unbekannter Titel" : titlePart;
        return new TitleDetails(titlePart, seasonNumber, episodeNumber);
    }

    private static Match? FindEpisodePattern(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var patterns = new[]
        {
            @"\(S(?<season>\d{1,4})\s*_\s*E(?<episode>\d{1,4})\)",
            @"\(S(?<season>\d{1,4})\s*/\s*E(?<episode>\d{1,4})\)",
            @"\(Staffel\s*(?<season>\d{1,4})\s*,\s*Folge\s*(?<episode>\d{1,4})\)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match;
            }
        }

        return null;
    }

    private static string BuildIdentityKey(string seriesName, string title)
    {
        var key = $"{NormalizeSeparators(seriesName)} - {NormalizeEpisodeTitle(title)}";
        key = Regex.Replace(key, @"\s+", " ").Trim();
        return key.ToLowerInvariant();
    }

    private static string NormalizeNameForParsing(string name)
    {
        name = Regex.Replace(name, @"-\d+$", string.Empty);
        name = Regex.Replace(name, @"\(\s*Audiodeskrip[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\bAudiodeskription\b", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\bAD\b", string.Empty, RegexOptions.IgnoreCase);
        name = NormalizeSeparators(name);

        var firstHyphenIndex = name.IndexOf('-', StringComparison.Ordinal);
        if (firstHyphenIndex >= 0)
        {
            name = name[..firstHyphenIndex] + " - " + name[(firstHyphenIndex + 1)..];
        }

        return Regex.Replace(name, @"\s+", " ").Trim();
    }

    private static string NormalizeEpisodeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = NormalizeSeparators(value);
        normalized = RemoveEditorialLabels(normalized);
        normalized = Regex.Replace(normalized, @"\(S\d{1,4}\s*[_/]\s*E\d{1,4}\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(Staffel\s*\d{1,4}\s*,\s*Folge\s*\d{1,4}\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(\s*Audiodeskrip[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bAudiodeskription\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"\s*[-:]\s*$", string.Empty);
        return normalized;
    }

    private static string RemoveEditorialLabels(string value)
    {
        var normalized = Regex.Replace(value, @"\s*-\s*Neue Folgen?\b.*$", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"^\s*Der Samstagskrimi\s*-\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s*-\s*Der Samstagskrimi\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"^\s*Der Samstagskrimi\s*$", string.Empty, RegexOptions.IgnoreCase);
        return normalized.Trim();
    }

    private static string NormalizeSeparators(string value)
    {
        var normalized = RepairMojibake(value)
            .Replace("\u2013", "-")
            .Replace("\u2014", "-")
            .Replace("\u2212", "-");

        normalized = Regex.Replace(normalized, @"\s*-\s*", " - ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"\s*[-:]\s*$", string.Empty);
        return normalized;
    }

    private static string NormalizeSender(string? sender)
    {
        return string.IsNullOrWhiteSpace(sender) ? "Unbekannt" : sender.Trim();
    }

    private static bool IsSrfSender(string? sender)
    {
        return string.Equals(sender?.Trim(), "SRF", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetSenderPriority(string? sender)
    {
        var normalized = sender?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized == "ZDF")
        {
            return 0;
        }

        if (normalized == "ARD" || normalized == "DAS ERSTE")
        {
            return 1;
        }

        if (normalized == "RBB")
        {
            return 2;
        }

        if (normalized == "ARTE")
        {
            return 3;
        }

        if (normalized == "SRF")
        {
            return 9;
        }

        return 5;
    }

    private int? ReadDurationSeconds(string filePath, TimeSpan? fallbackDuration)
    {
        var duration = _durationProbe.TryReadDuration(filePath) ?? fallbackDuration;
        if (duration is null)
        {
            return null;
        }

        return (int)Math.Round(duration.Value.TotalSeconds);
    }

    private static TextMetadata ReadTextMetadata(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return TextMetadata.Empty;
        }

        var content = ReadTextWithFallback(filePath);
        var sender = ReadLabeledValue(content, "Sender");
        var topic = ReadLabeledValue(content, "Thema");
        var title = ReadLabeledValue(content, "Titel");
        var durationText = ReadLabeledValue(content, "Dauer");
        var duration = TimeSpan.TryParse(durationText, out var parsedDuration) ? (TimeSpan?)parsedDuration : null;

        return new TextMetadata(sender, topic, title, duration);
    }

    private static string ReadTextWithFallback(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var utf8 = DecodeText(bytes, Encoding.UTF8);
        if (!LooksLikeMojibake(utf8))
        {
            return utf8;
        }

        var repairedUtf8 = RepairMojibake(utf8);
        if (!LooksLikeMojibake(repairedUtf8))
        {
            return repairedUtf8;
        }

        var latin1 = DecodeText(bytes, Encoding.Latin1);
        var repairedLatin1 = RepairMojibake(latin1);
        return string.IsNullOrWhiteSpace(repairedLatin1) ? latin1 : repairedLatin1;
    }

    private static string DecodeText(byte[] bytes, Encoding encoding)
    {
        try
        {
            return encoding.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string RepairMojibake(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !LooksLikeMojibake(value))
        {
            return value;
        }

        try
        {
            var repaired = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(value));
            return string.IsNullOrWhiteSpace(repaired) ? value : repaired;
        }
        catch
        {
            return value;
        }
    }

    private static bool LooksLikeMojibake(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (value.Contains("\u00C3", StringComparison.Ordinal)
                || value.Contains("\u00E2", StringComparison.Ordinal)
                || value.Contains("\u00C2", StringComparison.Ordinal)
                || value.Contains('\uFFFD'));
    }

    private static string? ReadLabeledValue(string content, string label)
    {
        var match = Regex.Match(content, $@"^{Regex.Escape(label)}\s*:\s*(.+)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private sealed record EpisodeDetectionContext(
        string Directory,
        EpisodeIdentity EpisodeIdentity,
        IReadOnlyList<NormalVideoCandidate> NormalCandidates,
        NormalVideoCandidate PrimaryVideoCandidate,
        IReadOnlyList<NormalVideoCandidate> SelectedVideoCandidates,
        IReadOnlyList<string> SubtitlePaths,
        IReadOnlyList<string> RelatedFilePaths,
        IReadOnlyList<AudioDescriptionCandidate> AudioDescriptionCandidates);

    private sealed record TextMetadata(string? Sender, string? Topic, string? Title, TimeSpan? Duration)
    {
        public static TextMetadata Empty { get; } = new(null, null, null, null);
    }

    private sealed record CandidateSeed(
        string FilePath,
        string? AttachmentPath,
        TextMetadata TextMetadata,
        EpisodeIdentity Identity);

    private sealed record EpisodeSeedCollection(
        IReadOnlyList<CandidateSeed> AllEpisodeVideoSeeds,
        IReadOnlyList<CandidateSeed> NormalVideoSeeds,
        IReadOnlyList<CandidateSeed> AudioDescriptionSeeds);

    private sealed record EpisodeIdentity(string SeriesName, string Title, string SeasonNumber, string EpisodeNumber, string Key);
    private sealed record TitleDetails(string Title, string SeasonNumber, string EpisodeNumber);
    private sealed record EpisodeNameParts(string SeriesName, string Title, string SeasonNumber, string EpisodeNumber);

    private abstract record EpisodeCandidateBase(EpisodeIdentity Identity, string Sender, int? DurationSeconds);

    private sealed record NormalVideoCandidate(
        string FilePath,
        EpisodeIdentity Identity,
        string Sender,
        int? DurationSeconds,
        int VideoWidth,
        string VideoCodecLabel,
        string AudioCodecLabel,
        long FileSizeBytes,
        IReadOnlyList<string> SubtitlePaths,
        string? AttachmentPath) : EpisodeCandidateBase(Identity, Sender, DurationSeconds);

    private sealed record AudioDescriptionCandidate(
        string FilePath,
        EpisodeIdentity Identity,
        string Sender,
        int? DurationSeconds,
        long FileSizeBytes,
        string? AttachmentPath) : EpisodeCandidateBase(Identity, Sender, DurationSeconds);
}

