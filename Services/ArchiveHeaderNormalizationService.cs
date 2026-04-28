using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Zentrale Regelbasis für Matroska-Header, die sowohl der Mux-Archivvergleich als auch
/// die Archivpflege verwenden. Der Service plant ausschließlich Zielwerte und führt keine
/// Änderungen an Dateien aus.
/// </summary>
internal static class ArchiveHeaderNormalizationService
{
    /// <summary>
    /// Plant die Container-Titelkorrektur für eine vorhandene MKV, sofern aktueller und erwarteter Titel abweichen.
    /// </summary>
    public static ContainerTitleEditOperation? BuildContainerTitleEdit(string? currentTitle, string expectedTitle)
    {
        var normalizedCurrentTitle = (currentTitle ?? string.Empty).Trim();
        var normalizedExpectedTitle = expectedTitle.Trim();
        return string.Equals(normalizedCurrentTitle, normalizedExpectedTitle, StringComparison.Ordinal)
            ? null
            : new ContainerTitleEditOperation(normalizedCurrentTitle, normalizedExpectedTitle);
    }

    /// <summary>
    /// Baut Header-Operationen für eine reine Archivdateiprüfung. Dabei werden alle vorhandenen
    /// fachlich relevanten Spuren geprüft, aber keine fehlenden AD- oder Untertitelspuren gefordert.
    /// </summary>
    public static ArchiveHeaderNormalizationResult BuildForArchiveFile(
        string filePath,
        ContainerMetadata container,
        string expectedTitle,
        string? originalLanguage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(container);

        var videoTracks = container.Tracks
            .Where(track => string.Equals(track.Type, "video", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var audioTracks = container.Tracks
            .Where(track => string.Equals(track.Type, "audio", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var subtitleTracks = container.Tracks
            .Where(track => string.Equals(track.Type, "subtitles", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var audioDescriptionTracks = audioTracks
            .Where(AudioTrackClassifier.IsAudioDescriptionTrack)
            .OrderBy(track => MediaLanguageHelper.GetLanguageSortRank(track.Language))
            .ThenBy(track => track.TrackId)
            .ToList();
        var normalAudioTracks = AudioTrackClassifier.GetNormalAudioTracks(audioTracks)
            .OrderBy(track => MediaLanguageHelper.GetLanguageSortRank(track.Language))
            .ThenBy(track => track.IsDefaultTrack ? 0 : 1)
            .ThenBy(track => track.TrackId)
            .ToList();
        var embeddedSubtitlePlans = subtitleTracks
            .Select(track =>
            {
                var kind = SubtitleKind.FromExistingCodec(track.CodecLabel);
                return SubtitleFile.CreateEmbedded(
                        filePath,
                        kind,
                        track.TrackId,
                        BuildEmbeddedSubtitleLabel(track, kind),
                        track.Language)
                    with
                    {
                        Accessibility = IsHearingImpairedSubtitleTrack(track)
                            ? SubtitleAccessibility.HearingImpaired
                            : SubtitleAccessibility.Standard,
                        IsForced = track.IsForcedTrack
                    };
            })
            .OrderBy(subtitle => subtitle.Kind.SortRank)
            .ThenBy(subtitle => subtitle.EmbeddedTrackId)
            .ToList();

        return new ArchiveHeaderNormalizationResult(
            BuildContainerTitleEdit(container.Title, expectedTitle),
            BuildTrackHeaderEdits(
                container.Tracks,
                videoTracks,
                normalAudioTracks,
                audioDescriptionTracks,
                subtitleTracks,
                embeddedSubtitlePlans,
                originalLanguage,
                filePath));
    }

    /// <summary>
    /// Baut die konkreten <c>mkvpropedit</c>-Operationen für vorhandene Tracks auf.
    /// Der Aufrufer bestimmt, welche Tracks fachlich relevant sind.
    /// </summary>
    public static IReadOnlyList<TrackHeaderEditOperation> BuildTrackHeaderEdits(
        IReadOnlyList<ContainerTrackMetadata> allExistingTracks,
        IReadOnlyList<ContainerTrackMetadata> retainedExistingVideoTracks,
        IReadOnlyList<ContainerTrackMetadata> retainedNormalAudioTracks,
        IReadOnlyList<ContainerTrackMetadata> existingAudioDescriptions,
        IReadOnlyList<ContainerTrackMetadata> existingSubtitleTracks,
        IReadOnlyList<SubtitleFile> embeddedSubtitlePlans,
        string? originalLanguage,
        string? seriesContext = null)
    {
        var selectorByTrackId = allExistingTracks
            .Select((track, index) => new KeyValuePair<int, string>(track.TrackId, $"track:{index + 1}"))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        var operations = new List<TrackHeaderEditOperation>();

        foreach (var entry in retainedExistingVideoTracks.Select((track, index) => new { Track = track, Index = index }))
        {
            TryAppendTrackHeaderEdit(
                operations,
                selectorByTrackId,
                entry.Track,
                BuildExpectedVideoTrackName(entry.Track),
                $"Video {entry.Track.TrackId}",
                expectedLanguageCode: entry.Track.Language,
                expectedDefaultFlag: true,
                expectedVisualImpairedFlag: null,
                expectedHearingImpairedFlag: null,
                expectedForcedFlag: null,
                expectedOriginalFlag: SeriesOriginalLanguageRules.BuildExpectedOriginalFlag(entry.Track.Language, originalLanguage, seriesContext));
        }

        foreach (var track in retainedNormalAudioTracks)
        {
            TryAppendTrackHeaderEdit(
                operations,
                selectorByTrackId,
                track,
                BuildExpectedAudioTrackName(track),
                $"Audio {track.TrackId}",
                expectedLanguageCode: track.Language,
                expectedDefaultFlag: true,
                expectedVisualImpairedFlag: false,
                expectedHearingImpairedFlag: null,
                expectedForcedFlag: null,
                expectedOriginalFlag: SeriesOriginalLanguageRules.BuildExpectedOriginalFlag(track.Language, originalLanguage, seriesContext));
        }

        foreach (var existingAudioDescription in existingAudioDescriptions)
        {
            var expectedLanguageCode = ResolveAudioDescriptionLanguage(existingAudioDescription, retainedNormalAudioTracks.FirstOrDefault()?.Language);
            TryAppendTrackHeaderEdit(
                operations,
                selectorByTrackId,
                existingAudioDescription,
                BuildExpectedAudioDescriptionTrackName(existingAudioDescription, retainedNormalAudioTracks.FirstOrDefault()?.Language),
                $"Audiodeskription {existingAudioDescription.TrackId}",
                expectedLanguageCode,
                expectedDefaultFlag: false,
                expectedVisualImpairedFlag: true,
                expectedHearingImpairedFlag: null,
                expectedForcedFlag: null,
                expectedOriginalFlag: SeriesOriginalLanguageRules.BuildExpectedOriginalFlag(expectedLanguageCode, originalLanguage, seriesContext));
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
                $"Untertitel {embeddedTrackId}",
                expectedLanguageCode: subtitlePlan.LanguageCode,
                expectedDefaultFlag: false,
                expectedVisualImpairedFlag: null,
                expectedHearingImpairedFlag: subtitlePlan.IsHearingImpaired,
                expectedForcedFlag: subtitlePlan.IsForced,
                expectedOriginalFlag: SeriesOriginalLanguageRules.BuildExpectedOriginalFlag(subtitlePlan.LanguageCode, originalLanguage, seriesContext));
        }

        return operations;
    }

    /// <summary>
    /// Formatiert Titel- und Trackänderungen als kompakte Vorschauzeilen.
    /// </summary>
    public static IReadOnlyList<string> BuildHeaderChangeNotes(
        ContainerTitleEditOperation? containerTitleEdit,
        IReadOnlyList<TrackHeaderEditOperation> trackHeaderEdits)
    {
        var notes = new List<string>();

        if (containerTitleEdit is not null)
        {
            notes.Add($"MKV-Titel: {FormatHeaderValue(containerTitleEdit.CurrentTitle)} -> {FormatHeaderValue(containerTitleEdit.ExpectedTitle)}");
        }

        notes.AddRange(trackHeaderEdits.Select(BuildTrackHeaderChangeNote));

        return notes;
    }

    /// <summary>
    /// Baut den projektweit erwarteten Namen einer Videospur.
    /// </summary>
    public static string BuildExpectedVideoTrackName(ContainerTrackMetadata track)
    {
        return $"{MediaLanguageHelper.GetLanguageDisplayName(track.Language)} - {ResolutionLabel.FromWidth(track.VideoWidth).Value} - {track.CodecLabel}";
    }

    /// <summary>
    /// Baut den projektweit erwarteten Namen einer normalen Audiospur.
    /// </summary>
    public static string BuildExpectedAudioTrackName(ContainerTrackMetadata track)
    {
        return $"{MediaLanguageHelper.GetLanguageDisplayName(track.Language)} - {track.CodecLabel}";
    }

    /// <summary>
    /// Baut den projektweit erwarteten Namen einer Audiodeskriptionsspur.
    /// </summary>
    public static string BuildExpectedAudioDescriptionTrackName(ContainerTrackMetadata track, string? fallbackLanguage)
    {
        var languageCode = ResolveAudioDescriptionLanguage(track, fallbackLanguage);
        return $"{MediaLanguageHelper.GetLanguageDisplayName(languageCode)} (sehbehinderte) - {track.CodecLabel}";
    }

    /// <summary>
    /// Prüft die projektweite Heuristik für Untertitel, die als hörgeschädigt/SDH markiert werden sollen.
    /// </summary>
    public static bool IsHearingImpairedSubtitleTrack(ContainerTrackMetadata track)
    {
        return track.IsHearingImpaired
            || ContainsHearingImpairedSubtitleMarker(track.TrackName);
    }

    /// <summary>
    /// Normalisiert leere Headerwerte für lesbare Änderungslisten.
    /// </summary>
    public static string FormatHeaderValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "(leer)"
            : value.Trim();
    }

    private static string BuildTrackHeaderChangeNote(TrackHeaderEditOperation edit)
    {
        if (edit.ValueEdits is { Count: > 0 })
        {
            if (edit.ValueEdits.Count == 1 && string.Equals(edit.ValueEdits[0].PropertyName, "name", StringComparison.Ordinal))
            {
                return BuildTrackNameHeaderChangeNote(edit);
            }

            var changes = edit.ValueEdits
                .Select(valueEdit => $"{valueEdit.DisplayName}: {FormatHeaderValue(valueEdit.CurrentDisplayValue)} -> {FormatHeaderValue(valueEdit.ExpectedDisplayValue)}")
                .ToList();
            return $"{FormatHeaderValue(edit.DisplayLabel)}: {string.Join("; ", changes)}";
        }

        return BuildTrackNameHeaderChangeNote(edit);
    }

    private static string BuildTrackNameHeaderChangeNote(TrackHeaderEditOperation edit)
    {
        var currentValue = FormatHeaderValue(edit.CurrentTrackName);
        var expectedValue = FormatHeaderValue(edit.ExpectedTrackName);
        return string.Equals(FormatHeaderValue(edit.DisplayLabel), currentValue, StringComparison.Ordinal)
            ? $"{currentValue} -> {expectedValue}"
            : $"{FormatHeaderValue(edit.DisplayLabel)}: {currentValue} -> {expectedValue}";
    }

    private static string? ResolveAudioDescriptionLanguage(ContainerTrackMetadata track, string? fallbackLanguage)
    {
        return string.IsNullOrWhiteSpace(track.Language)
            ? fallbackLanguage
            : track.Language;
    }

    private static string BuildEmbeddedSubtitleLabel(ContainerTrackMetadata track, SubtitleKind kind)
    {
        if (!string.IsNullOrWhiteSpace(track.TrackName))
        {
            return track.TrackName;
        }

        return $"Archiv-Untertitel {kind.DisplayName}";
    }

    private static void TryAppendTrackHeaderEdit(
        ICollection<TrackHeaderEditOperation> operations,
        IReadOnlyDictionary<int, string> selectorByTrackId,
        ContainerTrackMetadata track,
        string expectedTrackName,
        string fallbackDisplayLabel,
        string? expectedLanguageCode,
        bool? expectedDefaultFlag,
        bool? expectedVisualImpairedFlag,
        bool? expectedHearingImpairedFlag,
        bool? expectedForcedFlag,
        bool? expectedOriginalFlag)
    {
        if (!selectorByTrackId.TryGetValue(track.TrackId, out var selector))
        {
            return;
        }

        var valueEdits = BuildTrackHeaderValueEdits(
            track,
            expectedTrackName,
            expectedLanguageCode,
            expectedDefaultFlag,
            expectedVisualImpairedFlag,
            expectedHearingImpairedFlag,
            expectedForcedFlag,
            expectedOriginalFlag);
        if (valueEdits.Count == 0)
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
            expectedTrackName,
            valueEdits));
    }

    private static IReadOnlyList<TrackHeaderValueEdit> BuildTrackHeaderValueEdits(
        ContainerTrackMetadata track,
        string expectedTrackName,
        string? expectedLanguageCode,
        bool? expectedDefaultFlag,
        bool? expectedVisualImpairedFlag,
        bool? expectedHearingImpairedFlag,
        bool? expectedForcedFlag,
        bool? expectedOriginalFlag)
    {
        var edits = new List<TrackHeaderValueEdit>();
        TryAddTextHeaderEdit(
            edits,
            propertyName: "name",
            displayName: "Name",
            currentValue: track.TrackName,
            expectedValue: expectedTrackName);

        var normalizedExpectedLanguage = MediaLanguageHelper.NormalizeMuxLanguageCode(expectedLanguageCode);
        TryAddTextHeaderEdit(
            edits,
            propertyName: "language",
            displayName: "Sprache",
            currentValue: MediaLanguageHelper.NormalizeMuxLanguageCode(track.Language),
            expectedValue: normalizedExpectedLanguage);

        TryAddFlagHeaderEdit(edits, "flag-default", "Standard", track.IsDefaultTrack, expectedDefaultFlag);
        TryAddFlagHeaderEdit(edits, "flag-visual-impaired", "Sehbehindert", track.IsVisualImpaired, expectedVisualImpairedFlag);
        TryAddFlagHeaderEdit(edits, "flag-hearing-impaired", "Hörgeschädigt", track.IsHearingImpaired, expectedHearingImpairedFlag);
        TryAddFlagHeaderEdit(edits, "flag-forced", "Forced", track.IsForcedTrack, expectedForcedFlag);
        TryAddFlagHeaderEdit(edits, "flag-original", "Originalsprache", track.IsOriginalLanguage, expectedOriginalFlag);

        return edits;
    }

    private static void TryAddTextHeaderEdit(
        ICollection<TrackHeaderValueEdit> edits,
        string propertyName,
        string displayName,
        string? currentValue,
        string? expectedValue)
    {
        var normalizedCurrent = (currentValue ?? string.Empty).Trim();
        var normalizedExpected = (expectedValue ?? string.Empty).Trim();
        if (string.Equals(normalizedCurrent, normalizedExpected, StringComparison.Ordinal))
        {
            return;
        }

        edits.Add(new TrackHeaderValueEdit(
            propertyName,
            displayName,
            normalizedCurrent,
            normalizedExpected,
            normalizedExpected));
    }

    private static void TryAddFlagHeaderEdit(
        ICollection<TrackHeaderValueEdit> edits,
        string propertyName,
        string displayName,
        bool currentValue,
        bool? expectedValue)
    {
        if (expectedValue is null || currentValue == expectedValue.Value)
        {
            return;
        }

        edits.Add(new TrackHeaderValueEdit(
            propertyName,
            displayName,
            FormatBooleanHeaderValue(currentValue),
            FormatBooleanHeaderValue(expectedValue.Value),
            expectedValue.Value ? "1" : "0"));
    }

    private static bool ContainsHearingImpairedSubtitleMarker(string? trackName)
    {
        if (string.IsNullOrWhiteSpace(trackName))
        {
            return false;
        }

        return trackName.Contains("hörgesch", StringComparison.OrdinalIgnoreCase)
            || trackName.Contains("hoergesch", StringComparison.OrdinalIgnoreCase)
            || trackName.Contains("hearing impaired", StringComparison.OrdinalIgnoreCase)
            || trackName.Contains("sdh", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatBooleanHeaderValue(bool value)
    {
        return value ? "ja" : "nein";
    }
}

/// <summary>
/// Vollständig vorbereitete Header-Normalisierung für eine vorhandene Archiv-MKV.
/// </summary>
internal sealed record ArchiveHeaderNormalizationResult(
    ContainerTitleEditOperation? ContainerTitleEdit,
    IReadOnlyList<TrackHeaderEditOperation> TrackHeaderEdits)
{
    /// <summary>
    /// Kennzeichnet, ob mindestens eine direkte <c>mkvpropedit</c>-Änderung geplant ist.
    /// </summary>
    public bool HasChanges => ContainerTitleEdit is not null || TrackHeaderEdits.Count > 0;
}
