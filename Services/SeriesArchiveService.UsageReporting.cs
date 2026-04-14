using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Bündelt die Anzeige- und Änderungsaufbereitung für Archivvergleiche, damit Planlogik und GUI-Texte nicht im selben Partial vermischt bleiben.
/// </summary>
public sealed partial class SeriesArchiveService
{
    private static IReadOnlyList<string> BuildTrackHeaderNormalizationOnlyNotes(ContainerTrackMetadata? bestExistingVideo)
    {
        return
        [
            "Archiv-MKV bereits vorhanden. Die Datei bleibt inhaltlich unverändert; nur die Benennungen der relevanten Spuren werden direkt im Matroska-Header vereinheitlicht.",
            bestExistingVideo is null
                ? "Die vorhandene Archivdatei liefert weiterhin alle Hauptspuren."
                : $"Vorhandene Videospur wird beibehalten: {bestExistingVideo.VideoWidth}px / {bestExistingVideo.CodecLabel}.",
            "Es wird weder eine Arbeitskopie erstellt noch ein kompletter Remux-Lauf ausgefuehrt."
        ];
    }

    private static IReadOnlyList<string> BuildKeepExistingPrimaryNotes(
        ContainerTrackMetadata? bestExistingVideo,
        bool needsAudioDescription,
        bool needsSubtitleSupplement,
        bool needsAdditionalVideo,
        bool needsVideoCleanup,
        bool needsManualAttachments,
        bool requiresRelevantTrackNameNormalization,
        bool hasSuppressedExternalSubtitles)
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
            notes.Add("Alle Inhalte sind bereits vorhanden. Es werden nur die Benennungen der relevanten Spuren vereinheitlicht.");
        }

        if (hasSuppressedExternalSubtitles)
        {
            notes.Add("Ausgewählte externe Untertitel wurden nicht zusätzlich übernommen, weil die Zieldatei bereits Untertitel desselben Typs und derselben Sprache enthält.");
        }

        return notes;
    }

    private static EpisodeUsageSummary BuildReuseOnlySkipUsageSummary(
        string outputPath,
        IReadOnlyList<ContainerTrackMetadata> retainedExistingVideoTracks,
        IReadOnlyList<ContainerTrackMetadata> retainedNormalAudioTracks,
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
                retainedNormalAudioTracks.Count == 0
                    ? "(keine)"
                    : string.Join(
                        Environment.NewLine,
                        retainedNormalAudioTracks.Select(track => BuildExistingTargetDisplayText(BuildExpectedAudioTrackName(track)))),
                null,
                null),
            AudioDescription: new EpisodeUsageEntry(
                existingAudioDescription is null
                    ? "(keine)"
                    : BuildExistingTargetDisplayText(
                        BuildExpectedAudioDescriptionTrackName(existingAudioDescription, retainedNormalAudioTracks.FirstOrDefault()?.Language)),
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

    /// <summary>
    /// Baut die konkreten <c>mkvpropedit</c>-Operationen für bereits vorhandene Tracks auf.
    /// Nur solche Spuren werden betrachtet, die die aktuelle Planung tatsächlich weiterverwendet.
    /// Dadurch bleibt der direkte Header-Edit-Pfad fachlich deckungsgleich mit dem Vergleich, der
    /// zuvor auch den reinen "Tracknamen-Normalisierung"-Sonderfall erkannt hat.
    /// </summary>
    private static IReadOnlyList<TrackHeaderEditOperation> BuildRelevantTrackHeaderEdits(
        IReadOnlyList<ContainerTrackMetadata> allExistingTracks,
        IReadOnlyList<ContainerTrackMetadata> retainedExistingVideoTracks,
        IReadOnlyList<ContainerTrackMetadata> retainedNormalAudioTracks,
        ContainerTrackMetadata? existingAudioDescription,
        IReadOnlyList<ContainerTrackMetadata> existingSubtitleTracks,
        IReadOnlyList<SubtitleFile> embeddedSubtitlePlans)
    {
        var selectorByTrackId = allExistingTracks
            .Select((track, index) => new KeyValuePair<int, string>(track.TrackId, $"track:{index + 1}"))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        var operations = new List<TrackHeaderEditOperation>();

        foreach (var existingVideoTrack in retainedExistingVideoTracks)
        {
            TryAppendTrackHeaderEdit(
                operations,
                selectorByTrackId,
                existingVideoTrack,
                BuildExpectedVideoTrackName(existingVideoTrack),
                $"Video {existingVideoTrack.TrackId}");
        }

        foreach (var retainedNormalAudioTrack in retainedNormalAudioTracks)
        {
            TryAppendTrackHeaderEdit(
                operations,
                selectorByTrackId,
                retainedNormalAudioTrack,
                BuildExpectedAudioTrackName(retainedNormalAudioTrack),
                $"Audio {retainedNormalAudioTrack.TrackId}");
        }

        if (existingAudioDescription is not null)
        {
            TryAppendTrackHeaderEdit(
                operations,
                selectorByTrackId,
                existingAudioDescription,
                BuildExpectedAudioDescriptionTrackName(existingAudioDescription, retainedNormalAudioTracks.FirstOrDefault()?.Language),
                $"Audiodeskription {existingAudioDescription.TrackId}");
        }

        foreach (var subtitlePlan in embeddedSubtitlePlans)
        {
            if (subtitlePlan.EmbeddedTrackId is not int embeddedTrackId)
            {
                continue;
            }

            var existingTrack = existingSubtitleTracks.FirstOrDefault(track => track.TrackId == embeddedTrackId);
            if (existingTrack is null)
            {
                continue;
            }

            TryAppendTrackHeaderEdit(
                operations,
                selectorByTrackId,
                existingTrack,
                subtitlePlan.TrackName,
                $"Untertitel {embeddedTrackId}");
        }

        return operations;
    }

    private static bool HasConsistentTrackName(string? currentTrackName, string expectedTrackName)
    {
        return string.Equals(
            (currentTrackName ?? string.Empty).Trim(),
            expectedTrackName.Trim(),
            StringComparison.Ordinal);
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

    private static ArchiveUsageChange? BuildRemovedAudioChange(
        string outputPath,
        IReadOnlyList<ContainerTrackMetadata> removedAudioTracks)
    {
        if (removedAudioTracks.Count == 0)
        {
            return null;
        }

        return new ArchiveUsageChange(
            string.Join(Environment.NewLine, removedAudioTracks.Select(track => BuildAudioTrackLabel(outputPath, track))),
            "Vorhandene Tonspuren entfallen, weil für diese Sprache bereits frische Tonspuren aus den ausgewählten Quellen übernommen werden.");
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

    private static string BuildEmbeddedSubtitleLabel(ContainerTrackMetadata track, SubtitleKind kind)
    {
        if (!string.IsNullOrWhiteSpace(track.TrackName))
        {
            return track.TrackName;
        }

        return $"Archiv-Untertitel {kind.DisplayName}";
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

    private static void TryAppendTrackHeaderEdit(
        ICollection<TrackHeaderEditOperation> operations,
        IReadOnlyDictionary<int, string> selectorByTrackId,
        ContainerTrackMetadata track,
        string expectedTrackName,
        string fallbackDisplayLabel)
    {
        if (HasConsistentTrackName(track.TrackName, expectedTrackName)
            || !selectorByTrackId.TryGetValue(track.TrackId, out var selector))
        {
            return;
        }

        var displayLabel = string.IsNullOrWhiteSpace(track.TrackName)
            ? fallbackDisplayLabel
            : track.TrackName;
        operations.Add(new TrackHeaderEditOperation(
            selector,
            displayLabel,
            track.TrackName,
            expectedTrackName));
    }
}
