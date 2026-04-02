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
        ISet<string>? excludedSourcePaths)
    {
        var context = BuildEpisodeDetectionContext(
            mainVideoPath,
            directoryContext,
            onProgress,
            excludedSourcePaths,
            "Es konnten keine passenden Videoquellen fuer diese Episode gefunden werden.");
        var primaryVideoCandidate = context.PrimaryVideoCandidate
            ?? throw new InvalidOperationException("Es konnte keine primäre Videoquelle für diese Episode ermittelt werden.");
        var selectedAudioDescription = SelectAudioDescriptionCandidate(context.AudioDescriptionCandidates, primaryVideoCandidate);
        var notes = BuildDetectionNotes(mainVideoPath, context.NormalCandidates, context.SelectedVideoCandidates, context.PrimaryVideoCandidate, selectedAudioDescription);
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
        ISet<string>? excludedSourcePaths)
    {
        var context = BuildEpisodeDetectionContext(
            audioDescriptionPath,
            directoryContext,
            onProgress,
            excludedSourcePaths,
            "Zu der ausgewählten AD-Datei konnte keine passende Hauptdatei gefunden werden.",
            allowMissingPrimaryVideo: true);
        var selectedAudioDescription = SelectAudioDescriptionCandidate(
            context.AudioDescriptionCandidates,
            context.PrimaryVideoCandidate,
            preferredFilePath: audioDescriptionPath);
        var notes = BuildDetectionNotes(audioDescriptionPath, context.NormalCandidates, context.SelectedVideoCandidates, context.PrimaryVideoCandidate, selectedAudioDescription);
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
    private EpisodeDetectionContext BuildEpisodeDetectionContext(
        string selectedPath,
        DirectoryDetectionContext? directoryContext,
        Action<DetectionProgressUpdate>? onProgress,
        ISet<string>? excludedSourcePaths,
        string noVideoCandidatesMessage,
        bool allowMissingPrimaryVideo = false)
    {
        var directory = Path.GetDirectoryName(selectedPath)
            ?? throw new InvalidOperationException("Der Ordner der ausgewählten Datei konnte nicht bestimmt werden.");

        ReportProgress(onProgress, "Suche mkvmerge...", 3);
        var mkvMergePath = _locator.FindMkvMergePath();

        DirectoryDetectionContext preparedContext;
        if (directoryContext is null)
        {
            ReportProgress(onProgress, "Lese Dateien im Ordner...", 8);
            preparedContext = CreateDirectoryDetectionContext(directory);
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

        ReportProgress(onProgress, $"Prüfe {episodeSeeds.NormalVideoSeeds.Count} passende Videoquelle(n)...", 12);
        var allNormalCandidates = BuildNormalVideoCandidates(
            preparedContext,
            episodeSeeds.NormalVideoSeeds,
            mkvMergePath,
            companionFilesByBaseName,
            onProgress,
            12,
            72);
        var normalCandidates = allNormalCandidates
            .Where(candidate => excludedSourcePaths is null || !excludedSourcePaths.Contains(candidate.FilePath))
            .ToList();

        var audioDescriptionSeeds = episodeSeeds.AudioDescriptionSeeds
            .Where(seed => excludedSourcePaths is null || !excludedSourcePaths.Contains(seed.FilePath))
            .ToList();

        ReportProgress(onProgress, $"Prüfe {audioDescriptionSeeds.Count} passende AD-Quelle(n)...", 76);
        var audioDescriptionCandidates = BuildAudioDescriptionCandidates(preparedContext, audioDescriptionSeeds, onProgress, 76, 88);

        if (normalCandidates.Count == 0)
        {
            if (!allowMissingPrimaryVideo)
            {
                throw new InvalidOperationException(noVideoCandidatesMessage);
            }

            return new EpisodeDetectionContext(
                directory,
                selectedIdentity,
                [],
                null,
                [],
                [],
                CollectRelatedEpisodeFilePaths(episodeSeeds.AllEpisodeVideoSeeds, companionFilesByBaseName),
                audioDescriptionCandidates);
        }

        var episodeIdentity = SelectPreferredEpisodeIdentity(normalCandidates, selectedIdentity);
        var durationReferenceCandidate = SelectBestNormalVideoCandidate(normalCandidates);
        var selectedVideoCandidates = SelectVideoCandidates(normalCandidates, durationReferenceCandidate);
        var primaryVideoCandidate = selectedVideoCandidates[0];
        var subtitlePaths = CollectSubtitlePaths(selectedVideoCandidates, primaryVideoCandidate);
        var relatedFilePaths = CollectRelatedEpisodeFilePaths(episodeSeeds.AllEpisodeVideoSeeds, companionFilesByBaseName);

        return new EpisodeDetectionContext(
            directory,
            episodeIdentity,
            normalCandidates,
            primaryVideoCandidate,
            selectedVideoCandidates,
            subtitlePaths,
            relatedFilePaths,
            audioDescriptionCandidates);
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
        IReadOnlyList<string> manualCheckFilePaths,
        IReadOnlyList<string> notes)
    {
        var effectiveAudioDescriptionPath = selectedAudioDescription?.FilePath ?? detectionSeedPath;

        return new AutoDetectedEpisodeFiles(
            MainVideoPath: detectionSeedPath,
            AdditionalVideoPaths: [],
            AudioDescriptionPath: effectiveAudioDescriptionPath,
            SubtitlePaths: [],
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

    private List<NormalVideoCandidate> BuildNormalVideoCandidates(
        DirectoryDetectionContext? directoryContext,
        IReadOnlyList<CandidateSeed> seeds,
        string mkvMergePath,
        IReadOnlyDictionary<string, IReadOnlyList<string>> companionFilesByBaseName,
        Action<DetectionProgressUpdate>? onProgress,
        int startPercent,
        int endPercent)
    {
        var candidates = new List<NormalVideoCandidate>(seeds.Count);

        for (var index = 0; index < seeds.Count; index++)
        {
            var seed = seeds[index];
            var percent = InterpolateProgress(startPercent, endPercent, index, Math.Max(1, seeds.Count));
            ReportProgress(onProgress, $"Analysiere Videoquelle {index + 1}/{seeds.Count}: {Path.GetFileName(seed.FilePath)}", percent);
            candidates.Add(directoryContext?.GetOrCreateNormalVideoCandidate(seed, mkvMergePath) ?? BuildNormalVideoCandidate(seed, mkvMergePath, companionFilesByBaseName));
        }

        return candidates;
    }

    private List<AudioDescriptionCandidate> BuildAudioDescriptionCandidates(
        DirectoryDetectionContext? directoryContext,
        IReadOnlyList<CandidateSeed> seeds,
        Action<DetectionProgressUpdate>? onProgress,
        int startPercent,
        int endPercent)
    {
        var candidates = new List<AudioDescriptionCandidate>(seeds.Count);

        for (var index = 0; index < seeds.Count; index++)
        {
            var seed = seeds[index];
            var percent = InterpolateProgress(startPercent, endPercent, index, Math.Max(1, seeds.Count));
            ReportProgress(onProgress, $"Analysiere AD-Quelle {index + 1}/{seeds.Count}: {Path.GetFileName(seed.FilePath)}", percent);
            candidates.Add(directoryContext?.GetOrCreateAudioDescriptionCandidate(seed) ?? BuildAudioDescriptionCandidate(seed));
        }

        return candidates;
    }

    private static int InterpolateProgress(int startPercent, int endPercent, int index, int totalCount)
    {
        if (totalCount <= 0)
        {
            return startPercent;
        }

        var ratio = index / (double)totalCount;
        return (int)Math.Round(startPercent + ((endPercent - startPercent) * ratio));
    }

    private static void ReportProgress(
        Action<DetectionProgressUpdate>? onProgress,
        string statusText,
        int progressPercent)
    {
        onProgress?.Invoke(new DetectionProgressUpdate(statusText, Math.Clamp(progressPercent, 0, 100)));
    }

    /// <summary>
    /// Erzeugt aus einer konkreten Episodeingabe einen vollständig aufgelösten Mux-Plan inklusive Archivintegration.
    /// </summary>
    /// <param name="request">Aktuelle Benutzereingabe mit Quellen, Zielpfad und optionalen Ausschlüssen.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal für Probe-, Archiv- und Planungsarbeit.</param>
    /// <returns>Fertiger Mux-Plan oder ein fachlicher Skip-Plan, wenn kein Lauf mehr nötig ist.</returns>
    public async Task<SeriesEpisodeMuxPlan> CreatePlanAsync(
        SeriesEpisodeMuxRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<string> detectedNotes = [];
        List<string> plannedVideoPaths;
        if (request.HasPrimaryVideoSource)
        {
            var detected = DetectFromMainVideo(
                request.MainVideoPath,
                excludedSourcePaths: request.ExcludedSourcePaths);
            detectedNotes = detected.Notes;
            plannedVideoPaths = new[] { detected.MainVideoPath }
                .Concat(detected.AdditionalVideoPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            plannedVideoPaths = [];
        }

        cancellationToken.ThrowIfCancellationRequested();
        var subtitleFiles = request.SubtitlePaths
            .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => SubtitleFile.CreateDetectedExternal(path, SubtitleKind.FromExtension(Path.GetExtension(path))))
            .ToList();

        var mkvMergePath = _locator.FindMkvMergePath();

        var archiveDecision = await _archiveService.PrepareAsync(
            mkvMergePath,
            request,
            plannedVideoPaths,
            cancellationToken);

        if (!request.HasPrimaryVideoSource && string.IsNullOrWhiteSpace(archiveDecision.PrimarySourcePath))
        {
            throw new InvalidOperationException(
                "Zur ausgewählten AD-Datei wurde keine passende Hauptquelle gefunden, und am Ziel liegt noch keine wiederverwendbare Archiv-MKV. Die AD kann deshalb derzeit nicht verarbeitet werden.");
        }

        if (archiveDecision.SkipMux)
        {
            return SeriesEpisodeMuxPlan.CreateSkip(
                mkvMergePath,
                archiveDecision.OutputFilePath,
                request.Title,
                archiveDecision.SkipReason ?? "Archiv bereits aktuell.",
                archiveDecision.SkipUsageSummary,
                archiveDecision.Notes);
        }

        var effectiveOutputPath = archiveDecision.OutputFilePath;
        var videoSelections = archiveDecision.VideoSelections.Count > 0
            ? archiveDecision.VideoSelections
            : await BuildVideoSelectionsFromPlannedPathsAsync(mkvMergePath, plannedVideoPaths, cancellationToken);

        var videoSources = new List<VideoSourcePlan>();
        string? primaryAudioFilePath = null;
        var primaryAudioTrackId = 0;
        var primaryAudioCodecLabel = "Audio";
        var primaryAudioLanguage = "de";

        for (var index = 0; index < videoSelections.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var videoSelection = videoSelections[index];
            videoSources.Add(new VideoSourcePlan(
                videoSelection.FilePath,
                videoSelection.TrackId,
                BuildVideoTrackName(videoSelection.LanguageCode, videoSelection.VideoWidth, videoSelection.CodecLabel),
                IsDefaultTrack: index == 0,
                LanguageCode: videoSelection.LanguageCode));

            if (index == 0)
            {
                primaryAudioFilePath = videoSelection.FilePath;

                if (archiveDecision.PrimaryAudioTrackIds is { Count: > 0 }
                    && string.Equals(videoSelection.FilePath, effectiveOutputPath, StringComparison.OrdinalIgnoreCase))
                {
                    var embeddedPrimaryAudioMetadata = await ReadEmbeddedAudioTrackMetadataAsync(
                        mkvMergePath,
                        effectiveOutputPath,
                        archiveDecision.PrimaryAudioTrackIds[0],
                        videoSelection.LanguageCode,
                        cancellationToken);
                    primaryAudioTrackId = embeddedPrimaryAudioMetadata.TrackId;
                    primaryAudioCodecLabel = embeddedPrimaryAudioMetadata.CodecLabel;
                    primaryAudioLanguage = embeddedPrimaryAudioMetadata.Language;
                }
                else
                {
                    var metadata = await _probeService.ReadPrimaryVideoMetadataAsync(mkvMergePath, videoSelection.FilePath, cancellationToken);
                    primaryAudioTrackId = metadata.AudioTrackId;
                    primaryAudioCodecLabel = metadata.AudioCodecLabel;
                    primaryAudioLanguage = metadata.AudioLanguage;
                }
            }
        }

        if (primaryAudioFilePath is null)
        {
            throw new InvalidOperationException("Es wurde keine primäre Videoquelle für die Tonspur gefunden.");
        }

        var audioDescriptionPath = !string.IsNullOrWhiteSpace(archiveDecision.AudioDescriptionFilePath)
            ? archiveDecision.AudioDescriptionFilePath
            : string.IsNullOrWhiteSpace(request.AudioDescriptionPath) ? null : request.AudioDescriptionPath;
        AudioTrackMetadata? audioDescriptionMetadata = audioDescriptionPath is null
            ? null
            : archiveDecision.AudioDescriptionTrackId is int trackId
                ? await ReadEmbeddedAudioTrackMetadataAsync(
                    mkvMergePath,
                    audioDescriptionPath,
                    trackId,
                    primaryAudioLanguage,
                    cancellationToken)
                : await _probeService.ReadFirstAudioTrackMetadataAsync(mkvMergePath, audioDescriptionPath, cancellationToken);

        var subtitleFilesForPlan = archiveDecision.SubtitleFiles.Count > 0
            ? archiveDecision.SubtitleFiles
            : subtitleFiles;

        var attachmentFilePaths = archiveDecision.AttachmentFilePaths.Count > 0
            ? archiveDecision.AttachmentFilePaths
            : archiveDecision.FallbackToRequestAttachments
                ? request.AttachmentPaths
                : [];

        var notes = detectedNotes
            .Concat(archiveDecision.Notes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!request.HasPrimaryVideoSource && !string.IsNullOrWhiteSpace(archiveDecision.PrimarySourcePath))
        {
            notes.Insert(0, "Es liegt nur eine AD-Quelle vor. Die Hauptspuren werden aus der vorhandenen Archiv-MKV übernommen.");
        }
        if (audioDescriptionPath is not null && IsSrfSender(CompanionTextMetadataReader.ReadForMediaFile(audioDescriptionPath).Sender))
        {
            notes.Add("Die ausgewählte AD-Quelle stammt von SRF. Bitte die Datei vor dem Muxen prüfen.");
        }

        return new SeriesEpisodeMuxPlan(
            mkvMergePath,
            effectiveOutputPath,
            request.Title,
            videoSources,
            primaryAudioFilePath,
            primaryAudioTrackId,
            archiveDecision.PrimaryAudioTrackIds ?? [primaryAudioTrackId],
            archiveDecision.PrimarySubtitleTrackIds,
            archiveDecision.PrimarySourceAttachmentIds,
            archiveDecision.IncludePrimaryAttachments,
            archiveDecision.AttachmentSourcePath,
            archiveDecision.AttachmentSourceAttachmentIds,
            audioDescriptionPath,
            audioDescriptionMetadata?.TrackId,
            subtitleFilesForPlan,
            attachmentFilePaths,
            archiveDecision.PreservedAttachmentNames,
            archiveDecision.UsageComparison,
            archiveDecision.WorkingCopy,
            BuildTrackMetadata(primaryAudioCodecLabel, primaryAudioLanguage, audioDescriptionMetadata),
            notes.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private async Task<IReadOnlyList<VideoTrackSelection>> BuildVideoSelectionsFromPlannedPathsAsync(
        string mkvMergePath,
        IReadOnlyList<string> plannedVideoPaths,
        CancellationToken cancellationToken)
    {
        var selections = new List<VideoTrackSelection>(plannedVideoPaths.Count);

        foreach (var videoPath in plannedVideoPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = await _probeService.ReadPrimaryVideoMetadataAsync(mkvMergePath, videoPath, cancellationToken);
            selections.Add(new VideoTrackSelection(
                videoPath,
                metadata.VideoTrackId,
                metadata.VideoWidth,
                metadata.VideoCodecLabel,
                MediaLanguageHelper.NormalizeMuxLanguageCode(metadata.VideoLanguage)));
        }

        return selections;
    }

    private static void ValidateRequest(SeriesEpisodeMuxRequest request)
    {
        if (request.HasPrimaryVideoSource && !File.Exists(request.MainVideoPath))
        {
            throw new FileNotFoundException($"Hauptvideo nicht gefunden: {request.MainVideoPath}");
        }

        if (!string.IsNullOrWhiteSpace(request.AudioDescriptionPath) && !File.Exists(request.AudioDescriptionPath))
        {
            throw new FileNotFoundException($"AD-Datei nicht gefunden: {request.AudioDescriptionPath}");
        }

        foreach (var attachmentPath in request.AttachmentPaths)
        {
            if (!File.Exists(attachmentPath))
            {
                throw new FileNotFoundException($"Text-Anhang nicht gefunden: {attachmentPath}");
            }
        }

        foreach (var subtitlePath in request.SubtitlePaths)
        {
            if (!File.Exists(subtitlePath))
            {
                throw new FileNotFoundException($"Untertiteldatei nicht gefunden: {subtitlePath}");
            }
        }

        ValidateOutputPath(request.OutputFilePath);
    }

    private static void ValidateOutputPath(string outputFilePath)
    {
        var outputDirectory = Path.GetDirectoryName(outputFilePath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new DirectoryNotFoundException($"Ausgabeziel konnte nicht bestimmt werden: {outputFilePath}");
        }
    }

    private NormalVideoCandidate BuildNormalVideoCandidate(
        CandidateSeed seed,
        string mkvMergePath,
        IReadOnlyDictionary<string, IReadOnlyList<string>> companionFilesByBaseName)
    {
        var mediaMetadata = _probeService.ReadPrimaryVideoMetadata(mkvMergePath, seed.FilePath);
        var durationSeconds = ReadDurationSeconds(seed.FilePath, seed.TextMetadata.Duration);
        var subtitlePaths = FindExactSubtitleFiles(seed.FilePath, companionFilesByBaseName);

        return new NormalVideoCandidate(
            FilePath: seed.FilePath,
            Identity: seed.Identity,
            Sender: NormalizeSender(seed.TextMetadata.Sender),
            DurationSeconds: durationSeconds,
            VideoWidth: mediaMetadata.VideoWidth,
            VideoCodecLabel: mediaMetadata.VideoCodecLabel,
            VideoLanguage: MediaLanguageHelper.NormalizeMuxLanguageCode(mediaMetadata.VideoLanguage),
            AudioCodecLabel: mediaMetadata.AudioCodecLabel,
            FileSizeBytes: new FileInfo(seed.FilePath).Length,
            SubtitlePaths: subtitlePaths,
            AttachmentPath: seed.AttachmentPath);
    }

    private AudioDescriptionCandidate BuildAudioDescriptionCandidate(CandidateSeed seed)
    {
        var durationSeconds = ReadDurationSeconds(seed.FilePath, seed.TextMetadata.Duration);

        return new AudioDescriptionCandidate(
            FilePath: seed.FilePath,
            Identity: seed.Identity,
            Sender: NormalizeSender(seed.TextMetadata.Sender),
            DurationSeconds: durationSeconds,
            FileSizeBytes: new FileInfo(seed.FilePath).Length,
            AttachmentPath: seed.AttachmentPath);
    }

    private EpisodeIdentity SelectPreferredEpisodeIdentity(IReadOnlyList<NormalVideoCandidate> candidates, EpisodeIdentity fallbackIdentity)
    {
        var bestCandidate = candidates
            .OrderBy(candidate => candidate.Identity.SeasonNumber == "xx")
            .ThenByDescending(candidate => candidate.VideoWidth)
            .ThenBy(candidate => MediaCodecPreferenceHelper.GetVideoCodecPreferenceRank(candidate.VideoCodecLabel))
            .ThenByDescending(candidate => candidate.FileSizeBytes)
            .ThenBy(candidate => GetSenderPriority(candidate.Sender))
            .FirstOrDefault();

        return bestCandidate?.Identity ?? fallbackIdentity;
    }

    private static NormalVideoCandidate SelectBestNormalVideoCandidate(IEnumerable<NormalVideoCandidate> candidates)
    {
        return candidates
            .OrderByDescending(candidate => candidate.VideoWidth)
            .ThenBy(candidate => MediaCodecPreferenceHelper.GetVideoCodecPreferenceRank(candidate.VideoCodecLabel))
            .ThenByDescending(candidate => candidate.FileSizeBytes)
            .ThenBy(candidate => GetSenderPriority(candidate.Sender))
            .First();
    }

    private static List<NormalVideoCandidate> SelectVideoCandidates(
        IReadOnlyList<NormalVideoCandidate> candidates,
        NormalVideoCandidate durationReferenceCandidate)
    {
        var sameDurationCandidates = FilterByDuration(candidates, durationReferenceCandidate.DurationSeconds);

        // Fachlich wird pro Sprach-/Codec-Slot genau die beste Quelle übernommen.
        // So kann z. B. Deutsch H.264 vor Deutsch H.265 stehen, während Fremdsprachen
        // dahinter einsortiert werden, ohne dass gleichsprachige bessere Ersatzquellen
        // desselben Codecs doppelt im Plan landen.
        return sameDurationCandidates
            .GroupBy(candidate => BuildVideoSlotKey(candidate.VideoLanguage, candidate.VideoCodecLabel), StringComparer.OrdinalIgnoreCase)
            .Select(group => SelectBestNormalVideoCandidate(group))
            .OrderBy(candidate => MediaLanguageHelper.GetLanguageSortRank(candidate.VideoLanguage))
            .ThenBy(candidate => MediaCodecPreferenceHelper.GetVideoCodecPreferenceRank(candidate.VideoCodecLabel))
            .ThenByDescending(candidate => candidate.VideoWidth)
            .ThenByDescending(candidate => candidate.FileSizeBytes)
            .ThenBy(candidate => GetSenderPriority(candidate.Sender))
            .ToList();
    }

    private static AudioDescriptionCandidate? SelectAudioDescriptionCandidate(
        IReadOnlyList<AudioDescriptionCandidate> candidates,
        NormalVideoCandidate? primaryVideoCandidate,
        string? preferredFilePath = null)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var sameDurationCandidates = FilterByDuration(candidates, primaryVideoCandidate?.DurationSeconds);
        var pool = sameDurationCandidates.Count == 0 ? candidates : sameDurationCandidates;

        return pool
            .OrderBy(candidate => string.Equals(candidate.FilePath, preferredFilePath, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(candidate => primaryVideoCandidate is null
                ? 0
                : string.Equals(candidate.Sender, primaryVideoCandidate.Sender, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(candidate => candidate.FileSizeBytes)
            .ThenBy(candidate => GetSenderPriority(candidate.Sender))
            .FirstOrDefault();
    }

    private static List<TCandidate> FilterByDuration<TCandidate>(IEnumerable<TCandidate> candidates, int? preferredDurationSeconds)
        where TCandidate : EpisodeCandidateBase
    {
        if (preferredDurationSeconds is null)
        {
            return candidates.ToList();
        }

        var filtered = candidates
            .Where(candidate => candidate.DurationSeconds is null || Math.Abs(candidate.DurationSeconds.Value - preferredDurationSeconds.Value) <= 1)
            .ToList();

        return filtered.Count == 0 ? candidates.ToList() : filtered;
    }

    private static List<string> BuildDetectionNotes(
        string selectedMainVideoPath,
        IReadOnlyList<NormalVideoCandidate> normalCandidates,
        IReadOnlyList<NormalVideoCandidate> selectedVideoCandidates,
        NormalVideoCandidate? primaryVideoCandidate,
        AudioDescriptionCandidate? selectedAudioDescription)
    {
        var notes = new List<string>();

        if (primaryVideoCandidate is not null
            && !string.Equals(selectedMainVideoPath, primaryVideoCandidate.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            notes.Add($"Als Hauptquelle wurde automatisch {Path.GetFileName(primaryVideoCandidate.FilePath)} bevorzugt.");
        }

        var distinctDurations = normalCandidates
            .Where(candidate => candidate.DurationSeconds is not null)
            .Select(candidate => candidate.DurationSeconds!.Value)
            .Distinct()
            .Count();

        if (distinctDurations > 1)
        {
            notes.Add("Es wurden Quellen mit unterschiedlicher Laufzeit gefunden. Fuer Video, Audio und Untertitel wird nur die passende Laufzeitgruppe verwendet.");
        }

        if (selectedVideoCandidates.Count > 1)
        {
            notes.Add($"Es werden {selectedVideoCandidates.Count} Videospuren aus unterschiedlichen Sprach-/Codec-Slots übernommen.");
        }

        if (selectedVideoCandidates.Any(candidate => IsSrfSender(candidate.Sender)))
        {
            notes.Add("Mindestens eine ausgewählte Videoquelle stammt von SRF. Bitte die Datei vor dem Muxen prüfen.");
        }

        if (selectedAudioDescription is not null && IsSrfSender(selectedAudioDescription.Sender))
        {
            notes.Add("Die ausgewählte AD-Quelle stammt von SRF. Bitte die Datei vor dem Muxen prüfen.");
        }

        return notes;
    }

    private static List<string> BuildManualCheckFilePaths(
        IReadOnlyList<NormalVideoCandidate> selectedVideoCandidates,
        AudioDescriptionCandidate? selectedAudioDescription)
    {
        var files = selectedVideoCandidates
            .Where(candidate => IsSrfSender(candidate.Sender))
            .Select(candidate => candidate.FilePath)
            .ToList();

        if (selectedAudioDescription is not null && IsSrfSender(selectedAudioDescription.Sender))
        {
            files.Add(selectedAudioDescription.FilePath);
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> CollectSubtitlePaths(
        IReadOnlyList<NormalVideoCandidate> selectedVideoCandidates,
        NormalVideoCandidate primaryVideoCandidate)
    {
        var subtitlePaths = new List<string>();
        var coveredKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in FilterByDuration(selectedVideoCandidates, primaryVideoCandidate.DurationSeconds))
        {
            foreach (var subtitlePath in candidate.SubtitlePaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var subtitleKind = SubtitleKind.FromExtension(Path.GetExtension(subtitlePath));
                if (!coveredKinds.Add(subtitleKind.DisplayName))
                {
                    continue;
                }

                subtitlePaths.Add(subtitlePath);
            }
        }

        return subtitlePaths;
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

    private static EpisodeTrackMetadata BuildTrackMetadata(
        string primaryAudioCodecLabel,
        string primaryAudioLanguage,
        AudioTrackMetadata? audioDescriptionMetadata)
    {
        var normalizedAudioLanguage = MediaLanguageHelper.NormalizeMuxLanguageCode(primaryAudioLanguage);
        var audioDisplayName = MediaLanguageHelper.GetLanguageDisplayName(primaryAudioLanguage);
        var normalizedAudioDescriptionLanguage = MediaLanguageHelper.NormalizeMuxLanguageCode(audioDescriptionMetadata?.Language ?? primaryAudioLanguage);
        var audioDescriptionDisplayName = MediaLanguageHelper.GetLanguageDisplayName(audioDescriptionMetadata?.Language ?? primaryAudioLanguage);

        return new EpisodeTrackMetadata(
            AudioTrackName: $"{audioDisplayName} - {primaryAudioCodecLabel}",
            AudioDescriptionTrackName: $"{audioDescriptionDisplayName} (sehbehinderte) - {audioDescriptionMetadata?.CodecLabel ?? primaryAudioCodecLabel}",
            AudioLanguageCode: normalizedAudioLanguage,
            AudioDescriptionLanguageCode: normalizedAudioDescriptionLanguage);
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
                IsVisualImpaired: true);
        }

        return new AudioTrackMetadata(
            matchingTrack.TrackId,
            matchingTrack.CodecLabel,
            MediaLanguageHelper.NormalizeMuxLanguageCode(matchingTrack.Language),
            matchingTrack.TrackName,
            matchingTrack.IsVisualImpaired);
    }

}
