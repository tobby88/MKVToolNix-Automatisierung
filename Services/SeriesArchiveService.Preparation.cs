using System.Diagnostics;
using System.Text.Json;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

// Dieser Partial kapselt die eigentliche Archivanalyse: bestehende Container auswerten, neue Quellen bewerten und die Integrationsentscheidung bauen.
public sealed partial class SeriesArchiveService
{
    /// <summary>
    /// Entscheidet, wie eine vorhandene Archivdatei in einen neuen Mux-Lauf eingebunden werden soll.
    /// Bereits vorhandene Custom-Ziele außerhalb der Serienbibliothek gelten dabei bewusst nicht als wiederverwendbares Archiv.
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
        if (!ShouldReuseExistingArchive(outputPath))
        {
            return ArchiveIntegrationDecision.CreateForFreshTarget(outputPath);
        }

        var existingArchive = await ReadExistingArchiveStateAsync(mkvMergePath, outputPath, cancellationToken);
        var plannedVideos = await ReadPlannedVideoSourcesAsync(mkvMergePath, plannedVideoPaths, cancellationToken);
        var preferredExistingVideoTracks = SelectBestExistingVideoTracks(existingArchive.VideoTracks);
        var bestExistingVideo = preferredExistingVideoTracks.FirstOrDefault();
        var subtitlePlan = BuildSubtitleReusePlan(outputPath, request.SubtitlePaths, existingArchive.SubtitleTracks);
        var existingAudioDescription = FindExistingAudioDescription(existingArchive.AudioTracks);
        var workingCopyPlan = BuildWorkingCopyPlan(outputPath, ResolveWorkingDirectory(request, outputPath));
        var replacedSubtitleTracks = GetRemovedSubtitleTracks(existingArchive.SubtitleTracks, subtitlePlan.EmbeddedPlans);
        var videoPlan = BuildFinalVideoSelectionPlan(
            outputPath,
            plannedVideos,
            preferredExistingVideoTracks,
            existingArchive.VideoTracks);
        var attachmentReusePlan = await BuildAttachmentReusePlanAsync(
            mkvMergePath,
            outputPath,
            existingArchive.Container.Attachments,
            existingArchive.VideoTracks,
            videoPlan.VideoSelections,
            cancellationToken);

        if (plannedVideos.Count == 0)
        {
            return BuildDecisionUsingExistingPrimary(
                outputPath,
                request,
                videoPlan,
                subtitlePlan,
                replacedSubtitleTracks,
                existingArchive,
                bestExistingVideo,
                existingAudioDescription,
                workingCopyPlan,
                attachmentReusePlan);
        }

        var selectedPrimaryVideo = videoPlan.VideoSelections.FirstOrDefault()
            ?? throw new InvalidOperationException("Es konnte keine fachlich gültige Videospurauswahl für den Archivabgleich bestimmt werden.");
        var keepExistingPrimary = string.Equals(selectedPrimaryVideo.FilePath, outputPath, StringComparison.OrdinalIgnoreCase);
        var newPrimaryVideo = keepExistingPrimary
            ? null
            : plannedVideos.FirstOrDefault(video => string.Equals(video.FilePath, selectedPrimaryVideo.FilePath, StringComparison.OrdinalIgnoreCase));

