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
        // Die Archivdatei bleibt nur dann Hauptquelle, wenn die neue Quelle nicht klar besser ist.
        var keepExistingPrimary = bestExistingVideo is not null
            && !IsNewVideoClearlyBetter(newPrimaryVideo.Metadata, bestExistingVideo);
        var additionalVideoPaths = BuildAdditionalVideoPaths(plannedVideos, plannedVideoPaths, existingArchive.VideoTracks, keepExistingPrimary);
        var subtitlePlan = BuildSubtitleReusePlan(outputPath, request.SubtitlePaths, existingArchive.SubtitleTracks);
        var existingAudioDescription = FindExistingAudioDescription(existingArchive.AudioTracks);
        var workingCopyPlan = BuildWorkingCopyPlan(outputPath, Path.GetDirectoryName(request.MainVideoPath)!);
        var replacedSubtitleTracks = GetRemovedSubtitleTracks(existingArchive.SubtitleTracks, subtitlePlan.EmbeddedPlans);

        return keepExistingPrimary
            ? BuildDecisionUsingExistingPrimary(
                outputPath,
                request,
                additionalVideoPaths,
                subtitlePlan,
                replacedSubtitleTracks,
                existingArchive,
                bestExistingVideo,
                existingAudioDescription,
                workingCopyPlan)
            : BuildDecisionReplacingExistingPrimary(
                outputPath,
                request,
                additionalVideoPaths,
                subtitlePlan,
                replacedSubtitleTracks,
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
        var embeddedSubtitlePlans = existingSubtitleTracks
            .Select(track => new
            {
                Track = track,
                Kind = SubtitleKind.FromExistingCodec(track.CodecLabel)
            })
            .Where(entry => entry.Kind is not null)
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

        var embeddedCoverage = embeddedSubtitlePlans
            .Select(BuildSubtitleCoverageKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Vorhandene Untertitel in der Ziel-MKV bleiben für denselben fachlichen Slot erhalten.
        // Ergänzt werden nur fehlende Slots, z. B. ASS zusätzlich zu vorhandenem SRT.
        var externalSubtitlePlans = requestSubtitlePaths
            .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new SubtitleFile(path, SubtitleKind.FromExtension(Path.GetExtension(path))))
            .Where(subtitle => !embeddedCoverage.Contains(BuildSubtitleCoverageKey(subtitle)))
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
        IReadOnlyList<ContainerTrackMetadata> replacedSubtitleTracks,
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
        var attachmentFilePaths = BuildAttachmentPathsForUsedVideos(additionalVideoPaths)
            .Concat(request.AttachmentPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var needsManualAttachments = attachmentFilePaths.Count > 0;

        if (!needsAudioDescription && !needsSubtitleSupplement && !needsAdditionalVideo && !needsManualAttachments)
        {
            return ArchiveIntegrationDecision.CreateSkip(
                outputPath,
                "Die vorhandene MKV in der Serienbibliothek enthält bereits die bevorzugte Videoquelle sowie alle benötigten Zusatzspuren.",
                ["Zieldatei bereits vollständig. Kein erneutes Muxen nötig."]);
        }

        var usageComparison = new ArchiveUsageComparison(
            MainVideo: null,
            AdditionalVideos: null,
            Audio: null,
            AudioDescription: null,
            Subtitles: BuildRemovedSubtitleChange(outputPath, replacedSubtitleTracks),
            Attachments: null);

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
            // Manuell gewählte TXT-Anhänge dürfen auch beim Beibehalten der Archiv-Hauptquelle nicht verschwinden.
            AttachmentFilePaths: attachmentFilePaths,
            FallbackToRequestAttachments: false,
            PreservedAttachmentNames: existingArchive.Container.Attachments.Select(attachment => attachment.FileName).ToList(),
            UsageComparison: usageComparison,
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
        IReadOnlyList<ContainerTrackMetadata> replacedSubtitleTracks,
        ExistingArchiveState existingArchive,
        ContainerTrackMetadata? bestExistingVideo,
        PreparedVideoSource newPrimaryVideo,
        ContainerTrackMetadata? existingAudioDescription,
        FileCopyPlan workingCopyPlan)
    {
        var needsExistingCopy = (existingAudioDescription is not null && string.IsNullOrWhiteSpace(request.AudioDescriptionPath))
            || subtitlePlan.EmbeddedPlans.Count > 0;
        // Sobald Tracks oder Anhänge aus der bestehenden Archivdatei weiterverwendet werden, muss sie als separate Quelle erhalten bleiben.
        var preservedAttachmentNames = needsExistingCopy
            ? existingArchive.Container.Attachments.Select(attachment => attachment.FileName).ToList()
            : [];
        var removedAdditionalVideoTracks = existingArchive.VideoTracks
            .Where(track => bestExistingVideo is null || track.TrackId != bestExistingVideo.TrackId)
            .ToList();
        var removedPrimaryAudio = existingArchive.AudioTracks.FirstOrDefault(track => !track.IsVisualImpaired)
            ?? existingArchive.AudioTracks.FirstOrDefault();
        var usageComparison = new ArchiveUsageComparison(
            MainVideo: bestExistingVideo is null
                ? null
                : new ArchiveUsageChange(
                    BuildVideoTrackLabel(outputPath, bestExistingVideo),
                    $"Neue Videospur hat höhere Qualität: {newPrimaryVideo.Metadata.VideoWidth}px / {newPrimaryVideo.Metadata.VideoCodecLabel}."),
            AdditionalVideos: BuildRemovedAdditionalVideoChange(outputPath, removedAdditionalVideoTracks),
            Audio: removedPrimaryAudio is null
                ? null
                : new ArchiveUsageChange(
                    BuildAudioTrackLabel(outputPath, removedPrimaryAudio),
                    "Die bisherige Tonspur entfällt, weil die Hauptquelle ausgetauscht wird."),
            AudioDescription: BuildRemovedAudioDescriptionChange(
                outputPath,
                existingAudioDescription,
                request.AudioDescriptionPath),
            Subtitles: BuildRemovedSubtitleChange(outputPath, replacedSubtitleTracks),
            Attachments: BuildRemovedAttachmentChange(
                existingArchive.Container.Attachments,
                preservedAttachmentNames,
                request.AttachmentPaths.Count > 0));

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
            UsageComparison: usageComparison,
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
        // Gleicher Codec allein ist kein Ausschluss mehr; nur gleich- oder höherwertige Archivspuren desselben Codecs verdrängen die neue Spur.
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

    private static IReadOnlyList<ContainerTrackMetadata> GetRemovedSubtitleTracks(
        IReadOnlyList<ContainerTrackMetadata> existingSubtitleTracks,
        IReadOnlyList<SubtitleFile> embeddedSubtitlePlans)
    {
        if (existingSubtitleTracks.Count == 0)
        {
            return [];
        }

        var keptTrackIds = embeddedSubtitlePlans
            .Where(plan => plan.EmbeddedTrackId is not null)
            .Select(plan => plan.EmbeddedTrackId!.Value)
            .ToHashSet();

        return existingSubtitleTracks
            .Where(track => !keptTrackIds.Contains(track.TrackId))
            .ToList();
    }

    private static ArchiveUsageChange? BuildRemovedAdditionalVideoChange(
        string outputPath,
        IReadOnlyList<ContainerTrackMetadata> removedAdditionalVideoTracks)
    {
        if (removedAdditionalVideoTracks.Count == 0)
        {
            return null;
        }

        return new ArchiveUsageChange(
            string.Join(Environment.NewLine, removedAdditionalVideoTracks.Select(track => BuildVideoTrackLabel(outputPath, track))),
            "Zusätzliche bisherige Videospuren werden nicht übernommen.");
    }

    private static ArchiveUsageChange? BuildRemovedAudioDescriptionChange(
        string outputPath,
        ContainerTrackMetadata? existingAudioDescription,
        string? requestedAudioDescriptionPath)
    {
        if (existingAudioDescription is null || string.IsNullOrWhiteSpace(requestedAudioDescriptionPath))
        {
            return null;
        }

        return new ArchiveUsageChange(
            BuildAudioTrackLabel(outputPath, existingAudioDescription),
            "Die bisherige AD-Spur wird durch die neu ausgewählte AD-Datei ersetzt.");
    }

    private static ArchiveUsageChange? BuildRemovedSubtitleChange(
        string outputPath,
        IReadOnlyList<ContainerTrackMetadata> removedSubtitleTracks)
    {
        if (removedSubtitleTracks.Count == 0)
        {
            return null;
        }

        return new ArchiveUsageChange(
            string.Join(Environment.NewLine, removedSubtitleTracks.Select(track => BuildSubtitleTrackLabel(outputPath, track))),
            "Diese Untertitel werden nicht übernommen, weil neue oder passendere Untertitel denselben Slot belegen.");
    }

    private static ArchiveUsageChange? BuildRemovedAttachmentChange(
        IReadOnlyList<ContainerAttachmentMetadata> existingAttachments,
        IReadOnlyList<string> preservedAttachmentNames,
        bool hasNewManualAttachments)
    {
        if (existingAttachments.Count == 0)
        {
            return null;
        }

        var removedNames = existingAttachments
            .Select(attachment => attachment.FileName)
            .Where(name => !preservedAttachmentNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (removedNames.Count == 0)
        {
            return null;
        }

        return new ArchiveUsageChange(
            string.Join(Environment.NewLine, removedNames),
            hasNewManualAttachments
                ? "Vorhandene Anhänge werden durch neu ausgewählte Anhänge ersetzt."
                : "Vorhandene Anhänge entfallen, weil die Hauptquelle ausgetauscht wird.");
    }

    private static string BuildVideoTrackLabel(string outputPath, ContainerTrackMetadata track)
    {
        var baseText = string.IsNullOrWhiteSpace(track.TrackName)
            ? $"{track.VideoWidth}px / {track.CodecLabel}"
            : $"{track.TrackName} - {track.VideoWidth}px / {track.CodecLabel}";
        return $"{Path.GetFileName(outputPath)} -> {baseText}";
    }

    private static string BuildAudioTrackLabel(string outputPath, ContainerTrackMetadata track)
    {
        var baseText = string.IsNullOrWhiteSpace(track.TrackName)
            ? $"{MediaLanguageHelper.GetLanguageDisplayName(track.Language)} - {track.CodecLabel}"
            : track.TrackName;
        return $"{Path.GetFileName(outputPath)} -> {baseText}";
    }

    private static string BuildSubtitleTrackLabel(string outputPath, ContainerTrackMetadata track)
    {
        var kind = SubtitleKind.FromExistingCodec(track.CodecLabel);
        var label = kind is null
            ? (string.IsNullOrWhiteSpace(track.TrackName) ? track.CodecLabel : track.TrackName)
            : BuildEmbeddedSubtitleLabel(track, kind);
        return $"{Path.GetFileName(outputPath)} -> {label}";
    }

    private static string BuildSubtitleCoverageKey(SubtitleFile subtitle)
    {
        return BuildSubtitleCoverageKey(subtitle.Kind, subtitle.IsHearingImpaired, subtitle.LanguageCode);
    }

    private static string BuildSubtitleCoverageKey(SubtitleKind kind, bool isHearingImpaired, string? languageCode)
    {
        return $"{kind.DisplayName}|{(isHearingImpaired ? "hi" : "std")}|{MediaLanguageHelper.NormalizeMuxLanguageCode(languageCode)}";
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
