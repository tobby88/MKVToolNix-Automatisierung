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
        var hasExplicitTxtTitle = !string.IsNullOrWhiteSpace(textMetadata.Title);
        if ((string.IsNullOrWhiteSpace(textMetadata.Topic) || IsGenericMetadataTopic(textMetadata.Topic))
            && TryExtractSeriesPrefixFromTitle(textMetadata.Title, out var titleSeriesName, out var titlePrefixedParts))
        {
            txtTitleParts = titlePrefixedParts;
            fileNameParts = fileNameParts with { SeriesName = titleSeriesName };
        }

        var hasSpecificTextTopic = !string.IsNullOrWhiteSpace(textMetadata.Topic)
            && !IsGenericMetadataTopic(textMetadata.Topic);
        var seriesName = hasSpecificTextTopic
            ? NormalizeSeriesName(textMetadata.Topic!)
            : NormalizeSeriesName(fileNameParts.SeriesName);

        // Leere oder fehlende TXT-Metadaten dürfen keinen unbekannten Platzhalter über den
        // sauber erkannten Dateinamen legen. Der TXT-Titel hat nur dann Vorrang, wenn er
        // tatsächlich explizit vorhanden war.
        var title = hasExplicitTxtTitle && !string.IsNullOrWhiteSpace(txtTitleParts.Title)
            ? txtTitleParts.Title
            : fileNameParts.Title;

        var seasonNumber = fileNameParts.SeasonNumber != "xx"
            ? fileNameParts.SeasonNumber
            : txtTitleParts.SeasonNumber;

        var episodeNumber = fileNameParts.EpisodeNumber != "xx"
            ? fileNameParts.EpisodeNumber
            : txtTitleParts.EpisodeNumber;

        title = RemoveRepeatedSeriesPrefixFromTitle(title, seriesName);
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

        if (TryParseLegacyAudioDescriptionEpisodeName(normalizedName, out var legacyAudioDescriptionParts))
        {
            return legacyAudioDescriptionParts;
        }

        if (TryParseEpisodeNameWithLeadingEpisodeLabel(normalizedName, out var labeledEpisodeParts))
        {
            return labeledEpisodeParts;
        }

        if (TryParseEpisodeNameWithLeadingSeriesRubric(normalizedName, out var rubricEpisodeParts))
        {
            return rubricEpisodeParts;
        }

        var splitIndex = normalizedName.IndexOf(" - ", StringComparison.Ordinal);

        if (splitIndex < 0)
        {
            // Aeltere Mediathek-Dateien nutzen teilweise keinen sauber umgebrochenen Trenner
            // zwischen Serienname und Titel. Wenn der Dateiname mit dem Serienordner beginnt,
            // nehmen wir diesen Ordnernamen konservativ als Serienprefix-Fallback.
            if (TryParseEpisodeNameFromSeriesDirectory(filePath, normalizedName, out var directoryFallback))
            {
                return directoryFallback;
            }

            return new EpisodeNameParts("Unbekannte Serie", NormalizeNameForParsing(normalizedName), "xx", "xx");
        }

        var seriesName = NormalizeSeriesName(normalizedName[..splitIndex]);
        var titleDetails = ParseTitleDetails(normalizedName[(splitIndex + 3)..]);
        return new EpisodeNameParts(seriesName, titleDetails.Title, titleDetails.SeasonNumber, titleDetails.EpisodeNumber);
    }

    private static bool TryParseLegacyAudioDescriptionEpisodeName(
        string normalizedName,
        out EpisodeNameParts episodeNameParts)
    {
        episodeNameParts = default!;

        // Manche aelteren AD-Dateien kommen als "Serie-Hoerfassung_ Titel - Der Samstagskrimi ..."
        // ohne normalen "Serie - Titel"-Trenner. Diese Struktur erkennen wir vor der generischen
        // Split-Logik gezielt, damit Serie und Episodentitel nicht vertauscht werden.
        var legacyMatch = Regex.Match(
            normalizedName,
            @"^(?<series>.+?)-H(?:ö|oe)rfassung[_:]\s*(?<title>.+)$",
            RegexOptions.IgnoreCase);
        if (!legacyMatch.Success)
        {
            return false;
        }

        var seriesName = NormalizeSeriesName(legacyMatch.Groups["series"].Value);
        var titleDetails = ParseTitleDetails(legacyMatch.Groups["title"].Value);
        if (string.IsNullOrWhiteSpace(seriesName) || string.IsNullOrWhiteSpace(titleDetails.Title))
        {
            return false;
        }

        episodeNameParts = new EpisodeNameParts(
            seriesName,
            titleDetails.Title,
            titleDetails.SeasonNumber,
            titleDetails.EpisodeNumber);
        return true;
    }

    private static bool TryParseEpisodeNameWithLeadingEpisodeLabel(
        string normalizedName,
        out EpisodeNameParts episodeNameParts)
    {
        episodeNameParts = default!;

        // ARD/RBB-Dateien wie
        // "Die Heiland - Wir sind Anwalt-Folge 22_ Die Waffe im Müll (S03_E10)"
        // nutzen den Seriennamen selbst mit Leerzeichen-Bindestrich, trennen die Folge
        // danach aber nur mit "-Folge ...". Die generische " - "-Trennung würde sonst
        // "Die Heiland" als Serie und "Wir sind Anwalt-..." als Titel fehlinterpretieren.
        var match = Regex.Match(
            normalizedName,
            @"^(?<series>.+?)-\s*Folge\s+\d+\s*[_:]\s*(?<title>.+)$",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        var seriesName = NormalizeSeriesName(match.Groups["series"].Value);
        var titleDetails = ParseTitleDetails(match.Groups["title"].Value);
        if (string.IsNullOrWhiteSpace(seriesName) || string.IsNullOrWhiteSpace(titleDetails.Title))
        {
            return false;
        }

        episodeNameParts = new EpisodeNameParts(
            seriesName,
            titleDetails.Title,
            titleDetails.SeasonNumber,
            titleDetails.EpisodeNumber);
        return true;
    }

    private static bool TryParseEpisodeNameWithLeadingSeriesRubric(
        string normalizedName,
        out EpisodeNameParts episodeNameParts)
    {
        episodeNameParts = default!;

        // Rubriken wie "Backstage" oder "Der Samstagskrimi" stehen gelegentlich vor dem
        // eigentlichen Seriennamen. Ohne diese gezielte Erkennung würde z. B.
        // "Backstage-SOKO Leipzig_ Am Filmset ..." als Serie "Backstage" statt "SOKO Leipzig"
        // in Detection und TVDB-Matching laufen.
        var match = Regex.Match(
            normalizedName,
            @"^(?<rubric>Backstage|Der Samstagskrimi|Hallo Deutschland|Riverboat)-(?<series>.+?)[_:]\s*(?<title>.+)$",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        var seriesName = NormalizeSeriesName(match.Groups["series"].Value);
        var titleDetails = ParseTitleDetails(match.Groups["title"].Value);
        if (string.IsNullOrWhiteSpace(seriesName)
            || string.IsNullOrWhiteSpace(titleDetails.Title)
            || IsGenericMetadataTopic(seriesName))
        {
            return false;
        }

        episodeNameParts = new EpisodeNameParts(
            seriesName,
            titleDetails.Title,
            titleDetails.SeasonNumber,
            titleDetails.EpisodeNumber);
        return true;
    }

    private static bool TryParseEpisodeNameFromSeriesDirectory(
        string filePath,
        string normalizedName,
        out EpisodeNameParts episodeNameParts)
    {
        episodeNameParts = default!;

        var directoryPath = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        var directoryName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var normalizedSeriesName = NormalizeSeriesName(directoryName);
        if (string.IsNullOrWhiteSpace(normalizedSeriesName)
            || !normalizedName.StartsWith(normalizedSeriesName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var titleRemainder = normalizedName[normalizedSeriesName.Length..];
        titleRemainder = Regex.Replace(titleRemainder, @"^\s*[-:_]\s*", string.Empty);
        titleRemainder = NormalizeNameForParsing(titleRemainder);
        if (string.IsNullOrWhiteSpace(titleRemainder))
        {
            return false;
        }

        var titleDetails = ParseTitleDetails(titleRemainder);
        episodeNameParts = new EpisodeNameParts(
            normalizedSeriesName,
            titleDetails.Title,
            titleDetails.SeasonNumber,
            titleDetails.EpisodeNumber);
        return true;
    }

    private static string NormalizeSeriesName(string rawSeriesName)
    {
        var normalized = NormalizeNameForParsing(rawSeriesName);
        normalized = RemoveEditorialLabels(normalized);
        return normalized.Trim();
    }

    private static bool IsGenericMetadataTopic(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return false;
        }

        return BuildSeriesIdentityKey(topic) is "filme" or "film" or "backstage" or "der samstagskrimi" or "hallo deutschland" or "riverboat";
    }

    private static bool TryExtractSeriesPrefixFromTitle(
        string? rawTitle,
        out string seriesName,
        out TitleDetails titleDetails)
    {
        seriesName = string.Empty;
        titleDetails = new TitleDetails("Unbekannter Titel", "xx", "xx");

        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return false;
        }

        var match = Regex.Match(rawTitle, @"^(?<series>[^:]+?)\s*:\s*(?<title>.+)$");
        if (!match.Success)
        {
            return false;
        }

        var candidateSeriesName = NormalizeSeriesName(match.Groups["series"].Value);
        var candidateTitleDetails = ParseTitleDetails(match.Groups["title"].Value);
        if (string.IsNullOrWhiteSpace(candidateSeriesName)
            || IsGenericMetadataTopic(candidateSeriesName)
            || string.IsNullOrWhiteSpace(candidateTitleDetails.Title)
            || string.Equals(candidateTitleDetails.Title, "Unbekannter Titel", StringComparison.Ordinal))
        {
            return false;
        }

        seriesName = candidateSeriesName;
        titleDetails = candidateTitleDetails;
        return true;
    }

    private static TitleDetails ParseTitleDetails(string? rawTitle)
    {
        var titlePart = NormalizeEpisodeTitle(rawTitle);
        var seasonNumber = "xx";
        var episodeNumber = "xx";

        var episodeMatch = FindEpisodePattern(rawTitle);
        if (episodeMatch is not null)
        {
            seasonNumber = EpisodeFileNameHelper.NormalizeSeasonNumber(episodeMatch.Groups["season"].Value);
            episodeNumber = EpisodeFileNameHelper.NormalizeEpisodeNumber(episodeMatch.Groups["episode"].Value);
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
            @"\bS(?<season>\d{1,4})\s*E(?<episode>\d{1,4}(?:\s*-\s*(?:E)?\d{1,4})?)\b",
            @"\(S(?<season>\d{1,4})\s*_\s*E(?<episode>\d{1,4}(?:\s*-\s*(?:E)?\d{1,4})?)\)",
            @"\(S(?<season>\d{1,4})\s*/\s*E(?<episode>\d{1,4}(?:\s*-\s*(?:E)?\d{1,4})?)\)",
            @"\(Staffel\s*(?<season>\d{1,4})\s*,\s*Folge\s*(?<episode>\d{1,4}(?:\s*-\s*\d{1,4})?)\)"
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
        name = Regex.Replace(name, @"\(\s*mit\s+Audiodeskrip[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\(\s*Audiodeskrip[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\(\s*Audiodeskrip[^)]*$", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\(\s*H(?:ö|oe)rfassung\s*\)", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\bAudiodeskription\b", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\bAudiodeskrip\w*\b", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\bH(?:ö|oe)rfassung\b", string.Empty, RegexOptions.IgnoreCase);
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
        normalized = Regex.Replace(normalized, @"^\s*Folge\s+\d+\s*[_:]\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"^\s*S\d{1,4}\s*E\d{1,4}(?:\s*-\s*(?:E)?\d{1,4})?\s*[-:]\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(S\d{1,4}\s*[_/]\s*E\d{1,4}\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(S\d{1,4}\s*[_/]\s*E\d{1,4}(?:\s*-\s*(?:E)?\d{1,4})?\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(Staffel\s*\d{1,4}\s*,\s*Folge\s*\d{1,4}\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(Staffel\s*\d{1,4}\s*,\s*Folge\s*\d{1,4}(?:\s*-\s*\d{1,4})?\)", string.Empty, RegexOptions.IgnoreCase);
        // Manche Mediatheken hängen erst den Reihenzusatz und danach den Episodencode an.
        // Deshalb läuft die Label-Bereinigung nach dem Entfernen der Codes bewusst erneut.
        normalized = RemoveEditorialLabels(normalized);
        normalized = Regex.Replace(normalized, @"\(\s*mit\s+Audiodeskrip[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(\s*Audiodeskrip[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(\s*H(?:ö|oe)rfassung\s*\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bAudiodeskription\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bH(?:ö|oe)rfassung\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"\s*[-:]\s*$", string.Empty);
        return normalized;
    }

    private static string RemoveEditorialLabels(string value)
    {
        var normalized = Regex.Replace(value, @"\s*-\s*Neue Folgen?\b.*$", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"^\s*Filme\s*-\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"^\s*Der Samstagskrimi\s*-\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s*-\s*Der Samstagskrimi\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"^\s*Der Samstagskrimi\s*$", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"^\s*Kurzfilm\s*-\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s*-\s*Kurzfilm\b.*$", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"^\s*Kurzfilm\s*$", string.Empty, RegexOptions.IgnoreCase);
        // Einige Mediathek-Einträge hängen die Serien-/Reihenzuordnung an den Episodentitel,
        // z. B. "Der Nachtalb - aus der Reihe _Die Toten vom Bodensee_" oder
        // "Der Seelenkreis - aus der Krimireihe _Die Toten vom Bodensee_". Für
        // Dateiname und TVDB-Matching zählt hier nur der eigentliche Episodentitel.
        normalized = Regex.Replace(
            normalized,
            @"\s*[-:]\s*aus\s+der\s+(?:\p{L}+)?reihe\b(?:\s*[:\-]?\s*[_""'„“]?[^_""'„“]+[_""'„“]?)?\s*$",
            string.Empty,
            RegexOptions.IgnoreCase);
        // "Büttenwarder op Platt" ist bei NDR-Dateien keine eigene Episode, sondern
        // ein redaktioneller Sprach-/Rubrikvorsatz im Titel. Ohne diese gezielte
        // Bereinigung laufen normale, AD- und Platt-Quellen derselben Folge als
        // getrennte Episoden auseinander und werden nach einem Batch-Skip nicht
        // gemeinsam in den Done-/Papierkorb-Cleanup aufgenommen.
        normalized = Regex.Replace(
            normalized,
            @"^\s*Büttenwarder\s+op\s+Platt\s*[-:_]\s*",
            string.Empty,
            RegexOptions.IgnoreCase);
        return normalized.Trim();
    }

    /// <summary>
    /// Entfernt einen redundant vorangestellten Seriennamen aus dem Episodentitel.
    /// Das verbessert Dateinamen und TVDB-Matching bei Quellen wie
    /// "Pippi Langstrumpf: Pippi und die Seeräuber 2. Teil".
    /// </summary>
    private static string RemoveRepeatedSeriesPrefixFromTitle(string title, string seriesName)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(seriesName))
        {
            return title;
        }

        var normalizedTitle = NormalizeDashCharacters(title).Trim();
        var normalizedSeriesName = NormalizeDashCharacters(seriesName).Trim();
        if (string.IsNullOrWhiteSpace(normalizedSeriesName))
        {
            return normalizedTitle;
        }

        var trimmed = Regex.Replace(
            normalizedTitle,
            $"^\\s*{Regex.Escape(normalizedSeriesName)}\\s*(?::|-|_)\\s*",
            string.Empty,
            RegexOptions.IgnoreCase);
        trimmed = Regex.Replace(trimmed, @"\s+", " ").Trim();

        return string.IsNullOrWhiteSpace(trimmed)
            ? normalizedTitle
            : NormalizeEpisodeTitle(trimmed);
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

    private static MediaTrackMetadata ApplySourceLanguageHints(
        MediaTrackMetadata metadata,
        string filePath,
        CompanionTextMetadata textMetadata)
    {
        var sourceLanguageHint = MediaLanguageHelper.TryInferMuxLanguageCodeFromText(
            Path.GetFileNameWithoutExtension(filePath),
            textMetadata.Title,
            textMetadata.Topic);
        var videoLanguage = MediaLanguageHelper.ResolveMuxVideoLanguageCode(
            metadata.VideoLanguage,
            metadata.AudioLanguage,
            sourceLanguageHint);
        var audioLanguage = string.IsNullOrWhiteSpace(sourceLanguageHint)
            ? metadata.AudioLanguage
            : sourceLanguageHint;

        // Mediathek-Quellen mit "op Platt" tragen ihre Sprache häufig nur in Datei-/TXT-Texten;
        // sonst ist bei einsprachigen MP4s die Audiosprache belastbarer als das Video-Flag.
        return metadata with
        {
            VideoLanguage = videoLanguage,
            AudioLanguage = audioLanguage
        };
    }

    private sealed record EpisodeDetectionContext(
        string Directory,
        EpisodeIdentity EpisodeIdentity,
        IReadOnlyList<NormalVideoCandidate> NormalCandidates,
        NormalVideoCandidate? PrimaryVideoCandidate,
        IReadOnlyList<NormalVideoCandidate> SelectedVideoCandidates,
        IReadOnlyList<string> SubtitlePaths,
        IReadOnlyList<string> RelatedFilePaths,
        IReadOnlyList<AudioDescriptionCandidate> AudioDescriptionCandidates,
        IReadOnlyList<string> SourceHealthNotes);

    internal sealed record CandidateSeed(
        string FilePath,
        string? AttachmentPath,
        CompanionTextMetadata TextMetadata,
        EpisodeIdentity Identity);

    internal sealed record EpisodeSeedCollection(
        IReadOnlyList<CandidateSeed> AllEpisodeVideoSeeds,
        IReadOnlyList<CandidateSeed> NormalVideoSeeds,
        IReadOnlyList<CandidateSeed> AudioDescriptionSeeds,
        IReadOnlyList<CandidateSeed> SubtitleOnlySeeds);

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

