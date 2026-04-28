using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Bündelt die Anzeige- und Änderungsaufbereitung für Archivvergleiche, damit Planlogik und GUI-Texte nicht im selben Partial vermischt bleiben.
/// </summary>
public sealed partial class SeriesArchiveService
{
    private static IReadOnlyList<string> BuildTrackHeaderNormalizationOnlyNotes(
        ContainerTrackMetadata? bestExistingVideo,
        IReadOnlyList<TrackHeaderEditOperation> trackHeaderEdits,
        ContainerTitleEditOperation? containerTitleEdit)
    {
        var notes = new List<string>
        {
            $"Archiv-MKV bereits vorhanden. Die Datei bleibt inhaltlich unverändert; {BuildDirectHeaderNormalizationDescription(trackHeaderEdits.Count > 0, containerTitleEdit is not null)}.",
            bestExistingVideo is null
                ? "Die vorhandene Archivdatei liefert weiterhin alle Hauptspuren."
                : $"Vorhandene Videospur wird beibehalten: {bestExistingVideo.VideoWidth}px / {bestExistingVideo.CodecLabel}.",
            "Es wird weder eine Arbeitskopie erstellt noch ein kompletter Remux-Lauf ausgefuehrt."
        };

        notes.AddRange(BuildDirectHeaderChangeNotes(containerTitleEdit, trackHeaderEdits));
        return notes;
    }

