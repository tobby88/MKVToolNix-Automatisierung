using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

// Dieser Partial kapselt die eigentliche Archivanalyse: bestehende Container auswerten, neue Quellen bewerten und die Integrationsentscheidung bauen.
public sealed partial class SeriesArchiveService
{
    /// <summary>
    /// Entscheidet, wie eine vorhandene Archivdatei in einen neuen Mux-Lauf eingebunden werden soll.
    /// </summary>
    /// <param name="mkvMergePath">Pfad zur verwendeten mkvmerge-Executable.</param>
    /// <param name="request">Aktuelle Nutzereingaben für den Mux-Lauf.</param>
    /// <param name="plannedVideoPaths">Geplante Videodateien für den Lauf.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Archiventscheidung inklusive eventueller Arbeitskopie, zu übernehmender Tracks und Notizen.</returns>
    public async Task<ArchiveIntegrationDecision> PrepareAsync(
        string mkvMergePath,
        SeriesEpisodeMuxRequest request,
        IReadOnlyList<string> plannedVideoPaths,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var outputPath = request.OutputFilePath;
        if (!File.Exists(outputPath))
        {
            return ArchiveIntegrationDecision.CreateForFreshTarget(outputPath);
        }

        var existingArchive = await ReadExistingArchiveStateAsync(mkvMergePath, outputPath, cancellationToken);
        var plannedVideos = await ReadPlannedVideoSourcesAsync(mkvMergePath, plannedVideoPaths, cancellationToken);
        var newPrimaryVideo = plannedVideos.FirstOrDefault();
        if (newPrimaryVideo is null)
        {
            return ArchiveIntegrationDecision.CreateForFreshTarget(outputPath);
        }

        var bestExistingVideo = SelectBestExistingVideo(existingArchive.VideoTracks);
        var keepExistingPrimary = bestExistingVideo is not null
            && !IsNewVideoClearlyBetter(newPrimaryVideo.Metadata, bestExistingVideo);
        var additionalVideoPaths = BuildAdditionalVideoPaths(plannedVideos, plannedVideoPaths, existingArchive.VideoTracks, keepExistingPrimary);
        var subtitlePlan = BuildSubtitleReusePlan(outputPath, request.SubtitlePaths, existingArchive.SubtitleTracks);
        var existingAudioDescription = FindExistingAudioDescription(existingArchive.AudioTracks);
        var workingCopyPlan = BuildWorkingCopyPlan(outputPath, Path.GetDirectoryName(request.MainVideoPath)!);

        return keepExistingPrimary
            ? BuildDecisionUsingExistingPrimary(
                outputPath,
                request,
                additionalVideoPaths,
                subtitlePlan,
                existingArchive,
                bestExistingVideo,
                existingAudioDescription,
                workingCopyPlan)
            : BuildDecisionReplacingExistingPrimary(
                outputPath,
                request,
                additionalVideoPaths,
                subtitlePlan,
                existingArchive,
                bestExistingVideo,
                newPrimaryVideo,
                existingAudioDescription,
                workingCopyPlan);
    }

    private async Task<ExistingArchiveState> ReadExistingArchiveStateAsync(
        string mkvMergePath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var container = await _probeService.ReadContainerMetadataAsync(mkvMergePath, outputPath, cancellationToken);
        return new ExistingArchiveState(
            container,
            container.Tracks.Where(track => string.Equals(track.Type, "video", StringComparison.OrdinalIgnoreCase)).ToList(),
            container.Tracks.Where(track => string.Equals(track.Type, "audio", StringComparison.OrdinalIgnoreCase)).ToList(),
            container.Tracks.Where(track => string.Equals(track.Type, "subtitles", StringComparison.OrdinalIgnoreCase)).ToList());
    }

