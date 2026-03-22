using System.Text;
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
    private readonly SeriesArchiveService _archiveService;
    private readonly WindowsMediaDurationProbe _durationProbe = new();

    public SeriesEpisodeMuxPlanner(MkvToolNixLocator locator, MkvMergeProbeService probeService, SeriesArchiveService archiveService)
    {
        _locator = locator;
        _probeService = probeService;
        _archiveService = archiveService;
    }

    public AutoDetectedEpisodeFiles DetectFromMainVideo(
        string mainVideoPath,
        Action<DetectionProgressUpdate>? onProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null)
    {
        if (!File.Exists(mainVideoPath))
        {
            throw new FileNotFoundException($"Videodatei nicht gefunden: {mainVideoPath}");
        }

        ReportProgress(onProgress, "Bereite Erkennung vor...", 0);

        var excludedPathSet = excludedSourcePaths is null || excludedSourcePaths.Count == 0
            ? null
            : new HashSet<string>(excludedSourcePaths, StringComparer.OrdinalIgnoreCase);

        return LooksLikeAudioDescription(mainVideoPath)
            ? DetectFromAudioDescription(mainVideoPath, onProgress, excludedPathSet)
            : DetectFromNormalVideo(mainVideoPath, onProgress, excludedPathSet);
    }

    private AutoDetectedEpisodeFiles DetectFromNormalVideo(
        string mainVideoPath,
        Action<DetectionProgressUpdate>? onProgress,
        ISet<string>? excludedSourcePaths)
    {
        var directory = Path.GetDirectoryName(mainVideoPath)
            ?? throw new InvalidOperationException("Der Ordner der Hauptdatei konnte nicht bestimmt werden.");

        ReportProgress(onProgress, "Suche mkvmerge...", 3);
        var mkvMergePath = _locator.FindMkvMergePath();

        ReportProgress(onProgress, "Lese Dateien im Ordner...", 8);
        var allFiles = Directory.GetFiles(directory);
        var selectedSeed = BuildCandidateSeed(mainVideoPath);
        var selectedIdentity = selectedSeed.Identity;
        var allEpisodeVideoSeeds = allFiles
            .Where(path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            .Select(BuildCandidateSeed)
            .Where(seed => seed.Identity.Key == selectedIdentity.Key)
            .ToList();

        var allNormalSeeds = allFiles
            .Where(path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            .Where(path => !LooksLikeAudioDescription(path))
            .Select(BuildCandidateSeed)
            .Where(seed => seed.Identity.Key == selectedIdentity.Key)
            .ToList();

        var normalSeeds = allNormalSeeds
            .Where(seed => excludedSourcePaths is null || !excludedSourcePaths.Contains(seed.FilePath))
            .ToList();

        ReportProgress(onProgress, $"Pruefe {allNormalSeeds.Count} passende Videoquelle(n)...", 12);
        var allNormalCandidates = BuildNormalVideoCandidates(allNormalSeeds, mkvMergePath, onProgress, 12, 72);
        var normalCandidates = allNormalCandidates
            .Where(candidate => excludedSourcePaths is null || !excludedSourcePaths.Contains(candidate.FilePath))
            .ToList();

        if (normalCandidates.Count == 0)
        {
            throw new InvalidOperationException("Es konnten keine passenden Videoquellen fuer diese Episode gefunden werden.");
        }

        var episodeIdentity = SelectPreferredEpisodeIdentity(normalCandidates, selectedIdentity);
        var primaryVideoCandidate = SelectBestNormalVideoCandidate(normalCandidates);
        var selectedVideoCandidates = SelectVideoCandidates(normalCandidates, primaryVideoCandidate);
        var subtitlePaths = CollectSubtitlePaths(allNormalCandidates, primaryVideoCandidate);
        var relatedFilePaths = CollectRelatedEpisodeFilePaths(allEpisodeVideoSeeds);

        var audioDescriptionSeeds = allFiles
            .Where(path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            .Where(LooksLikeAudioDescription)
            .Select(BuildCandidateSeed)
            .Where(seed => seed.Identity.Key == selectedIdentity.Key)
            .Where(seed => excludedSourcePaths is null || !excludedSourcePaths.Contains(seed.FilePath))
            .ToList();

        ReportProgress(onProgress, $"Pruefe {audioDescriptionSeeds.Count} passende AD-Quelle(n)...", 76);
        var audioDescriptionCandidates = BuildAudioDescriptionCandidates(audioDescriptionSeeds, onProgress, 76, 88);

        var selectedAudioDescription = SelectAudioDescriptionCandidate(audioDescriptionCandidates, primaryVideoCandidate);
        var notes = BuildDetectionNotes(mainVideoPath, normalCandidates, selectedVideoCandidates, primaryVideoCandidate, selectedAudioDescription);
        var manualCheckFilePaths = BuildManualCheckFilePaths(selectedVideoCandidates, selectedAudioDescription);

        ReportProgress(onProgress, "Erstelle Vorschlag...", 94);
        var detectedFiles = BuildDetectedFiles(
            directory,
            episodeIdentity,
            primaryVideoCandidate,
            selectedVideoCandidates,
            subtitlePaths,
            relatedFilePaths,
            selectedAudioDescription,
            manualCheckFilePaths,
            notes);
        ReportProgress(onProgress, "Erkennung abgeschlossen", 100);
        return detectedFiles;
    }

    private AutoDetectedEpisodeFiles DetectFromAudioDescription(
        string audioDescriptionPath,
        Action<DetectionProgressUpdate>? onProgress,
        ISet<string>? excludedSourcePaths)
    {
        var directory = Path.GetDirectoryName(audioDescriptionPath)
            ?? throw new InvalidOperationException("Der Ordner der AD-Datei konnte nicht bestimmt werden.");

        ReportProgress(onProgress, "Suche mkvmerge...", 3);
        var mkvMergePath = _locator.FindMkvMergePath();

        ReportProgress(onProgress, "Lese Dateien im Ordner...", 8);
        var allFiles = Directory.GetFiles(directory);
        var selectedSeed = BuildCandidateSeed(audioDescriptionPath);
        var selectedIdentity = selectedSeed.Identity;
        var allEpisodeVideoSeeds = allFiles
            .Where(path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            .Select(BuildCandidateSeed)
            .Where(seed => seed.Identity.Key == selectedIdentity.Key)
            .ToList();

        var allNormalSeeds = allFiles
            .Where(path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            .Where(path => !LooksLikeAudioDescription(path))
            .Select(BuildCandidateSeed)
            .Where(seed => seed.Identity.Key == selectedIdentity.Key)
            .ToList();

        var normalSeeds = allNormalSeeds
            .Where(seed => excludedSourcePaths is null || !excludedSourcePaths.Contains(seed.FilePath))
            .ToList();

        ReportProgress(onProgress, $"Pruefe {allNormalSeeds.Count} passende Videoquelle(n)...", 12);
        var allNormalCandidates = BuildNormalVideoCandidates(allNormalSeeds, mkvMergePath, onProgress, 12, 72);
        var normalCandidates = allNormalCandidates
            .Where(candidate => excludedSourcePaths is null || !excludedSourcePaths.Contains(candidate.FilePath))
            .ToList();

        if (normalCandidates.Count == 0)
        {
            throw new InvalidOperationException("Zu der ausgewaehlten AD-Datei konnte keine passende Hauptdatei gefunden werden.");
        }

        var episodeIdentity = SelectPreferredEpisodeIdentity(normalCandidates, selectedIdentity);
        var primaryVideoCandidate = SelectBestNormalVideoCandidate(normalCandidates);
        var selectedVideoCandidates = SelectVideoCandidates(normalCandidates, primaryVideoCandidate);
        var subtitlePaths = CollectSubtitlePaths(allNormalCandidates, primaryVideoCandidate);
        var relatedFilePaths = CollectRelatedEpisodeFilePaths(allEpisodeVideoSeeds);

        var audioDescriptionSeeds = allFiles
            .Where(path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            .Where(LooksLikeAudioDescription)
            .Select(BuildCandidateSeed)
            .Where(seed => seed.Identity.Key == selectedIdentity.Key)
            .Where(seed => excludedSourcePaths is null || !excludedSourcePaths.Contains(seed.FilePath))
            .ToList();

        ReportProgress(onProgress, $"Pruefe {audioDescriptionSeeds.Count} passende AD-Quelle(n)...", 76);
        var audioDescriptionCandidates = BuildAudioDescriptionCandidates(audioDescriptionSeeds, onProgress, 76, 88);

        var selectedAudioDescription = SelectAudioDescriptionCandidate(
            audioDescriptionCandidates,
            primaryVideoCandidate,
            preferredFilePath: audioDescriptionPath);
        var notes = BuildDetectionNotes(audioDescriptionPath, normalCandidates, selectedVideoCandidates, primaryVideoCandidate, selectedAudioDescription);
        notes.Insert(0, $"Zur ausgewaehlten AD-Datei wurde automatisch {Path.GetFileName(primaryVideoCandidate.FilePath)} als Hauptquelle gefunden.");
        var manualCheckFilePaths = BuildManualCheckFilePaths(selectedVideoCandidates, selectedAudioDescription);

        ReportProgress(onProgress, "Erstelle Vorschlag...", 94);
        var detectedFiles = BuildDetectedFiles(
            directory,
            episodeIdentity,
            primaryVideoCandidate,
            selectedVideoCandidates,
            subtitlePaths,
            relatedFilePaths,
            selectedAudioDescription,
            manualCheckFilePaths,
            notes);
        ReportProgress(onProgress, "Erkennung abgeschlossen", 100);
        return detectedFiles;
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

    private static IReadOnlyList<string> CollectRelatedEpisodeFilePaths(IReadOnlyList<CandidateSeed> seeds)
    {
        var relatedFilePaths = new List<string>();

        foreach (var seed in seeds)
        {
            relatedFilePaths.Add(seed.FilePath);

            if (!string.IsNullOrWhiteSpace(seed.AttachmentPath))
            {
                relatedFilePaths.Add(seed.AttachmentPath!);
            }

            relatedFilePaths.AddRange(FindExactSubtitleFiles(seed.FilePath));
        }

        return relatedFilePaths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private CandidateSeed BuildCandidateSeed(string filePath)
    {
        var attachmentPath = File.Exists(Path.ChangeExtension(filePath, ".txt"))
            ? Path.ChangeExtension(filePath, ".txt")
            : null;

        var textMetadata = ReadTextMetadata(attachmentPath);
        var identity = ParseEpisodeIdentity(filePath, textMetadata);
        return new CandidateSeed(filePath, attachmentPath, textMetadata, identity);
    }

    private List<NormalVideoCandidate> BuildNormalVideoCandidates(
        IReadOnlyList<CandidateSeed> seeds,
        string mkvMergePath,
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
            candidates.Add(BuildNormalVideoCandidate(seed, mkvMergePath));
        }

        return candidates;
    }

    private List<AudioDescriptionCandidate> BuildAudioDescriptionCandidates(
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
            candidates.Add(BuildAudioDescriptionCandidate(seed));
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

    public async Task<SeriesEpisodeMuxPlan> CreatePlanAsync(SeriesEpisodeMuxRequest request)
    {
        ValidateRequest(request);

        var detected = DetectFromMainVideo(request.MainVideoPath);
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
            detected,
            plannedVideoPaths);

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
            var videoPath = effectiveVideoPaths[index];
            var metadata = await _probeService.ReadPrimaryVideoMetadataAsync(mkvMergePath, videoPath);
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
            throw new InvalidOperationException("Es wurde keine primaere Videoquelle fuer die Tonspur gefunden.");
        }

        var audioDescriptionPath = !string.IsNullOrWhiteSpace(archiveDecision.AudioDescriptionFilePath)
            ? archiveDecision.AudioDescriptionFilePath
            : string.IsNullOrWhiteSpace(request.AudioDescriptionPath) ? null : request.AudioDescriptionPath;
        AudioTrackMetadata? audioDescriptionMetadata = audioDescriptionPath is null
            ? null
            : archiveDecision.AudioDescriptionTrackId is int trackId
                ? new AudioTrackMetadata(trackId, "Audio", "de", string.Empty, IsVisualImpaired: true)
                : await _probeService.ReadFirstAudioTrackMetadataAsync(mkvMergePath, audioDescriptionPath);

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
        if (audioDescriptionPath is not null && IsSrfSender(ReadTextMetadata(Path.ChangeExtension(audioDescriptionPath, ".txt")).Sender))
        {
            notes.Add("Die ausgewaehlte AD-Quelle stammt von SRF. Bitte die Datei vor dem Muxen pruefen.");
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
            throw new DirectoryNotFoundException($"Ausgabeordner nicht gefunden: {outputDirectory}");
        }

        Directory.CreateDirectory(outputDirectory);
    }

    private NormalVideoCandidate BuildNormalVideoCandidate(CandidateSeed seed, string mkvMergePath)
    {
        var mediaMetadata = _probeService.ReadPrimaryVideoMetadataAsync(mkvMergePath, seed.FilePath).GetAwaiter().GetResult();
        var durationSeconds = ReadDurationSeconds(seed.FilePath, seed.TextMetadata.Duration);
        var subtitlePaths = FindExactSubtitleFiles(seed.FilePath);

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
            .ThenBy(candidate => GetCodecPreferenceRank(candidate.VideoCodecLabel))
            .ThenByDescending(candidate => candidate.FileSizeBytes)
            .ThenBy(candidate => GetSenderPriority(candidate.Sender))
            .FirstOrDefault();

        return bestCandidate?.Identity ?? fallbackIdentity;
    }

    private static NormalVideoCandidate SelectBestNormalVideoCandidate(IEnumerable<NormalVideoCandidate> candidates)
    {
        return candidates
            .OrderByDescending(candidate => candidate.VideoWidth)
            .ThenBy(candidate => GetCodecPreferenceRank(candidate.VideoCodecLabel))
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
            .ThenBy(candidate => GetCodecPreferenceRank(candidate.VideoCodecLabel))
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
            notes.Add($"Es werden {selectedVideoCandidates.Count} Videospuren mit unterschiedlichen Codecs uebernommen.");
        }

        if (selectedVideoCandidates.Any(candidate => IsSrfSender(candidate.Sender)))
        {
            notes.Add("Mindestens eine ausgewaehlte Videoquelle stammt von SRF. Bitte die Datei vor dem Muxen pruefen.");
        }

        if (selectedAudioDescription is not null && IsSrfSender(selectedAudioDescription.Sender))
        {
            notes.Add("Die ausgewaehlte AD-Quelle stammt von SRF. Bitte die Datei vor dem Muxen pruefen.");
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

    private static List<string> FindExactSubtitleFiles(string videoFilePath)
    {
        var directory = Path.GetDirectoryName(videoFilePath)
            ?? throw new InvalidOperationException("Der Ordner der Videodatei konnte nicht bestimmt werden.");
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(videoFilePath);

        return Directory.GetFiles(directory, fileNameWithoutExtension + ".*")
            .Where(path => SupportedSubtitleExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

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
        normalized = Regex.Replace(normalized, @"\s*-\s*Neue Folgen?\b.*$", string.Empty, RegexOptions.IgnoreCase);
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
            @"\(S(?<season>\d{1,2})\s*_\s*E(?<episode>\d{1,2})\)",
            @"\(S(?<season>\d{1,2})\s*/\s*E(?<episode>\d{1,2})\)",
            @"\(Staffel\s*(?<season>\d{1,2})\s*,\s*Folge\s*(?<episode>\d{1,2})\)"
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
        normalized = Regex.Replace(normalized, @"\s*-\s*Neue Folgen?\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(S\d{1,2}\s*[_/]\s*E\d{1,2}\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(Staffel\s*\d{1,2}\s*,\s*Folge\s*\d{1,2}\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(\s*Audiodeskrip[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bAudiodeskription\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"\s*[-:]\s*$", string.Empty);
        return normalized;
    }

    private static string NormalizeSeparators(string value)
    {
        var normalized = value
            .Replace("\u2013", "-")
            .Replace("\u2014", "-")
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¾Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â¦ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¦ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã¢â‚¬Å“", "-")
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¾Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â¦ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â", "-");

        normalized = Regex.Replace(normalized, @"\s*-\s*", " - ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"\s*[-:]\s*$", string.Empty);
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

    private static int GetCodecPreferenceRank(string codecLabel)
    {
        return codecLabel.ToUpperInvariant() switch
        {
            "H.264" => 0,
            "H.265" => 1,
            _ => 2
        };
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

        try
        {
            var utf8 = Encoding.UTF8.GetString(bytes);
            if (!utf8.Contains("ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¾Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¾ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢", StringComparison.Ordinal))
            {
                return utf8;
            }
        }
        catch
        {
        }

        return Encoding.Latin1.GetString(bytes);
    }

    private static string? ReadLabeledValue(string content, string label)
    {
        var match = Regex.Match(content, $@"^{Regex.Escape(label)}\s*:\s*(.+)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private sealed record TextMetadata(string? Sender, string? Topic, string? Title, TimeSpan? Duration)
    {
        public static TextMetadata Empty { get; } = new(null, null, null, null);
    }

    private sealed record CandidateSeed(
        string FilePath,
        string? AttachmentPath,
        TextMetadata TextMetadata,
        EpisodeIdentity Identity);

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
