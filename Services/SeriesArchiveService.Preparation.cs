using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Kapselt die zentrale Archivvorbereitung: bestehende Zielcontainer lesen, frische Quellen gegen Archivspuren abgleichen
/// und daraus die konkrete Integrationsentscheidung für den späteren Mux-Lauf ableiten.
/// </summary>
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
                plannedVideos,
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
                plannedVideos,
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
                plannedVideos,
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
            var metadata = await _probeService.ReadPrimaryVideoMetadataAsync(mkvMergePath, videoPath, cancellationToken);
            plannedVideos.Add(new PreparedVideoSource(
                videoPath,
                ApplySourceLanguageHints(metadata, videoPath),
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

        var (selectedVideos, retainedExistingTrackIds) = SelectVideoTracksFromSlots(outputPath, freshBySlot, existingBySlot);

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

    private static (IReadOnlyList<VideoTrackSelection> SelectedVideos, IReadOnlyCollection<int> RetainedExistingTrackIds) SelectVideoTracksFromSlots(
        string outputPath,
        IReadOnlyDictionary<string, PreparedVideoSource> freshBySlot,
        IReadOnlyDictionary<string, ContainerTrackMetadata> existingBySlot)
    {
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

        return (selectedVideos, retainedExistingTrackIds);
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
        var requestedExternalSubtitlePlans = requestSubtitlePaths
            .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => SubtitleFile.CreateDetectedExternal(path, SubtitleKind.FromExtension(Path.GetExtension(path))))
            .ToList();
        var externalSubtitlePlans = requestedExternalSubtitlePlans
            .Where(subtitle => !embeddedCoverage.Contains(BuildSubtitleReuseCoverageKey(subtitle)))
            .ToList();
        // Aus Benutzersicht darf eine ausgewählte ASS/SRT-Datei nicht "verschwinden".
        // Ist derselbe Typ+Sprache in der Ziel-MKV schon vorhanden, bleibt der externe Track
        // fachlich korrekt unterdrückt, wird aber als Hinweis am Plan dokumentiert.
        var suppressedExternalSubtitlePlans = requestedExternalSubtitlePlans
            .Where(subtitle => embeddedCoverage.Contains(BuildSubtitleReuseCoverageKey(subtitle)))
            .ToList();

        return new SubtitleReusePlan(externalSubtitlePlans, embeddedSubtitlePlans, suppressedExternalSubtitlePlans);
    }

    private static ContainerTrackMetadata? FindExistingAudioDescription(IReadOnlyList<ContainerTrackMetadata> existingAudioTracks)
    {
        return existingAudioTracks.FirstOrDefault(AudioTrackClassifier.IsAudioDescriptionTrack);
    }

    private ArchiveIntegrationDecision BuildDecisionUsingExistingPrimary(
        string outputPath,
        SeriesEpisodeMuxRequest request,
        IReadOnlyList<PreparedVideoSource> plannedVideos,
        FinalVideoSelectionPlan videoPlan,
        SubtitleReusePlan subtitlePlan,
        IReadOnlyList<ContainerTrackMetadata> replacedSubtitleTracks,
        ExistingArchiveState existingArchive,
        ContainerTrackMetadata? bestExistingVideo,
        ContainerTrackMetadata? existingAudioDescription,
        FileCopyPlan workingCopyPlan,
        AttachmentReusePlan attachmentReusePlan)
    {
        var retainedNormalAudioTracks = SelectRetainedExistingNormalAudioTracksForSelectedFreshVideos(
            outputPath,
            videoPlan.VideoSelections,
            plannedVideos,
            existingArchive.AudioTracks);
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
        var relevantTrackHeaderEdits = BuildRelevantTrackHeaderEdits(
            existingArchive.Container.Tracks,
            videoPlan.RetainedExistingTracks,
            retainedNormalAudioTracks,
            existingAudioDescription,
            existingArchive.SubtitleTracks,
            subtitlePlan.EmbeddedPlans);
        var requiresRelevantTrackNameNormalization = relevantTrackHeaderEdits.Count > 0;

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
                    retainedNormalAudioTracks,
                    existingAudioDescription,
                    subtitlePlan,
                    attachmentReusePlan.PreservedAttachmentNames),
                [
                    "Zieldatei bereits vollständig. Alle relevanten Spurnamen sind bereits konsistent. Kein erneutes Muxen nötig.",
                    .. BuildSubtitleSuppressionNotes(subtitlePlan)
                ]);
        }

        if (!needsAudioDescription
            && !needsSubtitleSupplement
            && !needsAdditionalVideo
            && !needsVideoCleanup
            && !needsManualAttachments
            && requiresRelevantTrackNameNormalization)
        {
            return new ArchiveIntegrationDecision(
                OutputFilePath: outputPath,
                SkipMux: false,
                SkipReason: null,
                SkipUsageSummary: null,
                WorkingCopy: null,
                PrimarySourcePath: outputPath,
                VideoSelections: videoPlan.VideoSelections,
                RetainedAudioTrackIds: retainedNormalAudioTracks.Select(track => track.TrackId).ToList(),
                PrimarySubtitleTrackIds: subtitlePlan.FinalPlans.Count > 0 || replacedSubtitleTracks.Count > 0 ? [] : null,
                PrimarySourceAttachmentIds: attachmentReusePlan.PrimarySourceAttachmentIds,
                IncludePrimaryAttachments: attachmentReusePlan.IncludePrimarySourceAttachments,
                AttachmentSourcePath: attachmentReusePlan.AttachmentSourcePath,
                AttachmentSourceAttachmentIds: attachmentReusePlan.AttachmentSourceAttachmentIds,
                AdditionalVideoPaths: [],
                AudioDescriptionFilePath: existingAudioDescription is null ? null : outputPath,
                AudioDescriptionTrackId: existingAudioDescription?.TrackId,
                SubtitleFiles: subtitlePlan.FinalPlans,
                AttachmentFilePaths: [],
                FallbackToRequestAttachments: false,
                PreservedAttachmentNames: attachmentReusePlan.PreservedAttachmentNames,
                UsageComparison: ArchiveUsageComparison.Empty,
                TrackHeaderEdits: relevantTrackHeaderEdits,
                Notes: BuildTrackHeaderNormalizationOnlyNotes(bestExistingVideo)
                    .Concat(BuildSubtitleSuppressionNotes(subtitlePlan))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());
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
            RetainedAudioTrackIds: retainedNormalAudioTracks.Select(track => track.TrackId).ToList(),
            PrimarySubtitleTrackIds: subtitlePlan.FinalPlans.Count > 0 || replacedSubtitleTracks.Count > 0 ? [] : null,
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
            TrackHeaderEdits: [],
            SkipUsageSummary: null,
            Notes: BuildKeepExistingPrimaryNotes(
                bestExistingVideo,
                needsAudioDescription,
                needsSubtitleSupplement,
                needsAdditionalVideo,
                needsVideoCleanup,
                needsManualAttachments,
                requiresRelevantTrackNameNormalization,
                subtitlePlan.SuppressedExternalPlans));
    }

    private static ArchiveIntegrationDecision BuildDecisionReplacingExistingPrimary(
        string outputPath,
        SeriesEpisodeMuxRequest request,
        FinalVideoSelectionPlan videoPlan,
        IReadOnlyList<PreparedVideoSource> plannedVideos,
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
        var freshVideoPaths = videoPlan.VideoSelections
            .Where(selection => !string.Equals(selection.FilePath, outputPath, StringComparison.OrdinalIgnoreCase))
            .Select(selection => selection.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var attachmentFilePaths = BuildAttachmentPathsForUsedVideos(freshVideoPaths)
            .Concat(manualAttachmentPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var retainedNormalAudioTracks = SelectRetainedExistingNormalAudioTracksForSelectedFreshVideos(
            outputPath,
            videoPlan.VideoSelections,
            plannedVideos,
            existingArchive.AudioTracks);
        var removedNormalAudioTracks = GetRetainedNormalAudioTracks(existingArchive.AudioTracks)
            .Where(track => !retainedNormalAudioTracks.Any(retained => retained.TrackId == track.TrackId))
            .ToList();
        var needsExistingCopy = videoPlan.RetainedExistingTracks.Count > 0
            || retainedNormalAudioTracks.Count > 0
            || (existingAudioDescription is not null && string.IsNullOrWhiteSpace(request.AudioDescriptionPath))
            || subtitlePlan.EmbeddedPlans.Count > 0
            || attachmentReusePlan.PreservedAttachmentNames.Count > 0;
        var removedAdditionalVideoTracks = videoPlan.RemovedExistingTracks
            .Where(track => bestExistingVideo is null || track.TrackId != bestExistingVideo.TrackId)
            .ToList();
        var usageComparison = new ArchiveUsageComparison(
            MainVideo: bestExistingVideo is null
                ? null
                : new ArchiveUsageChange(
                    BuildVideoTrackLabel(outputPath, bestExistingVideo),
                    BuildPrimaryVideoReplacementReason(bestExistingVideo, newPrimaryVideo)),
            AdditionalVideos: BuildRemovedAdditionalVideoChange(outputPath, removedAdditionalVideoTracks),
            Audio: BuildRemovedAudioChange(outputPath, removedNormalAudioTracks),
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
            RetainedAudioTrackIds: retainedNormalAudioTracks.Select(track => track.TrackId).ToList(),
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
            // Automatisch erkannte TXT-Dateien dürfen nur den frischen Videospuren folgen,
            // die der Archivabgleich tatsächlich in die finale Videospurauswahl übernommen hat.
            AttachmentFilePaths: attachmentFilePaths,
            FallbackToRequestAttachments: false,
            PreservedAttachmentNames: attachmentReusePlan.PreservedAttachmentNames,
            UsageComparison: usageComparison,
            TrackHeaderEdits: [],
            Notes:
            [
                "Archiv-MKV bereits vorhanden. Die neue Quelle ersetzt die Hauptspuren.",
                bestExistingVideo is null
                    ? $"Neue Hauptquelle wird verwendet: {Path.GetFileName(videoPlan.VideoSelections[0].FilePath)}."
                    : BuildPrimaryVideoReplacementReason(bestExistingVideo, newPrimaryVideo),
                .. BuildSubtitleSuppressionNotes(subtitlePlan)
            ]);
    }

    private static IReadOnlyList<string> BuildSubtitleSuppressionNotes(SubtitleReusePlan subtitlePlan)
    {
        return subtitlePlan.SuppressedExternalPlans.Count == 0
            ? []
            : [BuildSuppressedExternalSubtitleNote(subtitlePlan.SuppressedExternalPlans)];
    }

    private static IReadOnlyList<ContainerTrackMetadata> GetRetainedNormalAudioTracks(
        IReadOnlyList<ContainerTrackMetadata> existingAudioTracks)
    {
        return AudioTrackClassifier.GetNormalAudioTracks(existingAudioTracks)
            .OrderBy(track => MediaLanguageHelper.GetLanguageSortRank(track.Language))
            .ThenBy(track => track.IsDefaultTrack ? 0 : 1)
            .ThenBy(track => track.TrackId)
            .ToList();
    }

    private static IReadOnlyList<ContainerTrackMetadata> SelectRetainedExistingNormalAudioTracksForSelectedFreshVideos(
        string outputPath,
        IReadOnlyList<VideoTrackSelection> videoSelections,
        IReadOnlyList<PreparedVideoSource> plannedVideos,
        IReadOnlyList<ContainerTrackMetadata> existingAudioTracks)
    {
        var retainedNormalAudioTracks = GetRetainedNormalAudioTracks(existingAudioTracks);
        var selectedFreshVideoPaths = videoSelections
            .Where(selection => !string.Equals(selection.FilePath, outputPath, StringComparison.OrdinalIgnoreCase))
            .Select(selection => selection.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var freshAudioLanguages = plannedVideos
            .Where(video => selectedFreshVideoPaths.Contains(video.FilePath))
            .Select(video => MediaLanguageHelper.NormalizeMuxLanguageCode(video.Metadata.AudioLanguage))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return retainedNormalAudioTracks
            .Where(track => !freshAudioLanguages.Contains(MediaLanguageHelper.NormalizeMuxLanguageCode(track.Language)))
            .ToList();
    }

    private static MediaTrackMetadata ApplySourceLanguageHints(MediaTrackMetadata metadata, string filePath)
    {
        var textMetadata = CompanionTextMetadataReader.ReadForMediaFile(filePath);
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

        // Der Archivabgleich muss dieselbe Mediathek-Sprachkorrektur sehen wie die
        // Dateierkennung. Sonst landen Quellen trotz korrekter Anzeige in falschen
        // Sprachslots und ersetzen oder duplizieren Archivspuren.
        return metadata with
        {
            VideoLanguage = videoLanguage,
            AudioLanguage = audioLanguage
        };
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

    private static string BuildSubtitleReuseCoverageKey(SubtitleFile subtitle)
    {
        return BuildSubtitleReuseCoverageKey(subtitle.Kind, subtitle.LanguageCode);
    }

    private static string BuildSubtitleReuseCoverageKey(SubtitleKind kind, string? languageCode)
    {
        return $"{kind.DisplayName}|{MediaLanguageHelper.NormalizeMuxLanguageCode(languageCode)}";
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

    private sealed record SubtitleReusePlan(
        IReadOnlyList<SubtitleFile> ExternalPlans,
        IReadOnlyList<SubtitleFile> EmbeddedPlans,
        IReadOnlyList<SubtitleFile> SuppressedExternalPlans)
    {
        public IReadOnlyList<SubtitleFile> FinalPlans { get; } = ExternalPlans.Concat(EmbeddedPlans).ToList();
    }
}