    private async Task<List<PreparedVideoSource>> ReadPlannedVideoSourcesAsync(
        string mkvMergePath,
        IReadOnlyList<string> plannedVideoPaths,
        CancellationToken cancellationToken)
    {
        var plannedVideos = new List<PreparedVideoSource>(plannedVideoPaths.Count);

        foreach (var videoPath in plannedVideoPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            plannedVideos.Add(new PreparedVideoSource(
                videoPath,
                await _probeService.ReadPrimaryVideoMetadataAsync(mkvMergePath, videoPath, cancellationToken)));
        }

        return plannedVideos;
    }

    private static ContainerTrackMetadata? SelectBestExistingVideo(IReadOnlyList<ContainerTrackMetadata> existingVideoTracks)
    {
        return existingVideoTracks
            .OrderByDescending(track => track.VideoWidth)
            .ThenBy(track => MediaCodecPreferenceHelper.GetVideoCodecPreferenceRank(track.CodecLabel))
            .ThenBy(track => track.TrackId)
            .FirstOrDefault();
    }

    private static List<string> BuildAdditionalVideoPaths(
        IReadOnlyList<PreparedVideoSource> plannedVideos,
        IReadOnlyList<string> plannedVideoPaths,
        IReadOnlyList<ContainerTrackMetadata> existingVideoTracks,
        bool keepExistingPrimary)
    {
        if (!keepExistingPrimary)
        {
            return plannedVideoPaths.Skip(1).ToList();
        }

        var newPrimaryVideo = plannedVideos.First();
        return plannedVideos
            .Where(video => !string.Equals(video.FilePath, newPrimaryVideo.FilePath, StringComparison.OrdinalIgnoreCase))
            .Where(video => ShouldKeepAdditionalVideo(video.Metadata, existingVideoTracks))
            .Select(video => video.FilePath)
            .ToList();
    }

    private static SubtitleReusePlan BuildSubtitleReusePlan(
        string outputPath,
        IReadOnlyList<string> requestSubtitlePaths,
        IReadOnlyList<ContainerTrackMetadata> existingSubtitleTracks)
    {
        var externalSubtitlePlans = requestSubtitlePaths
            .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new SubtitleFile(path, SubtitleKind.FromExtension(Path.GetExtension(path))))
            .ToList();

        var externalCoverage = externalSubtitlePlans
            .Select(BuildSubtitleCoverageKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var embeddedSubtitlePlans = existingSubtitleTracks
            .Select(track => new
            {
                Track = track,
                Kind = SubtitleKind.FromExistingCodec(track.CodecLabel)
            })
            .Where(entry => entry.Kind is not null)
            .Where(entry => !externalCoverage.Contains(BuildSubtitleCoverageKey(entry.Kind!, entry.Track.IsHearingImpaired)))
            .OrderBy(entry => entry.Kind!.SortRank)
            .ThenBy(entry => entry.Track.TrackId)
            .Select(entry => new SubtitleFile(
                outputPath,
                entry.Kind!,
                entry.Track.TrackId,
                BuildEmbeddedSubtitleLabel(entry.Track, entry.Kind!),
                MediaLanguageHelper.NormalizeMuxLanguageCode(entry.Track.Language))
            {
                Accessibility = entry.Track.IsHearingImpaired
                    ? SubtitleAccessibility.HearingImpaired
                    : SubtitleAccessibility.Standard
            })
            .ToList();

        return new SubtitleReusePlan(externalSubtitlePlans, embeddedSubtitlePlans);
    }

    private static ContainerTrackMetadata? FindExistingAudioDescription(IReadOnlyList<ContainerTrackMetadata> existingAudioTracks)
    {
        return existingAudioTracks.FirstOrDefault(track =>
            track.IsVisualImpaired
            || track.TrackName.Contains("sehbehinder", StringComparison.OrdinalIgnoreCase)
            || track.TrackName.Contains("audiodeskrip", StringComparison.OrdinalIgnoreCase));
    }