        return keepExistingPrimary
            ? BuildDecisionUsingExistingPrimary(
                outputPath,
                request,
                videoPlan,
                subtitlePlan,
                replacedSubtitleTracks,
                existingArchive,
                bestExistingVideo,
                existingAudioDescription,
                workingCopyPlan,
                attachmentReusePlan)
            : BuildDecisionReplacingExistingPrimary(
                outputPath,
                request,
                videoPlan,
                subtitlePlan,
                replacedSubtitleTracks,
                existingArchive,
                bestExistingVideo,
                newPrimaryVideo ?? throw new InvalidOperationException("Die ausgewählte neue Hauptvideospur konnte nicht mehr einer frischen Quelle zugeordnet werden."),
                existingAudioDescription,
                workingCopyPlan,
                attachmentReusePlan);
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
                await _probeService.ReadPrimaryVideoMetadataAsync(mkvMergePath, videoPath, cancellationToken),
                new FileInfo(videoPath).Length));
        }

        return plannedVideos;
    }

    private static IReadOnlyList<ContainerTrackMetadata> SelectBestExistingVideoTracks(IReadOnlyList<ContainerTrackMetadata> existingVideoTracks)
    {
        return existingVideoTracks
            .GroupBy(track => BuildVideoSlotKey(track.Language, track.CodecLabel), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(track => track.VideoWidth)
                .ThenBy(track => track.IsDefaultTrack ? 0 : 1)
                .ThenBy(track => track.TrackId)
                .First())
            .OrderBy(track => MediaLanguageHelper.GetLanguageSortRank(track.Language))
            .ThenBy(track => MediaCodecPreferenceHelper.GetVideoCodecPreferenceRank(track.CodecLabel))
            .ThenByDescending(track => track.VideoWidth)
            .ThenBy(track => track.TrackId)
            .ToList();
    }

    private static FinalVideoSelectionPlan BuildFinalVideoSelectionPlan(
        string outputPath,
        IReadOnlyList<PreparedVideoSource> plannedVideos,
        IReadOnlyList<ContainerTrackMetadata> preferredExistingVideoTracks,
        IReadOnlyList<ContainerTrackMetadata> allExistingVideoTracks)
    {
        var freshBySlot = plannedVideos
            .GroupBy(video => BuildVideoSlotKey(video.Metadata.VideoLanguage, video.Metadata.VideoCodecLabel), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(video => video.Metadata.VideoWidth)
                    .ThenByDescending(video => video.FileSizeBytes)
                    .ThenBy(video => video.FilePath, StringComparer.OrdinalIgnoreCase)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
        var existingBySlot = preferredExistingVideoTracks.ToDictionary(
            track => BuildVideoSlotKey(track.Language, track.CodecLabel),
            track => track,
            StringComparer.OrdinalIgnoreCase);

        var selectedVideos = new List<VideoTrackSelection>();
        var retainedExistingTrackIds = new HashSet<int>();

        foreach (var slot in freshBySlot.Keys
                     .Concat(existingBySlot.Keys)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(slot => MediaLanguageHelper.GetLanguageSortRank(GetLanguageCodeFromSlotKey(slot)))
                     .ThenBy(slot => MediaCodecPreferenceHelper.GetVideoCodecPreferenceRank(GetCodecLabelFromSlotKey(slot)))
                     .ThenBy(slot => slot, StringComparer.OrdinalIgnoreCase))
        {
            if (freshBySlot.TryGetValue(slot, out var freshVideo)
                && existingBySlot.TryGetValue(slot, out var existingVideo))
            {
                if (ShouldReplaceExistingVideoForSameSlot(freshVideo.Metadata, existingVideo))
                {
                    selectedVideos.Add(CreateVideoTrackSelection(freshVideo));
                }
                else
                {
                    selectedVideos.Add(CreateVideoTrackSelection(outputPath, existingVideo));
                    retainedExistingTrackIds.Add(existingVideo.TrackId);
                }

                continue;
            }

            if (freshBySlot.TryGetValue(slot, out freshVideo))
            {
                selectedVideos.Add(CreateVideoTrackSelection(freshVideo));
                continue;
            }

            if (existingBySlot.TryGetValue(slot, out var retainedExistingVideo))
            {
                selectedVideos.Add(CreateVideoTrackSelection(outputPath, retainedExistingVideo));
                retainedExistingTrackIds.Add(retainedExistingVideo.TrackId);
            }
        }

        var selectedVideoOrder = selectedVideos
            .Where(selection => string.Equals(selection.FilePath, outputPath, StringComparison.OrdinalIgnoreCase))
            .Select((selection, index) => new { selection.TrackId, Index = index })
            .ToDictionary(entry => entry.TrackId, entry => entry.Index);
        var retainedExistingTracks = allExistingVideoTracks
            .Where(track => retainedExistingTrackIds.Contains(track.TrackId))
            .OrderBy(track => selectedVideoOrder[track.TrackId])
            .ToList();
        var removedExistingTracks = allExistingVideoTracks
            .Where(track => !retainedExistingTrackIds.Contains(track.TrackId))
            .OrderBy(track => MediaLanguageHelper.GetLanguageSortRank(track.Language))
            .ThenBy(track => MediaCodecPreferenceHelper.GetVideoCodecPreferenceRank(track.CodecLabel))
            .ThenByDescending(track => track.VideoWidth)
            .ThenBy(track => track.TrackId)
            .ToList();

        return new FinalVideoSelectionPlan(selectedVideos, retainedExistingTracks, removedExistingTracks);
    }

    private static VideoTrackSelection CreateVideoTrackSelection(PreparedVideoSource video)
    {
        return new VideoTrackSelection(
            video.FilePath,
            video.Metadata.VideoTrackId,
            video.Metadata.VideoWidth,
            video.Metadata.VideoCodecLabel,
            MediaLanguageHelper.NormalizeMuxLanguageCode(video.Metadata.VideoLanguage));
    }

    private static VideoTrackSelection CreateVideoTrackSelection(string outputPath, ContainerTrackMetadata track)
    {
        return new VideoTrackSelection(
            outputPath,
            track.TrackId,
            track.VideoWidth,
            track.CodecLabel,
            MediaLanguageHelper.NormalizeMuxLanguageCode(track.Language));
    }

    private static bool ShouldReplaceExistingVideoForSameSlot(MediaTrackMetadata newVideo, ContainerTrackMetadata existingVideo)
    {
        if (newVideo.VideoWidth != existingVideo.VideoWidth)
        {
            return newVideo.VideoWidth > existingVideo.VideoWidth;
        }

        return MediaCodecPreferenceHelper.GetVideoCodecPreferenceRank(newVideo.VideoCodecLabel)
            < MediaCodecPreferenceHelper.GetVideoCodecPreferenceRank(existingVideo.CodecLabel);
    }

    private static string BuildVideoSlotKey(string? languageCode, string codecLabel)
    {
        return $"{MediaLanguageHelper.NormalizeMuxLanguageCode(languageCode)}|{codecLabel.Trim().ToUpperInvariant()}";
    }

    private static string GetLanguageCodeFromSlotKey(string slotKey)
    {
        return slotKey.Split('|', 2)[0];
    }

    private static string GetCodecLabelFromSlotKey(string slotKey)
    {
        var parts = slotKey.Split('|', 2);
        return parts.Length == 2 ? parts[1] : slotKey;
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
            .Select(entry => SubtitleFile.CreateEmbedded(
                    outputPath,
                    entry.Kind!,
                    entry.Track.TrackId,
                    BuildEmbeddedSubtitleLabel(entry.Track, entry.Kind!),
                    entry.Track.Language)
                with
                {
                    Accessibility = entry.Track.IsHearingImpaired
                        ? SubtitleAccessibility.HearingImpaired
                        : SubtitleAccessibility.Standard
                })
            .ToList();

        var embeddedCoverage = embeddedSubtitlePlans
            // Externe Untertitel werden projektweit weiterhin vorsichtig als HI/SDH markiert,
            // solange keine sichere Unterscheidung vorliegt. Diese konservative Anzeige darf im
            // Archivabgleich aber nicht dazu führen, dass dieselbe Sprache und derselbe Typ noch
            // einmal extern angehängt werden, obwohl die Ziel-MKV den fachlichen Slot bereits
            // belegt. Für die Wiederverwendungsentscheidung zählt deshalb bewusst nur
            // Typ + Sprache; die genaue Accessibility-Markierung des vorhandenen Archivtracks
            // bleibt für Track-Metadaten und GUI sichtbar, steuert hier aber keine Duplikate.
            .Select(BuildSubtitleReuseCoverageKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Vorhandene Untertitel in der Ziel-MKV bleiben für denselben fachlichen Slot erhalten.
        // Ergänzt werden nur fehlende Slots, z. B. ASS zusätzlich zu vorhandenem SRT.
        var externalSubtitlePlans = requestSubtitlePaths
            .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => SubtitleFile.CreateDetectedExternal(path, SubtitleKind.FromExtension(Path.GetExtension(path))))
            .Where(subtitle => !embeddedCoverage.Contains(BuildSubtitleReuseCoverageKey(subtitle)))
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
        FinalVideoSelectionPlan videoPlan,
        SubtitleReusePlan subtitlePlan,
        IReadOnlyList<ContainerTrackMetadata> replacedSubtitleTracks,
        ExistingArchiveState existingArchive,
        ContainerTrackMetadata? bestExistingVideo,
        ContainerTrackMetadata? existingAudioDescription,
        FileCopyPlan workingCopyPlan,
        AttachmentReusePlan attachmentReusePlan)
    {
        var primaryAudioTrack = existingArchive.AudioTracks.FirstOrDefault(track => !track.IsVisualImpaired)
            ?? existingArchive.AudioTracks.FirstOrDefault();
        var manualAttachmentPaths = request.ManualAttachmentPaths ?? [];
        var needsAudioDescription = !string.IsNullOrWhiteSpace(request.AudioDescriptionPath) && existingAudioDescription is null;
        // Bereits vorhandene eingebettete Archiv-Untertitel sind für sich genommen kein Änderungsgrund.
        // Ein echter Zusatzbedarf liegt nur vor, wenn neue externe Untertitel dazukommen.
        var needsSubtitleSupplement = subtitlePlan.ExternalPlans.Count > 0;
        var freshVideoPaths = videoPlan.VideoSelections
            .Where(selection => !string.Equals(selection.FilePath, outputPath, StringComparison.OrdinalIgnoreCase))
            .Select(selection => selection.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var needsAdditionalVideo = freshVideoPaths.Count > 0;
        var needsVideoCleanup = videoPlan.RemovedExistingTracks.Count > 0;
        var attachmentFilePaths = BuildAttachmentPathsForUsedVideos(freshVideoPaths)
            .Concat(manualAttachmentPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var needsManualAttachments = manualAttachmentPaths.Count > 0;
        var requiresRelevantTrackNameNormalization = RequiresRelevantTrackNameNormalization(
            videoPlan.RetainedExistingTracks,
            primaryAudioTrack,
            existingAudioDescription,
            existingArchive.SubtitleTracks,
            subtitlePlan.EmbeddedPlans);

        if (!needsAudioDescription
            && !needsSubtitleSupplement
            && !needsAdditionalVideo
            && !needsVideoCleanup
            && !needsManualAttachments
            && !requiresRelevantTrackNameNormalization)
        {
            return ArchiveIntegrationDecision.CreateSkip(
                outputPath,
                "Die vorhandene MKV in der Serienbibliothek enthält bereits alle benötigten Inhalte; ein erneutes Muxen ist nicht nötig.",
                BuildReuseOnlySkipUsageSummary(
                    outputPath,
                    videoPlan.RetainedExistingTracks,
                    primaryAudioTrack,
                    existingAudioDescription,
                    subtitlePlan,
                    attachmentReusePlan.PreservedAttachmentNames),
                ["Zieldatei bereits vollständig. Alle relevanten Spurnamen sind bereits konsistent. Kein erneutes Muxen nötig."]);
        }

        var usageComparison = new ArchiveUsageComparison(
            MainVideo: null,
            AdditionalVideos: BuildRemovedAdditionalVideoChange(outputPath, videoPlan.RemovedExistingTracks),
            Audio: null,
            AudioDescription: null,
            Subtitles: BuildRemovedSubtitleChange(outputPath, replacedSubtitleTracks),
            Attachments: BuildRemovedAttachmentChange(
                existingArchive.Container.Attachments,
                attachmentReusePlan.PreservedAttachmentNames,
                manualAttachmentPaths.Count > 0));

        return new ArchiveIntegrationDecision(
            OutputFilePath: outputPath,
            SkipMux: false,
            SkipReason: null,
            WorkingCopy: workingCopyPlan,
            PrimarySourcePath: outputPath,
            VideoSelections: videoPlan.VideoSelections,
            PrimaryAudioTrackIds: primaryAudioTrack is null ? null : [primaryAudioTrack.TrackId],
            PrimarySubtitleTrackIds: subtitlePlan.FinalPlans.Count > 0 ? [] : null,
            PrimarySourceAttachmentIds: attachmentReusePlan.PrimarySourceAttachmentIds,
            IncludePrimaryAttachments: attachmentReusePlan.IncludePrimarySourceAttachments,
            AttachmentSourcePath: attachmentReusePlan.AttachmentSourcePath,
            AttachmentSourceAttachmentIds: attachmentReusePlan.AttachmentSourceAttachmentIds,
            AdditionalVideoPaths: freshVideoPaths,
            AudioDescriptionFilePath: existingAudioDescription is null
                ? request.AudioDescriptionPath
                : outputPath,
            AudioDescriptionTrackId: existingAudioDescription?.TrackId,
            SubtitleFiles: subtitlePlan.FinalPlans,
            // Manuell gewählte TXT-Anhänge dürfen auch beim Beibehalten der Archiv-Hauptquelle nicht verschwinden.
            AttachmentFilePaths: attachmentFilePaths,
            FallbackToRequestAttachments: false,
            PreservedAttachmentNames: attachmentReusePlan.PreservedAttachmentNames,
            UsageComparison: usageComparison,
            SkipUsageSummary: null,
            Notes: BuildKeepExistingPrimaryNotes(
                bestExistingVideo,
                needsAudioDescription,
                needsSubtitleSupplement,
                needsAdditionalVideo,
                needsVideoCleanup,
                needsManualAttachments,
                requiresRelevantTrackNameNormalization));
    }

    private static IReadOnlyList<string> BuildKeepExistingPrimaryNotes(
        ContainerTrackMetadata? bestExistingVideo,
        bool needsAudioDescription,
        bool needsSubtitleSupplement,
        bool needsAdditionalVideo,
        bool needsVideoCleanup,
        bool needsManualAttachments,
        bool requiresRelevantTrackNameNormalization)
    {
        var notes = new List<string>
        {
            "Archiv-MKV bereits vorhanden. Vor dem Muxen wird eine lokale Arbeitskopie verwendet.",
            bestExistingVideo is null
                ? "Die vorhandene Archivdatei liefert die Hauptspuren."
                : $"Vorhandene Videospur wird beibehalten: {bestExistingVideo.VideoWidth}px / {bestExistingVideo.CodecLabel}."
        };

        if (needsVideoCleanup)
        {
            notes.Add("Vorhandene Videospuren werden auf die projektweit bevorzugten Sprach-/Codec-Slots bereinigt.");
        }

        if (!needsAudioDescription
            && !needsSubtitleSupplement
            && !needsAdditionalVideo
            && !needsVideoCleanup
            && !needsManualAttachments
            && requiresRelevantTrackNameNormalization)
        {
            // Dieser Sonderfall soll sichtbar machen, dass inhaltlich nichts ergänzt oder ersetzt wird.
            // Der erneute Lauf dient dann ausschließlich dazu, vorhandene Tracknamen an die heute
            // projektweit erwartete Benennung anzupassen.
            notes.Add("Alle Inhalte sind bereits vorhanden. Es werden nur die Benennungen der relevanten Spuren vereinheitlicht.");
        }

        return notes;
    }

    private static ArchiveIntegrationDecision BuildDecisionReplacingExistingPrimary(
        string outputPath,
        SeriesEpisodeMuxRequest request,
        FinalVideoSelectionPlan videoPlan,
        SubtitleReusePlan subtitlePlan,
        IReadOnlyList<ContainerTrackMetadata> replacedSubtitleTracks,
        ExistingArchiveState existingArchive,
        ContainerTrackMetadata? bestExistingVideo,
        PreparedVideoSource newPrimaryVideo,
        ContainerTrackMetadata? existingAudioDescription,
        FileCopyPlan workingCopyPlan,
        AttachmentReusePlan attachmentReusePlan)
    {
        var manualAttachmentPaths = request.ManualAttachmentPaths ?? [];
        var needsExistingCopy = videoPlan.RetainedExistingTracks.Count > 0
            || (existingAudioDescription is not null && string.IsNullOrWhiteSpace(request.AudioDescriptionPath))
            || subtitlePlan.EmbeddedPlans.Count > 0
            || attachmentReusePlan.PreservedAttachmentNames.Count > 0;
        var removedAdditionalVideoTracks = videoPlan.RemovedExistingTracks
            .Where(track => bestExistingVideo is null || track.TrackId != bestExistingVideo.TrackId)
            .ToList();
        var removedPrimaryAudio = existingArchive.AudioTracks.FirstOrDefault(track => !track.IsVisualImpaired)
            ?? existingArchive.AudioTracks.FirstOrDefault();
        var usageComparison = new ArchiveUsageComparison(
            MainVideo: bestExistingVideo is null
                ? null
                : new ArchiveUsageChange(
                    BuildVideoTrackLabel(outputPath, bestExistingVideo),
                    BuildPrimaryVideoReplacementReason(bestExistingVideo, newPrimaryVideo)),
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
                attachmentReusePlan.PreservedAttachmentNames,
                manualAttachmentPaths.Count > 0));

        return new ArchiveIntegrationDecision(
            OutputFilePath: outputPath,
            SkipMux: false,
            SkipReason: null,
            SkipUsageSummary: null,
            WorkingCopy: needsExistingCopy ? workingCopyPlan : null,
            PrimarySourcePath: videoPlan.VideoSelections[0].FilePath,
            VideoSelections: videoPlan.VideoSelections,
            PrimaryAudioTrackIds: null,
            PrimarySubtitleTrackIds: null,
            PrimarySourceAttachmentIds: null,
            IncludePrimaryAttachments: false,
            AttachmentSourcePath: attachmentReusePlan.AttachmentSourcePath,
            AttachmentSourceAttachmentIds: attachmentReusePlan.AttachmentSourceAttachmentIds,
            AdditionalVideoPaths: videoPlan.VideoSelections
                .Skip(1)
                .Where(selection => !string.Equals(selection.FilePath, outputPath, StringComparison.OrdinalIgnoreCase))
                .Select(selection => selection.FilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
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
            PreservedAttachmentNames: attachmentReusePlan.PreservedAttachmentNames,
            UsageComparison: usageComparison,
            Notes:
            [
                "Archiv-MKV bereits vorhanden. Die neue Quelle ersetzt die Hauptspuren.",
                bestExistingVideo is null
                    ? $"Neue Hauptquelle wird verwendet: {Path.GetFileName(videoPlan.VideoSelections[0].FilePath)}."
                    : BuildPrimaryVideoReplacementReason(bestExistingVideo, newPrimaryVideo)
            ]);
    }

    private static EpisodeUsageSummary BuildReuseOnlySkipUsageSummary(
        string outputPath,
        IReadOnlyList<ContainerTrackMetadata> retainedExistingVideoTracks,
        ContainerTrackMetadata? primaryAudioTrack,
        ContainerTrackMetadata? existingAudioDescription,
        SubtitleReusePlan subtitlePlan,
        IReadOnlyList<string> preservedAttachmentNames)
    {
        var bestExistingVideo = retainedExistingVideoTracks.FirstOrDefault();
        var additionalVideoTracks = retainedExistingVideoTracks.Skip(1).ToList();

        return new EpisodeUsageSummary(
            ArchiveAction: "Zieldatei bereits aktuell",
            ArchiveDetails: "Alle benötigten Inhalte und relevanten Spurnamen sind bereits vorhanden. Kein erneutes Muxen nötig.",
            MainVideo: new EpisodeUsageEntry(
                bestExistingVideo is null
                    ? BuildExistingTargetDisplayText(Path.GetFileName(outputPath))
                    : BuildExistingTargetDisplayText(GetExistingVideoDisplayLabel(bestExistingVideo)),
                null,
                null),
            AdditionalVideos: new EpisodeUsageEntry(
                additionalVideoTracks.Count == 0
                    ? "(keine)"
                    : string.Join(
                        Environment.NewLine,
                        additionalVideoTracks.Select(track => BuildExistingTargetDisplayText(GetExistingVideoDisplayLabel(track)))),
                null,
                null),
            Audio: new EpisodeUsageEntry(
                primaryAudioTrack is null
                    ? "(keine)"
                    : BuildExistingTargetDisplayText(BuildExpectedAudioTrackName(primaryAudioTrack)),
                null,
                null),
            AudioDescription: new EpisodeUsageEntry(
                existingAudioDescription is null
                    ? "(keine)"
                    : BuildExistingTargetDisplayText(
                        BuildExpectedAudioDescriptionTrackName(existingAudioDescription, primaryAudioTrack?.Language)),
                null,
                null),
            Subtitles: new EpisodeUsageEntry(
                subtitlePlan.EmbeddedPlans.Count == 0
                    ? "(keine)"
                    : string.Join(
                        Environment.NewLine,
                        subtitlePlan.EmbeddedPlans.Select(subtitle => BuildExistingTargetDisplayText(subtitle.TrackName))),
                null,
                null),
            Attachments: new EpisodeUsageEntry(
                preservedAttachmentNames.Count == 0
                    ? "keine"
                    : string.Join(
                        ", ",
                        preservedAttachmentNames.Select(BuildExistingTargetDisplayText)),
                null,
                null));
    }

    private static bool RequiresRelevantTrackNameNormalization(
        IReadOnlyList<ContainerTrackMetadata> retainedExistingVideoTracks,
        ContainerTrackMetadata? primaryAudioTrack,
        ContainerTrackMetadata? existingAudioDescription,
        IReadOnlyList<ContainerTrackMetadata> existingSubtitleTracks,
        IReadOnlyList<SubtitleFile> embeddedSubtitlePlans)
    {
        foreach (var existingVideoTrack in retainedExistingVideoTracks)
        {
            if (!HasConsistentTrackName(existingVideoTrack.TrackName, BuildExpectedVideoTrackName(existingVideoTrack)))
            {
                return true;
            }
        }

        if (primaryAudioTrack is not null
            && !HasConsistentTrackName(primaryAudioTrack.TrackName, BuildExpectedAudioTrackName(primaryAudioTrack)))
        {
            return true;
        }

        if (existingAudioDescription is not null
            && !HasConsistentTrackName(
                existingAudioDescription.TrackName,
                BuildExpectedAudioDescriptionTrackName(existingAudioDescription, primaryAudioTrack?.Language)))
        {
            return true;
        }

        foreach (var subtitlePlan in embeddedSubtitlePlans)
        {
            if (subtitlePlan.EmbeddedTrackId is not int embeddedTrackId)
            {
                continue;
            }

            var existingTrack = existingSubtitleTracks.FirstOrDefault(track => track.TrackId == embeddedTrackId);
            if (existingTrack is not null && !HasConsistentTrackName(existingTrack.TrackName, subtitlePlan.TrackName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasConsistentTrackName(string? currentTrackName, string expectedTrackName)
    {
        return string.Equals(
            (currentTrackName ?? string.Empty).Trim(),
            expectedTrackName.Trim(),
            StringComparison.Ordinal);
    }

    private static string GetExistingVideoDisplayLabel(ContainerTrackMetadata track)
    {
        return string.IsNullOrWhiteSpace(track.TrackName)
            ? BuildExpectedVideoTrackName(track)
            : track.TrackName;
    }

    private static string BuildExpectedVideoTrackName(ContainerTrackMetadata track)
    {
        return $"{MediaLanguageHelper.GetLanguageDisplayName(track.Language)} - {ResolutionLabel.FromWidth(track.VideoWidth).Value} - {track.CodecLabel}";
    }

    private static string BuildExpectedAudioTrackName(ContainerTrackMetadata track)
    {
        return $"{MediaLanguageHelper.GetLanguageDisplayName(track.Language)} - {track.CodecLabel}";
    }

    private static string BuildExpectedAudioDescriptionTrackName(ContainerTrackMetadata track, string? fallbackLanguage)
    {
        var languageCode = string.IsNullOrWhiteSpace(track.Language)
            ? fallbackLanguage
            : track.Language;
        return $"{MediaLanguageHelper.GetLanguageDisplayName(languageCode)} (sehbehinderte) - {track.CodecLabel}";
    }

    private static string BuildExistingTargetDisplayText(string value)
    {
        return $"Aus Zieldatei: {value}";
    }

    /// <summary>
    /// Prüft, ob eine vorhandene Zieldatei fachlich als wiederverwendbares Archiv behandelt werden darf.
    /// </summary>
    /// <param name="outputPath">Tatsächlich geplanter Ausgabepfad des Mux-Laufs.</param>
    /// <returns>
    /// <see langword="true"/>, wenn die Datei existiert und innerhalb der konfigurierten Serienbibliothek liegt;
    /// andernfalls <see langword="false"/>, damit manuell gewählte Custom-Ziele als normale Overwrite-Ziele behandelt werden.
    /// </returns>
    private bool ShouldReuseExistingArchive(string outputPath)
    {
        return File.Exists(outputPath) && IsArchivePath(outputPath);
    }

    private static string BuildPrimaryVideoReplacementReason(
        ContainerTrackMetadata existingPrimaryVideo,
        PreparedVideoSource newPrimaryVideo)
    {
        if (ShouldReplaceExistingVideoForSameSlot(newPrimaryVideo.Metadata, existingPrimaryVideo))
        {
            return $"Neue Videospur hat höhere Qualität: {newPrimaryVideo.Metadata.VideoWidth}px / {newPrimaryVideo.Metadata.VideoCodecLabel} statt {existingPrimaryVideo.VideoWidth}px / {existingPrimaryVideo.CodecLabel}.";
        }

        return "Neue Videospur belegt einen bevorzugten Sprach-/Codec-Slot und wird deshalb an die erste Stelle gezogen.";
    }

    private async Task<AttachmentReusePlan> BuildAttachmentReusePlanAsync(
        string mkvMergePath,
        string outputPath,
        IReadOnlyList<ContainerAttachmentMetadata> existingAttachments,
        IReadOnlyList<ContainerTrackMetadata> existingVideoTracks,
        IReadOnlyList<VideoTrackSelection> selectedVideoTracks,
        CancellationToken cancellationToken)
    {
        if (existingAttachments.Count == 0)
        {
            return AttachmentReusePlan.None;
        }

        var preservedAttachmentIds = new HashSet<int>(
            existingAttachments
                .Where(attachment => !IsTextAttachment(attachment.FileName))
                .Select(attachment => attachment.Id));
        var textAttachments = existingAttachments
            .Where(attachment => IsTextAttachment(attachment.FileName))
            .ToList();
        var keptExistingTrackIds = selectedVideoTracks
            .Where(selection => string.Equals(selection.FilePath, outputPath, StringComparison.OrdinalIgnoreCase))
            .Select(selection => selection.TrackId)
            .ToHashSet();
        var removedExistingTrackIds = existingVideoTracks
            .Where(track => !keptExistingTrackIds.Contains(track.TrackId))
            .Select(track => track.TrackId)
            .ToHashSet();
        var singleExistingVideo = existingVideoTracks.Count == 1
            ? existingVideoTracks[0]
            : null;
        var keepsSingleExistingVideo = singleExistingVideo is not null
            && keptExistingTrackIds.Contains(singleExistingVideo.TrackId);

        if (textAttachments.Count > 0)
        {
            var textAttachmentMatches = await MatchTextAttachmentsToExistingTracksAsync(
                mkvMergePath,
                outputPath,
                textAttachments,
                existingVideoTracks,
                cancellationToken);
            var uniquelyMatchedRemovedAttachmentIds = textAttachmentMatches
                .Where(match => match.IsStrongMatch && match.MatchedTrackId is not null)
                .GroupBy(match => match.MatchedTrackId!.Value)
                .Where(group => group.Count() == 1)
                .Select(group => group.Single())
                .Where(match => removedExistingTrackIds.Contains(match.MatchedTrackId!.Value))
                .Select(match => match.AttachmentId)
                .ToHashSet();

            foreach (var textAttachment in textAttachments)
            {
                if (!uniquelyMatchedRemovedAttachmentIds.Contains(textAttachment.Id))
                {
                    preservedAttachmentIds.Add(textAttachment.Id);
                }
            }
        }

        // Nur im einzig wirklich sicheren Fall darf eine bestehende TXT automatisch verworfen werden:
        // genau eine vorhandene Videospur, genau eine TXT im Container und diese Videospur wird ersetzt.
        // Die neue Heuristik oben darf darüber hinaus ebenfalls nur eindeutige Zuordnungen verwerfen.
        // Wenn sie nichts Sicheres liefern kann, bleibt dieser alte, explizit sichere Fallback aktiv.
        if (singleExistingVideo is not null
            && !keepsSingleExistingVideo
            && textAttachments.Count == 1
            && preservedAttachmentIds.Contains(textAttachments[0].Id))
        {
            preservedAttachmentIds.Remove(textAttachments[0].Id);
        }

        if (preservedAttachmentIds.Count == 0)
        {
            return AttachmentReusePlan.None;
        }

        var preservedAttachments = existingAttachments
            .Where(attachment => preservedAttachmentIds.Contains(attachment.Id))
            .ToList();
        var preservedAttachmentNames = preservedAttachments
            .Select(attachment => attachment.FileName)
            .ToList();
        var primaryUsesArchive = selectedVideoTracks.Count > 0
            && string.Equals(selectedVideoTracks[0].FilePath, outputPath, StringComparison.OrdinalIgnoreCase);

        return primaryUsesArchive
            ? new AttachmentReusePlan(
                IncludePrimarySourceAttachments: true,
                PrimarySourceAttachmentIds: preservedAttachments.Select(attachment => attachment.Id).ToList(),
                AttachmentSourcePath: null,
                AttachmentSourceAttachmentIds: null,
                PreservedAttachmentNames: preservedAttachmentNames)
            : new AttachmentReusePlan(
                IncludePrimarySourceAttachments: false,
                PrimarySourceAttachmentIds: null,
                AttachmentSourcePath: outputPath,
                AttachmentSourceAttachmentIds: preservedAttachments.Select(attachment => attachment.Id).ToList(),
                PreservedAttachmentNames: preservedAttachmentNames);
    }

    private async Task<IReadOnlyList<AttachmentTrackMatch>> MatchTextAttachmentsToExistingTracksAsync(
        string mkvMergePath,
        string outputPath,
        IReadOnlyList<ContainerAttachmentMetadata> textAttachments,
        IReadOnlyList<ContainerTrackMetadata> existingVideoTracks,
        CancellationToken cancellationToken)
    {
        var matches = new List<AttachmentTrackMatch>(textAttachments.Count);

        foreach (var textAttachment in textAttachments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var details = await TryReadEmbeddedTextAttachmentDetailsAsync(
                mkvMergePath,
                outputPath,
                textAttachment,
                cancellationToken);
            var profile = BuildAttachmentTextHeuristicProfile(textAttachment.FileName, details);
            matches.Add(MatchTextAttachmentToTrack(textAttachment, profile, existingVideoTracks));
        }

        return matches;
    }

    private static AttachmentTrackMatch MatchTextAttachmentToTrack(
        ContainerAttachmentMetadata attachment,
        AttachmentTextHeuristicProfile profile,
        IReadOnlyList<ContainerTrackMetadata> existingVideoTracks)
    {
        if (!profile.HasUsableSignals)
        {
            return new AttachmentTrackMatch(attachment.Id, attachment.FileName, null, false);
        }

        var candidateTracks = existingVideoTracks
            .Where(track => MatchesAttachmentProfile(track, profile))
            .ToList();
        if (candidateTracks.Count != 1)
        {
            return new AttachmentTrackMatch(attachment.Id, attachment.FileName, null, false);
        }

        var matchedTrack = candidateTracks[0];
        var strongMatch = profile.HasCodecAndResolution
            || (profile.LanguageCode is not null && (profile.CodecLabel is not null || profile.ResolutionLabel is not null))
            || (profile.HasExplicitLanguageMarker
                && existingVideoTracks.Count(track => string.Equals(
                    MediaLanguageHelper.NormalizeMuxLanguageCode(track.Language),
                    profile.LanguageCode,
                    StringComparison.OrdinalIgnoreCase)) == 1);

        return new AttachmentTrackMatch(
            attachment.Id,
            attachment.FileName,
            strongMatch ? matchedTrack.TrackId : null,
            strongMatch);
    }

    private static bool MatchesAttachmentProfile(ContainerTrackMetadata track, AttachmentTextHeuristicProfile profile)
    {
        if (profile.LanguageCode is not null
            && !string.Equals(
                MediaLanguageHelper.NormalizeMuxLanguageCode(track.Language),
                profile.LanguageCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (profile.CodecLabel is not null
            && !string.Equals(track.CodecLabel, profile.CodecLabel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (profile.ResolutionLabel is not null
            && !string.Equals(
                ResolutionLabel.FromWidth(track.VideoWidth).Value,
                profile.ResolutionLabel,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static AttachmentTextHeuristicProfile BuildAttachmentTextHeuristicProfile(
        string fileName,
        CompanionTextDetails details)
    {
        var languageSignals = string.Join(
            " ",
            new[] { fileName, details.Title, details.Topic, details.Sender }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        var hasExplicitLanguageMarker = TryInferExplicitLanguageCode(languageSignals, out var languageCode);
        if (languageCode is null && (!string.IsNullOrWhiteSpace(details.Title) || !string.IsNullOrWhiteSpace(fileName)))
        {
            languageCode = "de";
        }

        var codecLabel = TryInferCodecLabel(details.MediaUrl);
        var resolutionLabel = TryInferResolutionLabel(details.MediaUrl);

        return new AttachmentTextHeuristicProfile(
            LanguageCode: languageCode,
            HasExplicitLanguageMarker: hasExplicitLanguageMarker,
            CodecLabel: codecLabel,
            ResolutionLabel: resolutionLabel);
    }

    private static bool TryInferExplicitLanguageCode(string? value, out string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            languageCode = null;
            return false;
        }

        if (ContainsAny(value, "op platt", "plattd", "plattdu", "plattdü"))
        {
            languageCode = "nds";
            return true;
        }

        if (ContainsAny(value, "englisch", "english", "originalversion"))
        {
            languageCode = "en";
            return true;
        }

        if (ContainsAny(value, "deutsch", "german"))
        {
            languageCode = "de";
            return true;
        }

        languageCode = null;
        return false;
    }

    private static string? TryInferCodecLabel(string? mediaUrl)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl))
        {
            return null;
        }

        if (ContainsAny(mediaUrl, "hevc", "h265", "h.265"))
        {
            return "H.265";
        }

        return mediaUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            || ContainsAny(mediaUrl, "h264", "h.264", "avc")
            ? "H.264"
            : null;
    }

    private static string? TryInferResolutionLabel(string? mediaUrl)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl))
        {
            return null;
        }

        if (ContainsAny(mediaUrl, "2160", "uhd", "4k"))
        {
            return "UHD";
        }

        if (ContainsAny(mediaUrl, "1080"))
        {
            return "FHD";
        }

        if (ContainsAny(mediaUrl, "720") || ContainsAny(mediaUrl, "hd.mp4", "_hd.", "-hd.", "/hd."))
        {
            return "HD";
        }

        if (ContainsAny(mediaUrl, "540", "sd.mp4", "_sd.", "-sd.", "/sd."))
        {
            return "SD";
        }

        return null;
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<CompanionTextDetails> TryReadEmbeddedTextAttachmentDetailsAsync(
        string mkvMergePath,
        string containerPath,
        ContainerAttachmentMetadata attachment,
        CancellationToken cancellationToken)
    {
        var probeSidecarContent = TryReadAttachmentTextFromProbeSidecar(containerPath, attachment);
        if (!string.IsNullOrWhiteSpace(probeSidecarContent))
        {
            return CompanionTextMetadataReader.ReadDetailedFromContent(probeSidecarContent);
        }

        var mkvExtractPath = ResolveMkvExtractPath(mkvMergePath);
        if (!File.Exists(mkvExtractPath))
        {
            return CompanionTextDetails.Empty;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-embedded-attachments", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var tempFilePath = Path.Combine(
            tempDirectory,
            $"{attachment.Id}{Path.GetExtension(attachment.FileName)}");

        try
        {
            var exitCode = await RunMkvExtractAttachmentAsync(
                mkvExtractPath,
                containerPath,
                attachment.Id,
                tempFilePath,
                cancellationToken);
            if (exitCode != 0 || !File.Exists(tempFilePath))
            {
                return CompanionTextDetails.Empty;
            }

            return CompanionTextMetadataReader.ReadDetailed(tempFilePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return CompanionTextDetails.Empty;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
            catch
            {
                // Die TXT-Heuristik ist nur ein optionaler Präzisionsgewinn.
                // Ein liegen gebliebener Temp-Ordner darf die eigentliche Planung nicht scheitern lassen.
            }
        }
    }

    private static string? TryReadAttachmentTextFromProbeSidecar(
        string containerPath,
        ContainerAttachmentMetadata attachment)
    {
        var probeSidecarPath = containerPath + ".mkvmerge.json";
        if (!File.Exists(probeSidecarPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(probeSidecarPath));
            if (!document.RootElement.TryGetProperty("attachments", out var attachmentsElement)
                || attachmentsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var fallbackAttachmentId = 0;
            foreach (var attachmentElement in attachmentsElement.EnumerateArray())
            {
                var candidateId = attachmentElement.TryGetProperty("id", out var idElement)
                    && idElement.TryGetInt32(out var parsedAttachmentId)
                        ? parsedAttachmentId
                        : fallbackAttachmentId;
                var candidateFileName = attachmentElement.TryGetProperty("file_name", out var fileNameElement)
                    ? fileNameElement.GetString()
                    : null;
                if ((candidateId == attachment.Id
                        || string.Equals(candidateFileName, attachment.FileName, StringComparison.OrdinalIgnoreCase))
                    && attachmentElement.TryGetProperty("text_content", out var textContentElement)
                    && textContentElement.ValueKind == JsonValueKind.String)
                {
                    return textContentElement.GetString();
                }

                fallbackAttachmentId++;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string ResolveMkvExtractPath(string mkvMergePath)
    {
        var toolDirectory = Path.GetDirectoryName(mkvMergePath);
        return string.IsNullOrWhiteSpace(toolDirectory)
            ? "mkvextract.exe"
            : Path.Combine(toolDirectory, "mkvextract.exe");
    }

    private static async Task<int> RunMkvExtractAttachmentAsync(
        string mkvExtractPath,
        string containerPath,
        int attachmentId,
        string outputPath,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = mkvExtractPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("attachments");
        process.StartInfo.ArgumentList.Add(containerPath);
        process.StartInfo.ArgumentList.Add($"{attachmentId}:{outputPath}");

        process.Start();
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort für Cancel. Die eigentliche Abbruchsemantik kommt über das geworfene Token.
            }
        });

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private static bool IsTextAttachment(string fileName)
    {
        return fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
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

    private static string BuildSubtitleReuseCoverageKey(SubtitleFile subtitle)
    {
        return BuildSubtitleReuseCoverageKey(subtitle.Kind, subtitle.LanguageCode);
    }

    private static string BuildSubtitleReuseCoverageKey(SubtitleKind kind, string? languageCode)
    {
        return $"{kind.DisplayName}|{MediaLanguageHelper.NormalizeMuxLanguageCode(languageCode)}";
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

    private static string ResolveWorkingDirectory(SeriesEpisodeMuxRequest request, string outputPath)
    {
        var preferredSourcePath = !string.IsNullOrWhiteSpace(request.MainVideoPath)
            ? request.MainVideoPath
            : request.AudioDescriptionPath;
        var workingDirectory = string.IsNullOrWhiteSpace(preferredSourcePath)
            ? null
            : Path.GetDirectoryName(preferredSourcePath);

        return string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetDirectoryName(outputPath)
                ?? throw new DirectoryNotFoundException($"Arbeitsverzeichnis konnte nicht bestimmt werden: {outputPath}")
            : workingDirectory;
    }

    private sealed record ExistingArchiveState(
        ContainerMetadata Container,
        IReadOnlyList<ContainerTrackMetadata> VideoTracks,
        IReadOnlyList<ContainerTrackMetadata> AudioTracks,
        IReadOnlyList<ContainerTrackMetadata> SubtitleTracks);

    private sealed record PreparedVideoSource(
        string FilePath,
        MediaTrackMetadata Metadata,
        long FileSizeBytes);

    private sealed record FinalVideoSelectionPlan(
        IReadOnlyList<VideoTrackSelection> VideoSelections,
        IReadOnlyList<ContainerTrackMetadata> RetainedExistingTracks,
        IReadOnlyList<ContainerTrackMetadata> RemovedExistingTracks);

    private sealed record AttachmentReusePlan(
        bool IncludePrimarySourceAttachments,
        IReadOnlyList<int>? PrimarySourceAttachmentIds,
        string? AttachmentSourcePath,
        IReadOnlyList<int>? AttachmentSourceAttachmentIds,
        IReadOnlyList<string> PreservedAttachmentNames)
    {
        public static AttachmentReusePlan None { get; } = new(
            IncludePrimarySourceAttachments: false,
            PrimarySourceAttachmentIds: null,
            AttachmentSourcePath: null,
            AttachmentSourceAttachmentIds: null,
            PreservedAttachmentNames: []);
    }

    private sealed record AttachmentTextHeuristicProfile(
        string? LanguageCode,
        bool HasExplicitLanguageMarker,
        string? CodecLabel,
        string? ResolutionLabel)
    {
        public bool HasCodecAndResolution => CodecLabel is not null && ResolutionLabel is not null;

        public bool HasUsableSignals => LanguageCode is not null || CodecLabel is not null || ResolutionLabel is not null;
    }

    private sealed record AttachmentTrackMatch(
        int AttachmentId,
        string FileName,
        int? MatchedTrackId,
        bool IsStrongMatch);

    private sealed record SubtitleReusePlan(
        IReadOnlyList<SubtitleFile> ExternalPlans,
        IReadOnlyList<SubtitleFile> EmbeddedPlans)
    {
        public IReadOnlyList<SubtitleFile> FinalPlans { get; } = ExternalPlans.Concat(EmbeddedPlans).ToList();
    }
}
