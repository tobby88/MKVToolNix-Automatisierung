using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

public sealed class SeriesArchiveService
{
    public const string ArchiveRootDirectory = @"Z:\Videos\Serien";

    private readonly MkvMergeProbeService _probeService;

    public SeriesArchiveService(MkvMergeProbeService probeService)
    {
        _probeService = probeService;
    }

    public string BuildSuggestedOutputPath(string fallbackDirectory, string seriesName, string seasonNumber, string episodeNumber, string title)
    {
        if (!Directory.Exists(ArchiveRootDirectory))
        {
            return EpisodeMetadataPath(fallbackDirectory, seriesName, seasonNumber, episodeNumber, title);
        }

        var seasonFolderName = int.TryParse(seasonNumber, out var parsedSeason) && parsedSeason > 0
            ? $"Season {parsedSeason}"
            : "Season xx";

        var targetDirectory = Path.Combine(
            ArchiveRootDirectory,
            SanitizePathPart(seriesName),
            seasonFolderName);

        var fileName = $"{seriesName} - S{NormalizeEpisodeNumber(seasonNumber)}E{NormalizeEpisodeNumber(episodeNumber)} - {title}.mkv";
        return Path.Combine(targetDirectory, SanitizeFileName(fileName));
    }

    public bool IsArchivePath(string outputFilePath)
    {
        return outputFilePath.StartsWith(ArchiveRootDirectory, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ArchiveIntegrationDecision> PrepareAsync(
        string mkvMergePath,
        SeriesEpisodeMuxRequest request,
        AutoDetectedEpisodeFiles detected,
        IReadOnlyList<string> plannedVideoPaths)
    {
        var outputPath = request.OutputFilePath;
        if (!File.Exists(outputPath))
        {
            return ArchiveIntegrationDecision.CreateForFreshTarget(outputPath);
        }

        var existingContainer = await _probeService.ReadContainerMetadataAsync(mkvMergePath, outputPath);
        var existingVideoTracks = existingContainer.Tracks
            .Where(track => string.Equals(track.Type, "video", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var existingAudioTracks = existingContainer.Tracks
            .Where(track => string.Equals(track.Type, "audio", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var existingSubtitleTracks = existingContainer.Tracks
            .Where(track => string.Equals(track.Type, "subtitles", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var newVideoMetadata = new List<(string FilePath, MediaTrackMetadata Metadata)>();
        foreach (var videoPath in plannedVideoPaths)
        {
            newVideoMetadata.Add((videoPath, await _probeService.ReadPrimaryVideoMetadataAsync(mkvMergePath, videoPath)));
        }

        var newPrimaryVideo = newVideoMetadata.FirstOrDefault();
        if (newPrimaryVideo == default)
        {
            return ArchiveIntegrationDecision.CreateForFreshTarget(outputPath);
        }

        var bestExistingVideo = existingVideoTracks
            .OrderByDescending(track => track.VideoWidth)
            .ThenBy(track => GetCodecPreferenceRank(track.CodecLabel))
            .ThenBy(track => track.TrackId)
            .FirstOrDefault();

        var keepExistingPrimary = bestExistingVideo is not null
            && !IsNewVideoClearlyBetter(newPrimaryVideo.Metadata, bestExistingVideo);

        var newAdditionalVideoPaths = keepExistingPrimary
            ? newVideoMetadata
                .Where(entry => !string.Equals(entry.FilePath, newPrimaryVideo.FilePath, StringComparison.OrdinalIgnoreCase))
                .Where(entry => existingVideoTracks.All(track => !string.Equals(track.CodecLabel, entry.Metadata.VideoCodecLabel, StringComparison.OrdinalIgnoreCase)))
                .Select(entry => entry.FilePath)
                .ToList()
            : plannedVideoPaths.Skip(1).ToList();

        var existingSubtitleKinds = existingSubtitleTracks
            .Select(track => SubtitleKind.FromExistingCodec(track.CodecLabel))
            .Where(kind => kind is not null)
            .Cast<SubtitleKind>()
            .ToList();

        var externalSubtitlePlans = request.SubtitlePaths
            .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new SubtitleFile(path, SubtitleKind.FromExtension(Path.GetExtension(path))))
            .ToList();

        var embeddedSubtitlePlans = existingSubtitleTracks
            .Select(track => new
            {
                Track = track,
                Kind = SubtitleKind.FromExistingCodec(track.CodecLabel)
            })
            .Where(entry => entry.Kind is not null)
            .Where(entry => externalSubtitlePlans.All(plan => !string.Equals(plan.Kind.DisplayName, entry.Kind!.DisplayName, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(entry => entry.Kind!.SortRank)
            .ThenBy(entry => entry.Track.TrackId)
            .Select(entry => new SubtitleFile(outputPath, entry.Kind!, entry.Track.TrackId))
            .ToList();

        var finalSubtitlePlans = externalSubtitlePlans
            .Concat(embeddedSubtitlePlans)
            .ToList();

        var existingAudioDescription = existingAudioTracks.FirstOrDefault(track =>
            track.IsVisualImpaired
            || track.TrackName.Contains("sehbehinder", StringComparison.OrdinalIgnoreCase)
            || track.TrackName.Contains("audiodeskrip", StringComparison.OrdinalIgnoreCase));

        var workingCopyPlan = BuildWorkingCopyPlan(outputPath, Path.GetDirectoryName(request.MainVideoPath)!);

        if (keepExistingPrimary)
        {
            var primaryAudioTrack = existingAudioTracks.FirstOrDefault(track => !track.IsVisualImpaired) ?? existingAudioTracks.FirstOrDefault();
            var needsAudioDescription = !string.IsNullOrWhiteSpace(request.AudioDescriptionPath) && existingAudioDescription is null;
            var needsSubtitleSupplement = finalSubtitlePlans.Count > 0;
            var needsAdditionalVideo = newAdditionalVideoPaths.Count > 0;

            if (!needsAudioDescription && !needsSubtitleSupplement && !needsAdditionalVideo)
            {
                return ArchiveIntegrationDecision.CreateSkip(
                    outputPath,
                    "Die Archiv-MKV enthaelt bereits die bevorzugte Videoquelle sowie alle benoetigten Zusatzspuren.",
                    ["Archiv bereits vollstaendig. Kein erneutes Muxen noetig."]);
            }

            return new ArchiveIntegrationDecision(
                OutputFilePath: outputPath,
                SkipMux: false,
                SkipReason: null,
                WorkingCopy: workingCopyPlan,
                PrimarySourcePath: outputPath,
                PrimaryAudioTrackIds: primaryAudioTrack is null ? null : [primaryAudioTrack.TrackId],
                PrimarySubtitleTrackIds: finalSubtitlePlans.Count > 0 ? [] : null,
                IncludePrimaryAttachments: true,
                AdditionalVideoPaths: newAdditionalVideoPaths,
                AudioDescriptionFilePath: existingAudioDescription is null ? request.AudioDescriptionPath : null,
                AudioDescriptionTrackId: null,
                SubtitleFiles: finalSubtitlePlans,
                AttachmentFilePaths: BuildAttachmentPathsForUsedVideos(newAdditionalVideoPaths),
                Notes:
                [
                    "Archiv-MKV bereits vorhanden. Vor dem Muxen wird eine lokale Arbeitskopie verwendet.",
                    bestExistingVideo is null
                        ? "Die vorhandene Archivdatei liefert die Hauptspuren."
                        : $"Vorhandene Videospur wird beibehalten: {bestExistingVideo.VideoWidth}px / {bestExistingVideo.CodecLabel}."
                ]);
        }

        var fallbackSubtitlePlans = externalSubtitlePlans
            .Concat(embeddedSubtitlePlans)
            .ToList();

        var needsExistingCopy = (existingAudioDescription is not null && string.IsNullOrWhiteSpace(request.AudioDescriptionPath))
            || embeddedSubtitlePlans.Count > 0;

        return new ArchiveIntegrationDecision(
            OutputFilePath: outputPath,
            SkipMux: false,
            SkipReason: null,
            WorkingCopy: needsExistingCopy ? workingCopyPlan : null,
            PrimarySourcePath: request.MainVideoPath,
            PrimaryAudioTrackIds: null,
            PrimarySubtitleTrackIds: null,
            IncludePrimaryAttachments: false,
            AdditionalVideoPaths: newAdditionalVideoPaths,
            AudioDescriptionFilePath: !string.IsNullOrWhiteSpace(request.AudioDescriptionPath)
                ? request.AudioDescriptionPath
                : existingAudioDescription is null || !needsExistingCopy
                    ? null
                    : outputPath,
            AudioDescriptionTrackId: !string.IsNullOrWhiteSpace(request.AudioDescriptionPath)
                ? null
                : existingAudioDescription?.TrackId,
            SubtitleFiles: fallbackSubtitlePlans,
            AttachmentFilePaths: request.AttachmentPaths,
            Notes:
            [
                "Archiv-MKV bereits vorhanden. Die neue Quelle ersetzt die Hauptspuren.",
                bestExistingVideo is null
                    ? $"Neue Hauptquelle wird verwendet: {Path.GetFileName(request.MainVideoPath)}."
                    : $"Neue Videospur ist besser als Archiv: {newPrimaryVideo.Metadata.VideoWidth}px / {newPrimaryVideo.Metadata.VideoCodecLabel} statt {bestExistingVideo.VideoWidth}px / {bestExistingVideo.CodecLabel}."
            ]);
    }

    private static bool IsNewVideoClearlyBetter(MediaTrackMetadata newVideo, ContainerTrackMetadata existingVideo)
    {
        if (newVideo.VideoWidth != existingVideo.VideoWidth)
        {
            return newVideo.VideoWidth > existingVideo.VideoWidth;
        }

        return GetCodecPreferenceRank(newVideo.VideoCodecLabel) < GetCodecPreferenceRank(existingVideo.CodecLabel);
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

    private static IReadOnlyList<string> BuildAttachmentPathsForUsedVideos(IEnumerable<string> usedVideoPaths)
    {
        return usedVideoPaths
            .Select(path => Path.ChangeExtension(path, ".txt"))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static FileCopyPlan BuildWorkingCopyPlan(string archiveFilePath, string workingDirectory)
    {
        var fileName = Path.GetFileNameWithoutExtension(archiveFilePath);
        var extension = Path.GetExtension(archiveFilePath);
        var destinationPath = Path.Combine(workingDirectory, $"{fileName} - Arbeitskopie{extension}");
        var fileSizeBytes = new FileInfo(archiveFilePath).Length;
        return new FileCopyPlan(archiveFilePath, destinationPath, fileSizeBytes);
    }

    private static string EpisodeMetadataPath(string fallbackDirectory, string seriesName, string seasonNumber, string episodeNumber, string title)
    {
        var fileName = $"{seriesName} - S{NormalizeEpisodeNumber(seasonNumber)}E{NormalizeEpisodeNumber(episodeNumber)} - {title}.mkv";
        return Path.Combine(fallbackDirectory, SanitizeFileName(fileName));
    }

    private static string NormalizeEpisodeNumber(string? value)
    {
        return int.TryParse(value, out var number) && number >= 0
            ? number.ToString("00")
            : "xx";
    }

    private static string SanitizePathPart(string value)
    {
        return string.Concat(value.Select(character => Path.GetInvalidPathChars().Contains(character) || character == ':' ? '_' : character)).Trim();
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(fileName.Select(character => invalidCharacters.Contains(character) ? '_' : character));
    }
}

public sealed record ArchiveIntegrationDecision(
    string OutputFilePath,
    bool SkipMux,
    string? SkipReason,
    FileCopyPlan? WorkingCopy,
    string PrimarySourcePath,
    IReadOnlyList<int>? PrimaryAudioTrackIds,
    IReadOnlyList<int>? PrimarySubtitleTrackIds,
    bool IncludePrimaryAttachments,
    IReadOnlyList<string> AdditionalVideoPaths,
    string? AudioDescriptionFilePath,
    int? AudioDescriptionTrackId,
    IReadOnlyList<SubtitleFile> SubtitleFiles,
    IReadOnlyList<string> AttachmentFilePaths,
    IReadOnlyList<string> Notes)
{
    public static ArchiveIntegrationDecision CreateForFreshTarget(string outputPath)
    {
        return new ArchiveIntegrationDecision(
            outputPath,
            SkipMux: false,
            SkipReason: null,
            WorkingCopy: null,
            PrimarySourcePath: string.Empty,
            PrimaryAudioTrackIds: null,
            PrimarySubtitleTrackIds: null,
            IncludePrimaryAttachments: false,
            AdditionalVideoPaths: [],
            AudioDescriptionFilePath: null,
            AudioDescriptionTrackId: null,
            SubtitleFiles: [],
            AttachmentFilePaths: [],
            Notes: []);
    }

    public static ArchiveIntegrationDecision CreateSkip(string outputPath, string skipReason, IReadOnlyList<string> notes)
    {
        return new ArchiveIntegrationDecision(
            outputPath,
            SkipMux: true,
            SkipReason: skipReason,
            WorkingCopy: null,
            PrimarySourcePath: string.Empty,
            PrimaryAudioTrackIds: null,
            PrimarySubtitleTrackIds: null,
            IncludePrimaryAttachments: false,
            AdditionalVideoPaths: [],
            AudioDescriptionFilePath: null,
            AudioDescriptionTrackId: null,
            SubtitleFiles: [],
            AttachmentFilePaths: [],
            Notes: notes);
    }
}
