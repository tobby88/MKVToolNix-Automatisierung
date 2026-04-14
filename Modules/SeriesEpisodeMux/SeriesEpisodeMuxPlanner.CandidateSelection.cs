using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

// Dieser Partial bündelt Kandidatenanalyse, Priorisierung und Notizbildung der Dateierkennung.
public sealed partial class SeriesEpisodeMuxPlanner
{
    private List<NormalVideoCandidate> BuildNormalVideoCandidates(
        DirectoryDetectionContext? directoryContext,
        IReadOnlyList<CandidateSeed> seeds,
        string mkvMergePath,
        IReadOnlyDictionary<string, IReadOnlyList<string>> companionFilesByBaseName,
        Action<DetectionProgressUpdate>? onProgress,
        int startPercent,
        int endPercent,
        CancellationToken cancellationToken)
    {
        var candidates = new List<NormalVideoCandidate>(seeds.Count);

        for (var index = 0; index < seeds.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seed = seeds[index];
            var percent = InterpolateProgress(startPercent, endPercent, index, Math.Max(1, seeds.Count));
            ReportProgress(onProgress, $"Analysiere Videoquelle {index + 1}/{seeds.Count}: {Path.GetFileName(seed.FilePath)}", percent);
            cancellationToken.ThrowIfCancellationRequested();
            candidates.Add(
                directoryContext?.GetOrCreateNormalVideoCandidate(seed, mkvMergePath, cancellationToken)
                ?? BuildNormalVideoCandidate(seed, mkvMergePath, companionFilesByBaseName, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
        }

        return candidates;
    }

    private List<AudioDescriptionCandidate> BuildAudioDescriptionCandidates(
        DirectoryDetectionContext? directoryContext,
        IReadOnlyList<CandidateSeed> seeds,
        Action<DetectionProgressUpdate>? onProgress,
        int startPercent,
        int endPercent,
        CancellationToken cancellationToken)
    {
        var candidates = new List<AudioDescriptionCandidate>(seeds.Count);

        for (var index = 0; index < seeds.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seed = seeds[index];
            var percent = InterpolateProgress(startPercent, endPercent, index, Math.Max(1, seeds.Count));
            ReportProgress(onProgress, $"Analysiere AD-Quelle {index + 1}/{seeds.Count}: {Path.GetFileName(seed.FilePath)}", percent);
            cancellationToken.ThrowIfCancellationRequested();
            candidates.Add(
                directoryContext?.GetOrCreateAudioDescriptionCandidate(seed, cancellationToken)
                ?? BuildAudioDescriptionCandidate(seed, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
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

    private NormalVideoCandidate BuildNormalVideoCandidate(
        CandidateSeed seed,
        string mkvMergePath,
        IReadOnlyDictionary<string, IReadOnlyList<string>> companionFilesByBaseName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mediaMetadata = _probeService.ReadPrimaryVideoMetadata(mkvMergePath, seed.FilePath);
        cancellationToken.ThrowIfCancellationRequested();
        var durationSeconds = ReadDurationSeconds(seed.FilePath, seed.TextMetadata.Duration);
        cancellationToken.ThrowIfCancellationRequested();
        var subtitlePaths = FindExactSubtitleFiles(seed.FilePath, companionFilesByBaseName);

        var mediaMetadataWithLanguageHints = ApplySourceLanguageHints(mediaMetadata, seed.FilePath, seed.TextMetadata);

        return new NormalVideoCandidate(
            FilePath: seed.FilePath,
            Identity: seed.Identity,
            Sender: NormalizeSender(seed.TextMetadata.Sender),
            DurationSeconds: durationSeconds,
            VideoWidth: mediaMetadataWithLanguageHints.VideoWidth,
            VideoCodecLabel: mediaMetadataWithLanguageHints.VideoCodecLabel,
            VideoLanguage: MediaLanguageHelper.NormalizeMuxLanguageCode(mediaMetadataWithLanguageHints.VideoLanguage),
            AudioCodecLabel: mediaMetadataWithLanguageHints.AudioCodecLabel,
            FileSizeBytes: new FileInfo(seed.FilePath).Length,
            SubtitlePaths: subtitlePaths,
            AttachmentPath: seed.AttachmentPath);
    }

    private AudioDescriptionCandidate BuildAudioDescriptionCandidate(CandidateSeed seed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var durationSeconds = ReadDurationSeconds(seed.FilePath, seed.TextMetadata.Duration);
        cancellationToken.ThrowIfCancellationRequested();

        return new AudioDescriptionCandidate(
            FilePath: seed.FilePath,
            Identity: seed.Identity,
            Sender: NormalizeSender(seed.TextMetadata.Sender),
            DurationSeconds: durationSeconds,
            FileSizeBytes: new FileInfo(seed.FilePath).Length,
            AttachmentPath: seed.AttachmentPath);
    }

    private static List<(CandidateSeed Seed, MediaFileHealthResult Health)> DetectDefectiveVideoSeeds(IReadOnlyList<CandidateSeed> seeds)
    {
        var defectiveSeeds = new List<(CandidateSeed Seed, MediaFileHealthResult Health)>();
        foreach (var seed in seeds)
        {
            var health = MediaFileHealth.CheckMp4FileAgainstDeclaredSize(seed.FilePath, seed.TextMetadata);
            if (!health.IsUsable)
            {
                defectiveSeeds.Add((seed, health));
            }
        }

        return defectiveSeeds;
    }

    private static List<string> BuildSourceHealthNotes(IReadOnlyList<(CandidateSeed Seed, MediaFileHealthResult Health)> defectiveSeedHealth)
    {
        return defectiveSeedHealth
            .Select(entry =>
            {
                var reason = string.IsNullOrWhiteSpace(entry.Health.Reason)
                    ? "MP4 wirkt defekt oder unvollständig."
                    : entry.Health.Reason;
                return $"Defekte/unvollständige Quelle wird nicht verwendet: {Path.GetFileName(entry.Seed.FilePath)} ({reason})";
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        return OrderCandidatesByPreference(candidates)
            .First();
    }

    private static IOrderedEnumerable<NormalVideoCandidate> OrderCandidatesByPreference(IEnumerable<NormalVideoCandidate> candidates)
    {
        return candidates
            .OrderByDescending(candidate => candidate.VideoWidth)
            .ThenBy(candidate => MediaCodecPreferenceHelper.GetVideoCodecPreferenceRank(candidate.VideoCodecLabel))
            .ThenByDescending(candidate => candidate.FileSizeBytes)
            .ThenBy(candidate => GetSenderPriority(candidate.Sender))
            .ThenBy(candidate => candidate.FilePath, StringComparer.OrdinalIgnoreCase);
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
        AudioDescriptionCandidate? selectedAudioDescription,
        IReadOnlyList<string> sourceHealthNotes)
    {
        var notes = sourceHealthNotes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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

    /// <summary>
    /// Sammelt externe Untertitel pro Untertiteltyp zunächst aus den tatsächlich ausgewählten
    /// Videospuren und ergänzt fehlende Formate anschließend aus anderen laufzeitpassenden
    /// Quellen derselben Episode. So bleibt eine bessere Hauptquelle bevorzugt, ohne dass
    /// zusätzliche Untertitelarten wie etwa ein vorhandenes SRT einer verdrängten Quelle
    /// verloren gehen.
    /// </summary>
    private static List<string> CollectSubtitlePaths(
        IReadOnlyList<NormalVideoCandidate> normalCandidates,
        IReadOnlyList<NormalVideoCandidate> selectedVideoCandidates,
        NormalVideoCandidate primaryVideoCandidate)
    {
        var durationMatchedCandidates = FilterByDuration(normalCandidates, primaryVideoCandidate.DurationSeconds);
        var selectedSourcePaths = new HashSet<string>(
            selectedVideoCandidates.Select(candidate => candidate.FilePath),
            StringComparer.OrdinalIgnoreCase);
        var orderedCandidates = FilterByDuration(selectedVideoCandidates, primaryVideoCandidate.DurationSeconds)
            .Concat(OrderCandidatesByPreference(durationMatchedCandidates.Where(candidate => !selectedSourcePaths.Contains(candidate.FilePath))))
            .ToList();
        var subtitlePathsByKind = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in orderedCandidates)
        {
            foreach (var subtitlePath in candidate.SubtitlePaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var subtitleKind = SubtitleKind.FromExtension(Path.GetExtension(subtitlePath));
                subtitlePathsByKind.TryAdd(subtitleKind.DisplayName, subtitlePath);
            }
        }

        return subtitlePathsByKind
            .Values
            .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
