using System.Text;
using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

// Dieser Partial enthält alle string- und metadatenbasierten Heuristiken für Dateinamen, TXT-Begleitdateien und Mojibake-Reparatur.
public sealed partial class SeriesEpisodeMuxPlanner
{
    private EpisodeIdentity ParseEpisodeIdentity(string filePath, CompanionTextMetadata textMetadata)
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
            episodeNumber);
    }

    private static EpisodeNameParts ParseEpisodeName(string filePath)
    {
        var normalizedName = StripPresentationMarkers(Path.GetFileNameWithoutExtension(filePath));
        var splitIndex = normalizedName.IndexOf(" - ", StringComparison.Ordinal);

        if (splitIndex < 0)
        {
            return new EpisodeNameParts("Unbekannte Serie", NormalizeNameForParsing(normalizedName), "xx", "xx");
        }

        var seriesName = NormalizeSeriesName(normalizedName[..splitIndex]);
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

    internal static Match? FindEpisodePattern(string? text)
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

    private static string NormalizeNameForParsing(string name)
    {
        name = StripPresentationMarkers(name);
        name = NormalizeSeparators(name);
        return Regex.Replace(name, @"\s+", " ").Trim();
    }

    private static string StripPresentationMarkers(string name)
    {
        name = NormalizeDashCharacters(name);
        name = Regex.Replace(name, @"-\d+$", string.Empty);
        name = Regex.Replace(name, @"\(\s*Audiodeskrip[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\bAudiodeskription\b", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\bAD\b", string.Empty, RegexOptions.IgnoreCase);
        return Regex.Replace(name, @"\s+", " ").Trim();
    }

    internal static string NormalizeEpisodeTitle(string? value)
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

    internal static string NormalizeSeparators(string value)
    {
        var normalized = NormalizeDashCharacters(value);

        // Nur echte Trenner mit angrenzendem Leerraum werden vereinheitlicht; Bindestriche innerhalb von Namen bleiben erhalten.
        normalized = Regex.Replace(normalized, @"(?:(?<=\S)\s+-\s*(?=\S)|(?<=\S)\s*-\s+(?=\S))", " - ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"\s*[-:]\s*$", string.Empty);
        return normalized;
    }

    private static string NormalizeDashCharacters(string value)
    {
        return MojibakeRepair.NormalizeLikelyMojibake(value)
            .Replace("\u2013", "-")
            .Replace("\u2014", "-")
            .Replace("\u2212", "-");
    }

    private static string BuildSeriesIdentityKey(string seriesName)
    {
        return Regex.Replace(NormalizeSeparators(seriesName), @"\s+", " ").Trim().ToLowerInvariant();
    }

    private static string BuildTitleIdentityKey(string title)
    {
        return Regex.Replace(NormalizeEpisodeTitle(title), @"\s+", " ").Trim().ToLowerInvariant();
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

    private sealed record EpisodeDetectionContext(
        string Directory,
        EpisodeIdentity EpisodeIdentity,
        IReadOnlyList<NormalVideoCandidate> NormalCandidates,
        NormalVideoCandidate? PrimaryVideoCandidate,
        IReadOnlyList<NormalVideoCandidate> SelectedVideoCandidates,
        IReadOnlyList<string> SubtitlePaths,
        IReadOnlyList<string> RelatedFilePaths,
        IReadOnlyList<AudioDescriptionCandidate> AudioDescriptionCandidates);

    internal sealed record CandidateSeed(
        string FilePath,
        string? AttachmentPath,
        CompanionTextMetadata TextMetadata,
        EpisodeIdentity Identity);

    internal sealed record EpisodeSeedCollection(
        IReadOnlyList<CandidateSeed> AllEpisodeVideoSeeds,
        IReadOnlyList<CandidateSeed> NormalVideoSeeds,
        IReadOnlyList<CandidateSeed> AudioDescriptionSeeds);

    internal sealed record EpisodeIdentity(string SeriesName, string Title, string SeasonNumber, string EpisodeNumber)
    {
        public bool Matches(EpisodeIdentity other)
        {
            if (!string.Equals(
                    SeriesEpisodeMuxPlanner.BuildSeriesIdentityKey(SeriesName),
                    SeriesEpisodeMuxPlanner.BuildSeriesIdentityKey(other.SeriesName),
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (HasKnownEpisodeCode && other.HasKnownEpisodeCode)
            {
                return string.Equals(SeasonNumber, other.SeasonNumber, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(EpisodeNumber, other.EpisodeNumber, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(
                SeriesEpisodeMuxPlanner.BuildTitleIdentityKey(Title),
                SeriesEpisodeMuxPlanner.BuildTitleIdentityKey(other.Title),
                StringComparison.Ordinal);
        }

        private bool HasKnownEpisodeCode => SeasonNumber != "xx" && EpisodeNumber != "xx";
    }
    internal sealed record TitleDetails(string Title, string SeasonNumber, string EpisodeNumber);
    internal sealed record EpisodeNameParts(string SeriesName, string Title, string SeasonNumber, string EpisodeNumber);

    internal abstract record EpisodeCandidateBase(EpisodeIdentity Identity, string Sender, int? DurationSeconds);

    internal sealed record NormalVideoCandidate(
        string FilePath,
        EpisodeIdentity Identity,
        string Sender,
        int? DurationSeconds,
        int VideoWidth,
        string VideoCodecLabel,
        string VideoLanguage,
        string AudioCodecLabel,
        long FileSizeBytes,
        IReadOnlyList<string> SubtitlePaths,
        string? AttachmentPath) : EpisodeCandidateBase(Identity, Sender, DurationSeconds);

    internal sealed record AudioDescriptionCandidate(
        string FilePath,
        EpisodeIdentity Identity,
        string Sender,
        int? DurationSeconds,
        long FileSizeBytes,
        string? AttachmentPath) : EpisodeCandidateBase(Identity, Sender, DurationSeconds);
}