    private ArchiveIntegrationDecision BuildDecisionUsingExistingPrimary(
        string outputPath,
        SeriesEpisodeMuxRequest request,
        IReadOnlyList<string> additionalVideoPaths,
        SubtitleReusePlan subtitlePlan,
        ExistingArchiveState existingArchive,
        ContainerTrackMetadata? bestExistingVideo,
        ContainerTrackMetadata? existingAudioDescription,
        FileCopyPlan workingCopyPlan)
    {
        var primaryAudioTrack = existingArchive.AudioTracks.FirstOrDefault(track => !track.IsVisualImpaired)
            ?? existingArchive.AudioTracks.FirstOrDefault();
        var needsAudioDescription = !string.IsNullOrWhiteSpace(request.AudioDescriptionPath) && existingAudioDescription is null;
        var needsSubtitleSupplement = subtitlePlan.FinalPlans.Count > 0;
        var needsAdditionalVideo = additionalVideoPaths.Count > 0;

        if (!needsAudioDescription && !needsSubtitleSupplement && !needsAdditionalVideo)
        {
            return ArchiveIntegrationDecision.CreateSkip(
                outputPath,
                "Die vorhandene MKV in der Serienbibliothek enthält bereits die bevorzugte Videoquelle sowie alle benötigten Zusatzspuren.",
                ["Zieldatei bereits vollständig. Kein erneutes Muxen nötig."]);
        }

        return new ArchiveIntegrationDecision(
            OutputFilePath: outputPath,
            SkipMux: false,
            SkipReason: null,
            WorkingCopy: workingCopyPlan,
            PrimarySourcePath: outputPath,
            PrimaryAudioTrackIds: primaryAudioTrack is null ? null : [primaryAudioTrack.TrackId],
            PrimarySubtitleTrackIds: subtitlePlan.FinalPlans.Count > 0 ? [] : null,
            IncludePrimaryAttachments: true,
            AttachmentSourcePath: null,
            AdditionalVideoPaths: additionalVideoPaths,
            AudioDescriptionFilePath: existingAudioDescription is null
                ? request.AudioDescriptionPath
                : outputPath,
            AudioDescriptionTrackId: existingAudioDescription?.TrackId,
            SubtitleFiles: subtitlePlan.FinalPlans,
            AttachmentFilePaths: BuildAttachmentPathsForUsedVideos(additionalVideoPaths),
            FallbackToRequestAttachments: false,
            PreservedAttachmentNames: existingArchive.Container.Attachments.Select(attachment => attachment.FileName).ToList(),
            Notes:
            [
                "Archiv-MKV bereits vorhanden. Vor dem Muxen wird eine lokale Arbeitskopie verwendet.",
                bestExistingVideo is null
                    ? "Die vorhandene Archivdatei liefert die Hauptspuren."
                    : $"Vorhandene Videospur wird beibehalten: {bestExistingVideo.VideoWidth}px / {bestExistingVideo.CodecLabel}."
            ]);
    }

