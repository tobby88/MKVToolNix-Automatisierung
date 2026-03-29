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
        var selectedAudioDescription = SelectAudioDescriptionCandidate(context.AudioDescriptionCandidates, context.PrimaryVideoCandidate);
        var notes = BuildDetectionNotes(mainVideoPath, context.NormalCandidates, context.SelectedVideoCandidates, context.PrimaryVideoCandidate, selectedAudioDescription);
        var manualCheckFilePaths = BuildManualCheckFilePaths(context.SelectedVideoCandidates, selectedAudioDescription);

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
            "Zu der ausgewählten AD-Datei konnte keine passende Hauptdatei gefunden werden.");
        var selectedAudioDescription = SelectAudioDescriptionCandidate(
            context.AudioDescriptionCandidates,
            context.PrimaryVideoCandidate,
            preferredFilePath: audioDescriptionPath);
        var notes = BuildDetectionNotes(audioDescriptionPath, context.NormalCandidates, context.SelectedVideoCandidates, context.PrimaryVideoCandidate, selectedAudioDescription);
        notes.Insert(0, $"Zur ausgewählten AD-Datei wurde automatisch {Path.GetFileName(context.PrimaryVideoCandidate.FilePath)} als Hauptquelle gefunden.");
        var manualCheckFilePaths = BuildManualCheckFilePaths(context.SelectedVideoCandidates, selectedAudioDescription);

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
        string noVideoCandidatesMessage)
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

        if (normalCandidates.Count == 0)
        {
            throw new InvalidOperationException(noVideoCandidatesMessage);
        }

        var episodeIdentity = SelectPreferredEpisodeIdentity(normalCandidates, selectedIdentity);
        var primaryVideoCandidate = SelectBestNormalVideoCandidate(normalCandidates);
        var selectedVideoCandidates = SelectVideoCandidates(normalCandidates, primaryVideoCandidate);
        var subtitlePaths = CollectSubtitlePaths(allNormalCandidates, primaryVideoCandidate);
        var relatedFilePaths = CollectRelatedEpisodeFilePaths(episodeSeeds.AllEpisodeVideoSeeds, companionFilesByBaseName);

        var audioDescriptionSeeds = episodeSeeds.AudioDescriptionSeeds
            .Where(seed => excludedSourcePaths is null || !excludedSourcePaths.Contains(seed.FilePath))
            .ToList();

        ReportProgress(onProgress, $"Prüfe {audioDescriptionSeeds.Count} passende AD-Quelle(n)...", 76);
        var audioDescriptionCandidates = BuildAudioDescriptionCandidates(preparedContext, audioDescriptionSeeds, onProgress, 76, 88);

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
            Notes: notes);
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

    public async Task<SeriesEpisodeMuxPlan> CreatePlanAsync(
        SeriesEpisodeMuxRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        var detected = DetectFromMainVideo(request.MainVideoPath, directoryContext: null, onProgress: null, excludedSourcePaths: null, allowCachedResult: false);
        cancellationToken.ThrowIfCancellationRequested();
        var subtitleFiles = request.SubtitlePaths
            .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new SubtitleFile(path, SubtitleKind.FromExtension(Path.GetExtension(path))))
            .ToList();

        var mkvMergePath = _locator.FindMkvMergePath();
        var plannedVideoPaths = new[] { detected.MainVideoPath }
            .Concat(detected.AdditionalVideoPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var archiveDecision = await _archiveService.PrepareAsync(
            mkvMergePath,
            request,
            plannedVideoPaths,
            cancellationToken);

        if (archiveDecision.SkipMux)
        {
            return SeriesEpisodeMuxPlan.CreateSkip(
                mkvMergePath,
                archiveDecision.OutputFilePath,
                request.Title,
                archiveDecision.SkipReason ?? "Archiv bereits aktuell.",
                archiveDecision.Notes);
        }

        var effectiveOutputPath = archiveDecision.OutputFilePath;
        var effectivePrimarySourcePath = string.IsNullOrWhiteSpace(archiveDecision.PrimarySourcePath)
            ? request.MainVideoPath
            : archiveDecision.PrimarySourcePath;
        var additionalVideoPaths = archiveDecision.AdditionalVideoPaths.Count > 0
            ? archiveDecision.AdditionalVideoPaths
            : plannedVideoPaths
                .Where(path => !string.Equals(path, request.MainVideoPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var videoSources = new List<VideoSourcePlan>();
        string? primaryAudioFilePath = null;
        var primaryAudioTrackId = 0;
        var primaryAudioCodecLabel = "Audio";

        var effectiveVideoPaths = new[] { effectivePrimarySourcePath }
            .Concat(additionalVideoPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var index = 0; index < effectiveVideoPaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var videoPath = effectiveVideoPaths[index];
            var metadata = await _probeService.ReadPrimaryVideoMetadataAsync(mkvMergePath, videoPath, cancellationToken);
            var trackName = BuildVideoTrackName(metadata);
            videoSources.Add(new VideoSourcePlan(videoPath, metadata.VideoTrackId, trackName, IsDefaultTrack: index == 0));

            if (index == 0)
            {
                primaryAudioFilePath = videoPath;
                primaryAudioTrackId = metadata.AudioTrackId;
                primaryAudioCodecLabel = metadata.AudioCodecLabel;
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
                ? new AudioTrackMetadata(trackId, "Audio", "de", string.Empty, IsVisualImpaired: true)
                : await _probeService.ReadFirstAudioTrackMetadataAsync(mkvMergePath, audioDescriptionPath, cancellationToken);

        var subtitleFilesForPlan = archiveDecision.SubtitleFiles.Count > 0
            ? archiveDecision.SubtitleFiles
            : subtitleFiles;

        var attachmentFilePaths = archiveDecision.AttachmentFilePaths.Count > 0
            ? archiveDecision.AttachmentFilePaths
            : archiveDecision.FallbackToRequestAttachments
                ? request.AttachmentPaths
                : [];

        var notes = detected.Notes
            .Concat(archiveDecision.Notes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
            archiveDecision.PrimaryAudioTrackIds,
            archiveDecision.PrimarySubtitleTrackIds,
            archiveDecision.IncludePrimaryAttachments,
            audioDescriptionPath,
            audioDescriptionMetadata?.TrackId,
            subtitleFilesForPlan,
            attachmentFilePaths,
            archiveDecision.PreservedAttachmentNames,
            archiveDecision.WorkingCopy,
            BuildTrackMetadata(primaryAudioCodecLabel, audioDescriptionMetadata),
            notes.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
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

        var outputDirectory = Path.GetDirectoryName(request.OutputFilePath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new DirectoryNotFoundException($"Ausgabeziel nicht gefunden: {outputDirectory}");
        }

        Directory.CreateDirectory(outputDirectory);
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

    private static List<NormalVideoCandidate> SelectVideoCandidates(IReadOnlyList<NormalVideoCandidate> candidates, NormalVideoCandidate primaryCandidate)
    {
        var sameDurationCandidates = FilterByDuration(candidates, primaryCandidate.DurationSeconds);
        var bestByCodec = sameDurationCandidates
            .GroupBy(candidate => candidate.VideoCodecLabel, StringComparer.OrdinalIgnoreCase)
            .Select(group => SelectBestNormalVideoCandidate(group))
            .ToList();

        var additionalCandidates = bestByCodec
            .Where(candidate => !string.Equals(candidate.FilePath, primaryCandidate.FilePath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.VideoWidth)
            .ThenBy(candidate => MediaCodecPreferenceHelper.GetVideoCodecPreferenceRank(candidate.VideoCodecLabel))
            .ThenByDescending(candidate => candidate.FileSizeBytes)
            .ThenBy(candidate => GetSenderPriority(candidate.Sender))
            .ToList();

        return [primaryCandidate, .. additionalCandidates];
    }

    private static AudioDescriptionCandidate? SelectAudioDescriptionCandidate(
        IReadOnlyList<AudioDescriptionCandidate> candidates,
        NormalVideoCandidate primaryVideoCandidate,
        string? preferredFilePath = null)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var sameDurationCandidates = FilterByDuration(candidates, primaryVideoCandidate.DurationSeconds);
        var pool = sameDurationCandidates.Count == 0 ? candidates : sameDurationCandidates;

        return pool
            .OrderBy(candidate => string.Equals(candidate.FilePath, preferredFilePath, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(candidate => string.Equals(candidate.Sender, primaryVideoCandidate.Sender, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
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
        NormalVideoCandidate primaryVideoCandidate,
        AudioDescriptionCandidate? selectedAudioDescription)
    {
        var notes = new List<string>();

        if (!string.Equals(selectedMainVideoPath, primaryVideoCandidate.FilePath, StringComparison.OrdinalIgnoreCase))
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
            notes.Add($"Es werden {selectedVideoCandidates.Count} Videospuren mit unterschiedlichen Codecs übernommen.");
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
        IReadOnlyList<NormalVideoCandidate> allCandidates,
        NormalVideoCandidate primaryVideoCandidate)
    {
        return FilterByDuration(allCandidates, primaryVideoCandidate.DurationSeconds)
            .SelectMany(candidate => candidate.SubtitlePaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildVideoTrackName(MediaTrackMetadata metadata)
    {
        return $"Deutsch - {metadata.ResolutionLabel.Value} - {metadata.VideoCodecLabel}";
    }

    private static EpisodeTrackMetadata BuildTrackMetadata(string primaryAudioCodecLabel, AudioTrackMetadata? audioDescriptionMetadata)
    {
        return new EpisodeTrackMetadata(
            AudioTrackName: $"Deutsch - {primaryAudioCodecLabel}",
            AudioDescriptionTrackName: $"Deutsch (sehbehinderte) - {audioDescriptionMetadata?.CodecLabel ?? primaryAudioCodecLabel}");
    }

}
