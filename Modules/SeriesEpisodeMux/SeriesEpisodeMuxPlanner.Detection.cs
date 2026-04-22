using System.Collections.Concurrent;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

// Dieser Partial enthält die eigentliche Quellenauswahl: Kandidaten gruppieren, priorisieren und zu einer Episode zusammenführen.
public sealed partial class SeriesEpisodeMuxPlanner
{
    private AutoDetectedEpisodeFiles DetectFromNormalVideo(
        string mainVideoPath,
        DirectoryDetectionContext? directoryContext,
        Action<DetectionProgressUpdate>? onProgress,
        ISet<string>? excludedSourcePaths,
        CancellationToken cancellationToken)
    {
        var context = BuildEpisodeDetectionContext(
            mainVideoPath,
            directoryContext,
            onProgress,
            excludedSourcePaths,
            "Es konnten keine passenden Videoquellen fuer diese Episode gefunden werden.",
            cancellationToken: cancellationToken);
        var primaryVideoCandidate = context.PrimaryVideoCandidate
            ?? throw new InvalidOperationException("Es konnte keine primäre Videoquelle für diese Episode ermittelt werden.");
        var selectedAudioDescription = SelectAudioDescriptionCandidate(context.AudioDescriptionCandidates, primaryVideoCandidate);
        var notes = BuildDetectionNotes(mainVideoPath, context.NormalCandidates, context.SelectedVideoCandidates, context.PrimaryVideoCandidate, selectedAudioDescription, context.SourceHealthNotes);
        var manualCheckFilePaths = BuildManualCheckFilePaths(context.SelectedVideoCandidates, selectedAudioDescription);

        ReportProgress(onProgress, "Erstelle Vorschlag...", 94);
        var detectedFiles = BuildDetectedFiles(
            context.Directory,
            context.EpisodeIdentity,
            primaryVideoCandidate,
            context.SelectedVideoCandidates,
            context.SubtitlePaths,
            context.RelatedFilePaths,
            selectedAudioDescription,
            manualCheckFilePaths,
            notes);
        ReportProgress(onProgress, "Erkennung abgeschlossen", 100);
        return detectedFiles;
    }
    private AutoDetectedEpisodeFiles DetectFromAudioDescription(
        string audioDescriptionPath,
        DirectoryDetectionContext? directoryContext,
        Action<DetectionProgressUpdate>? onProgress,
        ISet<string>? excludedSourcePaths,
        CancellationToken cancellationToken)
    {
        var context = BuildEpisodeDetectionContext(
            audioDescriptionPath,
            directoryContext,
            onProgress,
            excludedSourcePaths,
            "Zu der ausgewählten AD-Datei konnte keine passende Hauptdatei gefunden werden.",
            allowMissingPrimaryVideo: true,
            cancellationToken: cancellationToken);
        var selectedAudioDescription = SelectAudioDescriptionCandidate(
            context.AudioDescriptionCandidates,
            context.PrimaryVideoCandidate,
            preferredFilePath: audioDescriptionPath);
        var notes = BuildDetectionNotes(audioDescriptionPath, context.NormalCandidates, context.SelectedVideoCandidates, context.PrimaryVideoCandidate, selectedAudioDescription, context.SourceHealthNotes);
        var manualCheckFilePaths = BuildManualCheckFilePaths(context.SelectedVideoCandidates, selectedAudioDescription);

        if (context.PrimaryVideoCandidate is null)
        {
            notes.Insert(
                0,
                "Zur ausgewählten AD-Datei wurde keine passende frische Hauptquelle gefunden. Falls am Ziel bereits eine Archiv-MKV liegt, kann sie später als Hauptquelle weiterverwendet und um die AD ergänzt werden.");

            ReportProgress(onProgress, "Erstelle Vorschlag...", 94);
            var detectedAudioOnly = BuildDetectedFilesWithoutPrimaryVideo(
                context.Directory,
                context.EpisodeIdentity,
                audioDescriptionPath,
                context.RelatedFilePaths,
                selectedAudioDescription,
                context.SubtitlePaths,
                manualCheckFilePaths,
                notes);
            ReportProgress(onProgress, "Erkennung abgeschlossen", 100);
            return detectedAudioOnly;
        }

        notes.Insert(0, $"Zur ausgewählten AD-Datei wurde automatisch {Path.GetFileName(context.PrimaryVideoCandidate.FilePath)} als Hauptquelle gefunden.");

        ReportProgress(onProgress, "Erstelle Vorschlag...", 94);
        var detectedFiles = BuildDetectedFiles(
            context.Directory,
            context.EpisodeIdentity,
            context.PrimaryVideoCandidate,
            context.SelectedVideoCandidates,
            context.SubtitlePaths,
            context.RelatedFilePaths,
            selectedAudioDescription,
            manualCheckFilePaths,
            notes);
        ReportProgress(onProgress, "Erkennung abgeschlossen", 100);
        return detectedFiles;
    }