    private static ArchiveIntegrationDecision BuildDecisionReplacingExistingPrimary(
        string outputPath,
        SeriesEpisodeMuxRequest request,
        IReadOnlyList<string> additionalVideoPaths,
        SubtitleReusePlan subtitlePlan,
        ExistingArchiveState existingArchive,
        ContainerTrackMetadata? bestExistingVideo,
        PreparedVideoSource newPrimaryVideo,
        ContainerTrackMetadata? existingAudioDescription,
        FileCopyPlan workingCopyPlan)
    {
        var needsExistingCopy = (existingAudioDescription is not null && string.IsNullOrWhiteSpace(request.AudioDescriptionPath))
            || subtitlePlan.EmbeddedPlans.Count > 0;
        var preservedAttachmentNames = needsExistingCopy
            ? existingArchive.Container.Attachments.Select(attachment => attachment.FileName).ToList()
            : [];

        return new ArchiveIntegrationDecision(
            OutputFilePath: outputPath,
            SkipMux: false,
            SkipReason: null,
            WorkingCopy: needsExistingCopy ? workingCopyPlan : null,
            PrimarySourcePath: request.MainVideoPath,
            PrimaryAudioTrackIds: null,
            PrimarySubtitleTrackIds: null,
            IncludePrimaryAttachments: false,
            AttachmentSourcePath: preservedAttachmentNames.Count > 0 ? outputPath : null,
            AdditionalVideoPaths: additionalVideoPaths,
            AudioDescriptionFilePath: !string.IsNullOrWhiteSpace(request.AudioDescriptionPath)
                ? request.AudioDescriptionPath
                : existingAudioDescription is null || !needsExistingCopy
                    ? null
                    : outputPath,
            AudioDescriptionTrackId: !string.IsNullOrWhiteSpace(request.AudioDescriptionPath)
                ? null
                : existingAudioDescription?.TrackId,
            SubtitleFiles: subtitlePlan.FinalPlans,
            AttachmentFilePaths: request.AttachmentPaths,
            FallbackToRequestAttachments: true,
            PreservedAttachmentNames: preservedAttachmentNames,
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

        return MediaCodecPreferenceHelper.GetVideoCodecPreferenceRank(newVideo.VideoCodecLabel)
            < MediaCodecPreferenceHelper.GetVideoCodecPreferenceRank(existingVideo.CodecLabel);
    }

    private static bool ShouldKeepAdditionalVideo(
        MediaTrackMetadata candidate,
        IReadOnlyList<ContainerTrackMetadata> existingVideoTracks)
    {
        var bestExistingWithSameCodec = existingVideoTracks
            .Where(track => string.Equals(track.CodecLabel, candidate.VideoCodecLabel, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(track => track.VideoWidth)
            .ThenBy(track => track.TrackId)
            .FirstOrDefault();

        return bestExistingWithSameCodec is null
            || candidate.VideoWidth > bestExistingWithSameCodec.VideoWidth;
    }

    private static IReadOnlyList<string> BuildAttachmentPathsForUsedVideos(IEnumerable<string> usedVideoPaths)
    {
        return usedVideoPaths
            .Select(path => Path.ChangeExtension(path, ".txt"))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildEmbeddedSubtitleLabel(ContainerTrackMetadata track, SubtitleKind kind)
    {
        if (!string.IsNullOrWhiteSpace(track.TrackName))
        {
            return track.TrackName;
        }

        return $"Archiv-Untertitel {kind.DisplayName}";
    }

    private static string BuildSubtitleCoverageKey(SubtitleFile subtitle)
    {
        return BuildSubtitleCoverageKey(subtitle.Kind, subtitle.IsHearingImpaired);
    }

    private static string BuildSubtitleCoverageKey(SubtitleKind kind, bool isHearingImpaired)
    {
        return $"{kind.DisplayName}|{(isHearingImpaired ? "hi" : "std")}";
    }

    private static FileCopyPlan BuildWorkingCopyPlan(string archiveFilePath, string workingDirectory)
    {
        var archiveInfo = new FileInfo(archiveFilePath);
        var fileName = Path.GetFileNameWithoutExtension(archiveFilePath);
        var extension = Path.GetExtension(archiveFilePath);
        var destinationPath = Path.Combine(workingDirectory, $"{fileName} - Arbeitskopie{extension}");
        return new FileCopyPlan(
            archiveFilePath,
            destinationPath,
            archiveInfo.Length,
            archiveInfo.LastWriteTimeUtc);
    }

    private sealed record ExistingArchiveState(
        ContainerMetadata Container,
        IReadOnlyList<ContainerTrackMetadata> VideoTracks,
        IReadOnlyList<ContainerTrackMetadata> AudioTracks,
        IReadOnlyList<ContainerTrackMetadata> SubtitleTracks);

    private sealed record PreparedVideoSource(string FilePath, MediaTrackMetadata Metadata);

    private sealed record SubtitleReusePlan(
        IReadOnlyList<SubtitleFile> ExternalPlans,
        IReadOnlyList<SubtitleFile> EmbeddedPlans)
    {
        public IReadOnlyList<SubtitleFile> FinalPlans { get; } = ExternalPlans.Concat(EmbeddedPlans).ToList();
    }
}