    private static IReadOnlyList<string> BuildKeepExistingPrimaryNotes(
        ContainerTrackMetadata? bestExistingVideo,
        bool needsAudioDescription,
        bool needsSubtitleSupplement,
        bool needsAdditionalVideo,
        bool needsVideoCleanup,
        bool needsManualAttachments,
        bool hasTrackHeaderEdits,
        bool hasContainerTitleEdit,
        IReadOnlyList<SubtitleFile> suppressedExternalSubtitlePlans)
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
            && (hasTrackHeaderEdits || hasContainerTitleEdit))
        {
            notes.Add($"Alle Inhalte sind bereits vorhanden. {BuildDirectHeaderNormalizationDescription(hasTrackHeaderEdits, hasContainerTitleEdit, capitalizeFirstLetter: true)}.");
        }

        if (suppressedExternalSubtitlePlans.Count > 0)
        {
            notes.Add(BuildSuppressedExternalSubtitleNote(suppressedExternalSubtitlePlans));
        }

        return notes;
    }

    /// <summary>
    /// Formuliert unterdrückte externe Untertitel so, dass nur die wirklich schon belegten
    /// Typ-/Sprach-Slots genannt werden. So bleibt z. B. sichtbar, dass ein neues ASS trotz
    /// vorhandenen SRT übernommen wurde und nur das SRT selbst entfiel.
    /// </summary>
    private static string BuildSuppressedExternalSubtitleNote(IReadOnlyList<SubtitleFile> suppressedExternalSubtitlePlans)
    {
        var occupiedSlots = suppressedExternalSubtitlePlans
            .Select(subtitle => subtitle.TrackName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(slot => slot, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return occupiedSlots.Count == 1
            ? $"Nicht zusätzlich übernommen wurde ein externer Untertitel für den bereits belegten Slot {occupiedSlots[0]}."
            : $"Nicht zusätzlich übernommen wurden externe Untertitel für bereits belegte Slots: {string.Join(", ", occupiedSlots)}.";
    }

    private static ContainerTitleEditOperation? BuildRelevantContainerTitleEdit(string? currentTitle, string expectedTitle)
    {
        return ArchiveHeaderNormalizationService.BuildContainerTitleEdit(currentTitle, expectedTitle);
    }

    private static string BuildDirectHeaderNormalizationDescription(
        bool hasTrackHeaderEdits,
        bool hasContainerTitleEdit,
        bool capitalizeFirstLetter = false)
    {
        var text = (hasTrackHeaderEdits, hasContainerTitleEdit) switch
        {
            (true, true) => "es werden nur der MKV-Titel und die Benennungen der relevanten Spuren direkt im Matroska-Header vereinheitlicht",
            (true, false) => "es werden nur die Benennungen der relevanten Spuren direkt im Matroska-Header vereinheitlicht",
            (false, true) => "es wird nur der MKV-Titel direkt im Matroska-Header vereinheitlicht",
            _ => "es werden nur Header-Metadaten direkt im Matroska-Header vereinheitlicht"
        };

        if (!capitalizeFirstLetter || string.IsNullOrEmpty(text))
        {
            return text;
        }

        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static IReadOnlyList<string> BuildDirectHeaderChangeNotes(
        ContainerTitleEditOperation? containerTitleEdit,
        IReadOnlyList<TrackHeaderEditOperation> trackHeaderEdits)
    {
        return ArchiveHeaderNormalizationService.BuildHeaderChangeNotes(containerTitleEdit, trackHeaderEdits);
    }

    private static string FormatHeaderValue(string? value)
    {
        return ArchiveHeaderNormalizationService.FormatHeaderValue(value);
    }

    private static EpisodeUsageSummary BuildReuseOnlySkipUsageSummary(
        string outputPath,
        IReadOnlyList<ContainerTrackMetadata> retainedExistingVideoTracks,
        IReadOnlyList<ContainerTrackMetadata> retainedNormalAudioTracks,
        IReadOnlyList<ContainerTrackMetadata> existingAudioDescriptions,
        SubtitleReusePlan subtitlePlan,
        IReadOnlyList<string> preservedAttachmentNames)
    {
        var bestExistingVideo = retainedExistingVideoTracks.FirstOrDefault();
        var additionalVideoTracks = retainedExistingVideoTracks.Skip(1).ToList();

        return new EpisodeUsageSummary(
            ArchiveAction: "Zieldatei bereits aktuell",
            ArchiveDetails: "Alle benötigten Inhalte und relevanten Spurnamen sind bereits vorhanden. Kein erneutes Muxen nötig.",
            MainVideo: BuildExistingUsageEntry(
                [
                    bestExistingVideo is null
                        ? Path.GetFileName(outputPath)
                        : GetExistingVideoDisplayLabel(bestExistingVideo)
                ]),
            AdditionalVideos: BuildExistingUsageEntry(
                additionalVideoTracks.Select(GetExistingVideoDisplayLabel).ToList()),
            Audio: BuildExistingUsageEntry(
                retainedNormalAudioTracks.Select(BuildExpectedAudioTrackName).ToList()),
            AudioDescription: BuildExistingUsageEntry(
                BuildRetainedAudioDescriptionSources(
                        outputPath,
                        existingAudioDescriptions,
                        retainedNormalAudioTracks.FirstOrDefault()?.Language)
                    .Select(source => source.TrackName)
                    .ToList()),
            Subtitles: BuildExistingUsageEntry(
                subtitlePlan.EmbeddedPlans.Select(subtitle => subtitle.TrackName).ToList()),
            Attachments: BuildExistingUsageEntry(
                preservedAttachmentNames,
                noneText: "keine",
                separator: ", "));
    }

    private static EpisodeUsageEntry BuildExistingUsageEntry(
        IReadOnlyList<string> existingItems,
        string noneText = "(keine)",
        string? separator = null)
    {
        if (existingItems.Count == 0)
        {
            return new EpisodeUsageEntry(noneText, null, null);
        }

        var items = existingItems
            .Select(item => new EpisodeUsageItem(BuildExistingTargetDisplayText(item), EpisodeUsageItemKind.Existing))
            .ToList();
        return new EpisodeUsageEntry(
            string.Join(separator ?? Environment.NewLine, items.Select(item => item.Text)),
            null,
            null,
            items);
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
        IReadOnlyList<ContainerTrackMetadata> existingAudioDescriptions,
        IReadOnlyList<ContainerTrackMetadata> existingSubtitleTracks,
        IReadOnlyList<SubtitleFile> embeddedSubtitlePlans,
        string? originalLanguage,
        string? seriesContext)
    {
        return ArchiveHeaderNormalizationService.BuildTrackHeaderEdits(
            allExistingTracks,
            retainedExistingVideoTracks,
            retainedNormalAudioTracks,
            existingAudioDescriptions,
            existingSubtitleTracks,
            embeddedSubtitlePlans,
            originalLanguage,
            seriesContext);
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
        return ArchiveHeaderNormalizationService.BuildExpectedVideoTrackName(track);
    }

    private static string BuildExpectedAudioTrackName(ContainerTrackMetadata track)
    {
        return ArchiveHeaderNormalizationService.BuildExpectedAudioTrackName(track);
    }

    private static string BuildExpectedAudioDescriptionTrackName(ContainerTrackMetadata track, string? fallbackLanguage)
    {
        return ArchiveHeaderNormalizationService.BuildExpectedAudioDescriptionTrackName(track, fallbackLanguage);
    }

    private static string? ResolveAudioDescriptionLanguage(ContainerTrackMetadata track, string? fallbackLanguage)
    {
        return string.IsNullOrWhiteSpace(track.Language)
            ? fallbackLanguage
            : track.Language;
    }

    private static IReadOnlyList<AudioDescriptionSourcePlan> BuildRetainedAudioDescriptionSources(
        string outputPath,
        IReadOnlyList<ContainerTrackMetadata> existingAudioDescriptions,
        string? fallbackLanguage)
    {
        return existingAudioDescriptions
            .Select(track => new AudioDescriptionSourcePlan(
                outputPath,
                track.TrackId,
                BuildExpectedAudioDescriptionTrackName(track, fallbackLanguage),
                MediaLanguageHelper.NormalizeMuxLanguageCode(
                    string.IsNullOrWhiteSpace(track.Language)
                        ? fallbackLanguage
                        : track.Language)))
            .ToList();
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
        var label = BuildEmbeddedSubtitleLabel(track, kind);
        return $"{Path.GetFileName(outputPath)} -> {label}";
    }

}