    private AutoDetectedEpisodeFiles DetectFromSubtitleOnly(
        string subtitlePath,
        DirectoryDetectionContext? directoryContext,
        Action<DetectionProgressUpdate>? onProgress,
        ISet<string>? excludedSourcePaths,
        CancellationToken cancellationToken)
    {
        var context = BuildEpisodeDetectionContext(
            subtitlePath,
            directoryContext,
            onProgress,
            excludedSourcePaths,
            "Zu der ausgewählten Untertiteldatei konnte keine passende Hauptdatei gefunden werden.",
            allowMissingPrimaryVideo: true,
            cancellationToken: cancellationToken);
        var selectedAudioDescription = SelectAudioDescriptionCandidate(
            context.AudioDescriptionCandidates,
            context.PrimaryVideoCandidate);
        var notes = BuildDetectionNotes(subtitlePath, context.NormalCandidates, context.SelectedVideoCandidates, context.PrimaryVideoCandidate, selectedAudioDescription, context.SourceHealthNotes);
        var manualCheckFilePaths = BuildManualCheckFilePaths(context.SelectedVideoCandidates, selectedAudioDescription);

        if (context.PrimaryVideoCandidate is null)
        {
            notes.Insert(
                0,
                "Zur ausgewählten Untertiteldatei wurde keine passende frische Hauptquelle gefunden. Falls am Ziel bereits eine Archiv-MKV liegt, kann sie später als Hauptquelle weiterverwendet und um die Untertitel ergänzt werden.");

            ReportProgress(onProgress, "Erstelle Vorschlag...", 94);
            var detectedSubtitleOnly = BuildDetectedFilesWithoutPrimaryVideo(
                context.Directory,
                context.EpisodeIdentity,
                subtitlePath,
                context.RelatedFilePaths,
                selectedAudioDescription,
                context.SubtitlePaths,
                manualCheckFilePaths,
                notes);
            ReportProgress(onProgress, "Erkennung abgeschlossen", 100);
            return detectedSubtitleOnly;
        }

        ReportProgress(onProgress, "Erstelle Vorschlag...", 94);
        var detectedFiles = BuildDetectedFiles(
            context.Directory,
            context.EpisodeIdentity,
            context.PrimaryVideoCandidate,
            context.SelectedVideoCandidates,
            context.SubtitlePaths,
            context.RelatedFilePaths,
            selectedAudioDescription,
            manualCheckFilePaths,
            notes);
        ReportProgress(onProgress, "Erkennung abgeschlossen", 100);
        return detectedFiles;
    }

    private EpisodeDetectionContext BuildEpisodeDetectionContext(
        string selectedPath,
        DirectoryDetectionContext? directoryContext,
        Action<DetectionProgressUpdate>? onProgress,
        ISet<string>? excludedSourcePaths,
        string noVideoCandidatesMessage,
        bool allowMissingPrimaryVideo = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(selectedPath)
            ?? throw new InvalidOperationException("Der Ordner der ausgewählten Datei konnte nicht bestimmt werden.");

        ReportProgress(onProgress, "Suche mkvmerge...", 3);
        var mkvMergePath = _locator.FindMkvMergePath();
        cancellationToken.ThrowIfCancellationRequested();

        DirectoryDetectionContext preparedContext;
        if (directoryContext is null)
        {
            ReportProgress(onProgress, "Lese Dateien im Ordner...", 8);
            preparedContext = CreateDirectoryDetectionContext(directory, cancellationToken);
        }
        else
        {
            if (!PathComparisonHelper.AreSamePath(directoryContext.SourceDirectory, directory))
            {
                throw new InvalidOperationException("Der vorbereitete Ordnerkontext passt nicht zur gewählten Datei.");
            }

            ReportProgress(onProgress, "Nutze vorbereiteten Ordnerkontext...", 8);
            preparedContext = directoryContext;
        }

        var companionFilesByBaseName = preparedContext.CompanionFilesByBaseName;
        var selectedSeed = preparedContext.GetSelectedSeed(selectedPath);
        var selectedIdentity = selectedSeed.Identity;
        var episodeSeeds = preparedContext.GetEpisodeSeeds(selectedSeed);
        var defectiveSeedHealth = DetectDefectiveVideoSeeds(episodeSeeds.AllEpisodeVideoSeeds);
        var defectiveSeedPaths = defectiveSeedHealth
            .Select(entry => entry.Seed.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usableEpisodeSeeds = episodeSeeds.AllEpisodeVideoSeeds
            .Where(seed => !defectiveSeedPaths.Contains(seed.FilePath))
            .ToList();
        var usableNormalSeeds = episodeSeeds.NormalVideoSeeds
            .Where(seed => !defectiveSeedPaths.Contains(seed.FilePath))
            .ToList();
        var usableAudioDescriptionSeeds = episodeSeeds.AudioDescriptionSeeds
            .Where(seed => !defectiveSeedPaths.Contains(seed.FilePath))
            .ToList();
        var sourceHealthNotes = BuildSourceHealthNotes(defectiveSeedHealth);
        cancellationToken.ThrowIfCancellationRequested();

        ReportProgress(onProgress, $"Prüfe {usableNormalSeeds.Count} passende Videoquelle(n)...", 12);
        var allNormalCandidates = BuildNormalVideoCandidates(
            preparedContext,
            usableNormalSeeds,
            mkvMergePath,
            companionFilesByBaseName,
            onProgress,
            12,
            72,
            cancellationToken);
        var normalCandidates = allNormalCandidates
            .Where(candidate => excludedSourcePaths is null || !excludedSourcePaths.Contains(candidate.FilePath))
            .ToList();

        var audioDescriptionSeeds = usableAudioDescriptionSeeds
            .Where(seed => excludedSourcePaths is null || !excludedSourcePaths.Contains(seed.FilePath))
            .ToList();

        ReportProgress(onProgress, $"Prüfe {audioDescriptionSeeds.Count} passende AD-Quelle(n)...", 76);
        var audioDescriptionCandidates = BuildAudioDescriptionCandidates(
            preparedContext,
            audioDescriptionSeeds,
            onProgress,
            76,
            88,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (normalCandidates.Count == 0)
        {
            if (!allowMissingPrimaryVideo)
            {
                var details = sourceHealthNotes.Count == 0
                    ? string.Empty
                    : " " + string.Join(" ", sourceHealthNotes);
                throw new InvalidOperationException(noVideoCandidatesMessage + details);
            }

            // Zusatzmaterial-only-Fälle dürfen Untertitel begleitender defekter MP4-Varianten
            // nicht verlieren. Auch wenn die MP4 selbst fachlich ausgesiebt wird, bleiben ihre
            // Untertitel und TXT-Begleiter für spätere Reuse-/Cleanup-Entscheidungen relevant.
            var defectiveSeedCompanionCleanupPaths = CollectCompanionCleanupPathsFromSeeds(
                defectiveSeedHealth.Select(entry => entry.Seed).ToList(),
                companionFilesByBaseName);
            return new EpisodeDetectionContext(
                directory,
                selectedIdentity,
                [],
                null,
                [],
                CollectSubtitlePathsFromSeeds(defectiveSeedHealth.Select(entry => entry.Seed).ToList(), companionFilesByBaseName)
                    .Concat(CollectSubtitlePathsFromSeeds(episodeSeeds.SubtitleOnlySeeds, companionFilesByBaseName))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                CollectRelatedEpisodeFilePaths([.. usableEpisodeSeeds, .. episodeSeeds.SubtitleOnlySeeds, .. episodeSeeds.MetadataOnlySeeds], companionFilesByBaseName)
                    .Concat(defectiveSeedCompanionCleanupPaths)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                audioDescriptionCandidates,
                sourceHealthNotes);
        }

        var episodeIdentity = SelectPreferredEpisodeIdentity(normalCandidates, selectedIdentity);
        var durationReferenceCandidate = SelectBestNormalVideoCandidate(normalCandidates);
        var selectedVideoCandidates = SelectVideoCandidates(normalCandidates, durationReferenceCandidate);
        var primaryVideoCandidate = selectedVideoCandidates[0];
        var subtitlePaths = CollectSubtitlePaths(normalCandidates, selectedVideoCandidates, primaryVideoCandidate)
            .Concat(CollectSubtitlePathsFromSeeds(defectiveSeedHealth.Select(entry => entry.Seed).ToList(), companionFilesByBaseName))
            .Concat(CollectSubtitlePathsFromSeeds(episodeSeeds.SubtitleOnlySeeds, companionFilesByBaseName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var relatedFilePaths = CollectRelatedEpisodeFilePaths([.. usableEpisodeSeeds, .. episodeSeeds.SubtitleOnlySeeds, .. episodeSeeds.MetadataOnlySeeds], companionFilesByBaseName);

        return new EpisodeDetectionContext(
            directory,
            episodeIdentity,
            normalCandidates,
            primaryVideoCandidate,
            selectedVideoCandidates,
            subtitlePaths,
            relatedFilePaths,
            audioDescriptionCandidates,
            sourceHealthNotes);
    }

    private AutoDetectedEpisodeFiles BuildDetectedFiles(
        string directory,
        EpisodeIdentity episodeIdentity,
        NormalVideoCandidate primaryVideoCandidate,
        IReadOnlyList<NormalVideoCandidate> selectedVideoCandidates,
        IReadOnlyList<string> subtitlePaths,
        IReadOnlyList<string> relatedFilePaths,
        AudioDescriptionCandidate? selectedAudioDescription,
        IReadOnlyList<string> manualCheckFilePaths,
        IReadOnlyList<string> notes)
    {
        return new AutoDetectedEpisodeFiles(
            MainVideoPath: primaryVideoCandidate.FilePath,
            AdditionalVideoPaths: selectedVideoCandidates.Skip(1).Select(candidate => candidate.FilePath).ToList(),
            AudioDescriptionPath: selectedAudioDescription?.FilePath,
            SubtitlePaths: subtitlePaths,
            AttachmentPaths: selectedVideoCandidates
                .Select(candidate => candidate.AttachmentPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            RelatedFilePaths: relatedFilePaths,
            SuggestedOutputFilePath: _archiveService.BuildSuggestedOutputPath(
                directory,
                episodeIdentity.SeriesName,
                episodeIdentity.SeasonNumber,
                episodeIdentity.EpisodeNumber,
                episodeIdentity.Title),
            SuggestedTitle: episodeIdentity.Title,
            SeriesName: episodeIdentity.SeriesName,
            SeasonNumber: episodeIdentity.SeasonNumber,
            EpisodeNumber: episodeIdentity.EpisodeNumber,
            RequiresManualCheck: manualCheckFilePaths.Count > 0,
            ManualCheckFilePaths: manualCheckFilePaths,
            Notes: notes,
            HasPrimaryVideoSource: true);
    }

    private AutoDetectedEpisodeFiles BuildDetectedFilesWithoutPrimaryVideo(
        string directory,
        EpisodeIdentity episodeIdentity,
        string detectionSeedPath,
        IReadOnlyList<string> relatedFilePaths,
        AudioDescriptionCandidate? selectedAudioDescription,
        IReadOnlyList<string> subtitlePaths,
        IReadOnlyList<string> manualCheckFilePaths,
        IReadOnlyList<string> notes)
    {
        // Ohne frische Hauptquelle kann die Erkennungsquelle AD oder Untertitel sein. Nur
        // echte AD-Pfade dürfen in den AD-Slot wandern; reine Untertitelquellen bleiben
        // ausschließlich in SubtitlePaths, damit der spätere Plan sie nicht als Audiospur
        // an mkvmerge weitergibt.
        return new AutoDetectedEpisodeFiles(
            MainVideoPath: detectionSeedPath,
            AdditionalVideoPaths: [],
            AudioDescriptionPath: selectedAudioDescription?.FilePath,
            SubtitlePaths: subtitlePaths,
            AttachmentPaths: [],
            RelatedFilePaths: relatedFilePaths,
            SuggestedOutputFilePath: _archiveService.BuildSuggestedOutputPath(
                directory,
                episodeIdentity.SeriesName,
                episodeIdentity.SeasonNumber,
                episodeIdentity.EpisodeNumber,
                episodeIdentity.Title),
            SuggestedTitle: episodeIdentity.Title,
            SeriesName: episodeIdentity.SeriesName,
            SeasonNumber: episodeIdentity.SeasonNumber,
            EpisodeNumber: episodeIdentity.EpisodeNumber,
            RequiresManualCheck: manualCheckFilePaths.Count > 0,
            ManualCheckFilePaths: manualCheckFilePaths,
            Notes: notes,
            HasPrimaryVideoSource: false);
    }

    /// <summary>
    /// Sammelt Begleitdateien defekter Seeds, ohne die defekte Mediendatei selbst als Cleanup-
    /// oder Reuse-Kandidat zurückzugeben.
    /// </summary>
    /// <param name="seeds">Defekte oder ausgesiebte Seeds derselben Episode.</param>
    /// <param name="companionFilesByBaseName">Ordnerweiter Lookup für exakte Begleitdateien.</param>
    /// <returns>Vorhandene Untertitel- und TXT-Begleiter der Seeds.</returns>
    private static IReadOnlyList<string> CollectCompanionCleanupPathsFromSeeds(
        IReadOnlyList<CandidateSeed> seeds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> companionFilesByBaseName)
    {
        return seeds
            .SelectMany(seed =>
            {
                var companionCleanupPaths = FindExactCompanionCleanupFiles(seed.FilePath, companionFilesByBaseName);
                return string.IsNullOrWhiteSpace(seed.AttachmentPath)
                    ? companionCleanupPaths
                    : companionCleanupPaths.Concat([seed.AttachmentPath!]);
            })
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> CollectRelatedEpisodeFilePaths(
        IReadOnlyList<CandidateSeed> seeds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> companionFilesByBaseName)
    {
        var relatedFilePaths = new List<string>();

        foreach (var seed in seeds)
        {
            relatedFilePaths.Add(seed.FilePath);

            if (!string.IsNullOrWhiteSpace(seed.AttachmentPath))
            {
                relatedFilePaths.Add(seed.AttachmentPath!);
            }

            relatedFilePaths.AddRange(FindExactCompanionCleanupFiles(seed.FilePath, companionFilesByBaseName));
        }

        return relatedFilePaths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> CollectSubtitlePathsFromSeeds(
        IReadOnlyList<CandidateSeed> seeds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> companionFilesByBaseName)
    {
        return seeds
            .SelectMany(seed => FindExactSubtitleFiles(seed.FilePath, companionFilesByBaseName))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildVideoTrackName(MediaTrackMetadata metadata)
    {
        return BuildVideoTrackName(metadata.VideoLanguage, metadata.VideoWidth, metadata.VideoCodecLabel);
    }

    private static string BuildVideoTrackName(string? languageCode, int videoWidth, string codecLabel)
    {
        return $"{MediaLanguageHelper.GetLanguageDisplayName(languageCode)} - {ResolutionLabel.FromWidth(videoWidth).Value} - {codecLabel}";
    }

    private static string BuildVideoSlotKey(string? languageCode, string codecLabel)
    {
        return $"{MediaLanguageHelper.NormalizeMuxLanguageCode(languageCode)}|{codecLabel.Trim().ToUpperInvariant()}";
    }

    private static string BuildNormalAudioTrackName(string? languageCode, string codecLabel)
    {
        return $"{MediaLanguageHelper.GetLanguageDisplayName(languageCode)} - {codecLabel}";
    }

    private static (string? TrackName, string? LanguageCode) BuildAudioDescriptionTrackMetadata(
        string primaryAudioLanguage,
        string primaryAudioCodecLabel,
        AudioTrackMetadata? audioDescriptionMetadata)
    {
        if (audioDescriptionMetadata is null)
        {
            return (null, null);
        }

        var normalizedAudioDescriptionLanguage = MediaLanguageHelper.NormalizeMuxLanguageCode(audioDescriptionMetadata.Language ?? primaryAudioLanguage);
        var audioDescriptionDisplayName = MediaLanguageHelper.GetLanguageDisplayName(audioDescriptionMetadata.Language ?? primaryAudioLanguage);
        return ($"{audioDescriptionDisplayName} (sehbehinderte) - {audioDescriptionMetadata.CodecLabel ?? primaryAudioCodecLabel}", normalizedAudioDescriptionLanguage);
    }

    private static string? TryReadCodecLabelFromTrackName(string? trackName)
    {
        if (string.IsNullOrWhiteSpace(trackName))
        {
            return null;
        }

        var separatorIndex = trackName.LastIndexOf(" - ", StringComparison.Ordinal);
        return separatorIndex >= 0 && separatorIndex < trackName.Length - 3
            ? trackName[(separatorIndex + 3)..]
            : null;
    }

    private async Task<AudioTrackMetadata> ReadEmbeddedAudioTrackMetadataAsync(
        string mkvMergePath,
        string inputFilePath,
        int trackId,
        string fallbackLanguage,
        CancellationToken cancellationToken)
    {
        var container = await _probeService.ReadContainerMetadataAsync(mkvMergePath, inputFilePath, cancellationToken);
        var matchingTrack = container.Tracks.FirstOrDefault(track =>
            string.Equals(track.Type, "audio", StringComparison.OrdinalIgnoreCase)
            && track.TrackId == trackId);

        if (matchingTrack is null)
        {
            return new AudioTrackMetadata(
                trackId,
                "Audio",
                MediaLanguageHelper.NormalizeMuxLanguageCode(fallbackLanguage),
                string.Empty,
                IsVisualImpaired: false);
        }

        return new AudioTrackMetadata(
            matchingTrack.TrackId,
            matchingTrack.CodecLabel,
            MediaLanguageHelper.NormalizeMuxLanguageCode(matchingTrack.Language),
            matchingTrack.TrackName,
            matchingTrack.IsVisualImpaired);
    }

}
