using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

// Dieser Partial kapselt die eigentliche Mux-Planerzeugung aus bereits erkannten UI-Eingaben und Archivdaten.
public sealed partial class SeriesEpisodeMuxPlanner
{
    /// <summary>
    /// Erzeugt aus einer konkreten Episodeingabe einen vollständig aufgelösten Mux-Plan inklusive Archivintegration.
    /// </summary>
    /// <param name="request">Aktuelle Benutzereingabe mit Quellen, Zielpfad und optionalen Ausschlüssen.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal für Probe-, Archiv- und Planungsarbeit.</param>
    /// <returns>Fertiger Mux-Plan oder ein fachlicher Skip-Plan, wenn kein Lauf mehr nötig ist.</returns>
    public async Task<SeriesEpisodeMuxPlan> CreatePlanAsync(
        SeriesEpisodeMuxRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        var (planNotes, plannedVideoPaths) = ResolvePlanSourceSelection(request, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var subtitleFiles = request.SubtitlePaths
            .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => SubtitleFile.CreateDetectedExternal(path, SubtitleKind.FromExtension(Path.GetExtension(path))))
            .ToList();

        var mkvMergePath = _locator.FindMkvMergePath();

        var archiveDecision = await _archiveService.PrepareAsync(
            mkvMergePath,
            request,
            plannedVideoPaths,
            cancellationToken);

        if (archiveDecision.SkipMux)
        {
            return SeriesEpisodeMuxPlan.CreateSkip(
                mkvMergePath,
                archiveDecision.OutputFilePath,
                request.Title,
                archiveDecision.SkipReason ?? "Archiv bereits aktuell.",
                archiveDecision.SkipUsageSummary,
                archiveDecision.Notes);
        }

        if (!request.HasPrimaryVideoSource && string.IsNullOrWhiteSpace(archiveDecision.PrimarySourcePath))
        {
            throw new InvalidOperationException(
                "Zur ausgewählten AD-Datei wurde keine passende Hauptquelle gefunden, und am Ziel liegt noch keine wiederverwendbare Archiv-MKV. Die AD kann deshalb derzeit nicht verarbeitet werden.");
        }

        var effectiveOutputPath = archiveDecision.OutputFilePath;
        var videoSelections = archiveDecision.VideoSelections.Count > 0
            ? archiveDecision.VideoSelections
            : await BuildVideoSelectionsFromPlannedPathsAsync(mkvMergePath, plannedVideoPaths, cancellationToken);

        var videoSources = videoSelections
            .Select((videoSelection, index) => new VideoSourcePlan(
                videoSelection.FilePath,
                videoSelection.TrackId,
                BuildVideoTrackName(videoSelection.LanguageCode, videoSelection.VideoWidth, videoSelection.CodecLabel),
                IsDefaultTrack: index == 0,
                LanguageCode: videoSelection.LanguageCode))
            .ToList();
        var audioSources = await BuildNormalAudioSourcesAsync(
            mkvMergePath,
            effectiveOutputPath,
            videoSelections,
            archiveDecision.RetainedAudioTrackIds,
            cancellationToken);
        if (audioSources.Count == 0)
        {
            throw new InvalidOperationException("Es wurde keine normale Audiospur für den Mux-Plan gefunden.");
        }

        var primaryInputAudioTrackIds = audioSources
            .Where(source => string.Equals(source.FilePath, videoSources[0].FilePath, StringComparison.OrdinalIgnoreCase))
            .Select(source => source.TrackId)
            .Distinct()
            .ToList();
        var primaryAudioLanguage = audioSources[0].LanguageCode;
        var primaryAudioCodecLabel = TryReadCodecLabelFromTrackName(audioSources[0].TrackName) ?? "Audio";

        var audioDescriptionPath = !string.IsNullOrWhiteSpace(archiveDecision.AudioDescriptionFilePath)
            ? archiveDecision.AudioDescriptionFilePath
            : string.IsNullOrWhiteSpace(request.AudioDescriptionPath) ? null : request.AudioDescriptionPath;
        AudioTrackMetadata? audioDescriptionMetadata = audioDescriptionPath is null
            ? null
            : archiveDecision.AudioDescriptionTrackId is int trackId
                ? await ReadEmbeddedAudioTrackMetadataAsync(
                    mkvMergePath,
                    audioDescriptionPath,
                    trackId,
                    primaryAudioLanguage,
                    cancellationToken)
                : await _probeService.ReadFirstAudioTrackMetadataAsync(mkvMergePath, audioDescriptionPath, cancellationToken);

        var subtitleFilesForPlan = archiveDecision.SubtitleFiles.Count > 0
            ? archiveDecision.SubtitleFiles
            : subtitleFiles;

        var attachmentFilePaths = archiveDecision.AttachmentFilePaths.Count > 0
            ? archiveDecision.AttachmentFilePaths
            : archiveDecision.FallbackToRequestAttachments
                ? request.AttachmentPaths
                : [];

        var notes = planNotes
            .Concat(archiveDecision.Notes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (request.HasPrimaryVideoSource)
        {
            var durationMismatchHint = await TryBuildArchiveDurationMismatchHintAsync(
                mkvMergePath,
                request.MainVideoPath,
                effectiveOutputPath,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(durationMismatchHint))
            {
                notes.Add(durationMismatchHint);
            }
        }
        if (!request.HasPrimaryVideoSource && !string.IsNullOrWhiteSpace(archiveDecision.PrimarySourcePath))
        {
            notes.Insert(0, "Es liegt nur eine AD-Quelle vor. Die Hauptspuren werden aus der vorhandenen Archiv-MKV übernommen.");
        }
        if (audioDescriptionPath is not null && IsSrfSender(CompanionTextMetadataReader.ReadForMediaFile(audioDescriptionPath).Sender))
        {
            notes.Add("Die ausgewählte AD-Quelle stammt von SRF. Bitte die Datei vor dem Muxen prüfen.");
        }

        var (audioDescriptionTrackName, audioDescriptionLanguageCode) = BuildAudioDescriptionTrackMetadata(
            primaryAudioLanguage,
            primaryAudioCodecLabel,
            audioDescriptionMetadata);

        return new SeriesEpisodeMuxPlan(
            mkvMergePath,
            effectiveOutputPath,
            request.Title,
            videoSources,
            audioSources,
            primaryInputAudioTrackIds,
            archiveDecision.PrimarySubtitleTrackIds,
            archiveDecision.PrimarySourceAttachmentIds,
            archiveDecision.IncludePrimaryAttachments,
            archiveDecision.AttachmentSourcePath,
            archiveDecision.AttachmentSourceAttachmentIds,
            audioDescriptionPath,
            audioDescriptionMetadata?.TrackId,
            audioDescriptionTrackName,
            audioDescriptionLanguageCode,
            subtitleFilesForPlan,
            attachmentFilePaths,
            archiveDecision.PreservedAttachmentNames,
            archiveDecision.UsageComparison,
            archiveDecision.WorkingCopy,
            notes.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// Ergänzt bei bestehender Bibliotheksdatei einen konservativen Hinweis, wenn Quelle und Archivdatei
    /// ungefähr ein ganzzahliges Laufzeitvielfaches voneinander abweichen. Genau dieses Muster tritt
    /// typischerweise bei zusammengefassten Doppelfolgen oder Mehrfachfolgen auf.
    /// Die Heuristik arbeitet bewusst nur mit bereits vorhandenen Begleitmetadaten aus TXT-Dateien
    /// oder eindeutigem eingebettetem TXT-Anhang. Sie soll helfen, darf aber die eigentliche
    /// Planerstellung nicht mit zusätzlichen Medienprobes ausbremsen.
    /// </summary>
    private async Task<string?> TryBuildArchiveDurationMismatchHintAsync(
        string mkvMergePath,
        string? sourceFilePath,
        string? archiveOutputPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath)
            || string.IsNullOrWhiteSpace(archiveOutputPath)
            || !_archiveService.IsArchivePath(archiveOutputPath)
            || !File.Exists(sourceFilePath)
            || !File.Exists(archiveOutputPath)
            || PathComparisonHelper.AreSamePath(sourceFilePath, archiveOutputPath))
        {
            return null;
        }

        var sourceDuration = CompanionTextMetadataReader.ReadForMediaFile(sourceFilePath).Duration;
        var archiveDuration = await _archiveService.TryReadUniqueEmbeddedTextDurationAsync(
            mkvMergePath,
            archiveOutputPath,
            cancellationToken);
        int? sourceSeconds = sourceDuration is null
            ? null
            : (int)Math.Round(sourceDuration.Value.TotalSeconds);
        int? archiveSeconds = archiveDuration is null
            ? null
            : (int)Math.Round(archiveDuration.Value.TotalSeconds);
        var durationMultiple = TryFindNearIntegerMultiple(sourceSeconds, archiveSeconds);
        if (durationMultiple is null)
        {
            return null;
        }

        var archiveIsLonger = archiveSeconds > sourceSeconds;
        var relationText = archiveIsLonger
            ? $"Die vorhandene Bibliotheksdatei ist ungefähr {durationMultiple}-mal so lang wie die aktuelle Quelle."
            : $"Die aktuelle Quelle ist ungefähr {durationMultiple}-mal so lang wie die vorhandene Bibliotheksdatei.";
        return relationText
            + " Möglicherweise handelt es sich um eine Doppelfolge oder um einen Teil einer Mehrfachfolge. Bitte Archivtreffer und Episodencode prüfen.";
    }

    private static int? TryFindNearIntegerMultiple(int? firstSeconds, int? secondSeconds)
    {
        if (firstSeconds is null or <= 0 || secondSeconds is null or <= 0)
        {
            return null;
        }

        var shorter = Math.Min(firstSeconds.Value, secondSeconds.Value);
        var longer = Math.Max(firstSeconds.Value, secondSeconds.Value);

        for (var factor = 2; factor <= 4; factor++)
        {
            var expectedLonger = shorter * factor;
            var toleranceSeconds = Math.Max(90, (int)Math.Round(expectedLonger * 0.08));
            if (Math.Abs(longer - expectedLonger) <= toleranceSeconds)
            {
                return factor;
            }
        }

        return null;
    }

    private (IReadOnlyList<string> Notes, IReadOnlyList<string> PlannedVideoPaths) ResolvePlanSourceSelection(
        SeriesEpisodeMuxRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasPrimaryVideoSource)
        {
            return ([], []);
        }

        var plannedVideoPaths = request.PlannedVideoPaths?
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (plannedVideoPaths is { Count: > 0 })
        {
            return (
                request.DetectionNotes?
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                ?? [],
                plannedVideoPaths);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var detected = DetectFromMainVideo(
            request.MainVideoPath,
            excludedSourcePaths: request.ExcludedSourcePaths,
            cancellationToken: cancellationToken);

        return (
            detected.Notes,
            new[] { detected.MainVideoPath }
                .Concat(detected.AdditionalVideoPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private async Task<IReadOnlyList<VideoTrackSelection>> BuildVideoSelectionsFromPlannedPathsAsync(
        string mkvMergePath,
        IReadOnlyList<string> plannedVideoPaths,
        CancellationToken cancellationToken)
    {
        var selections = new List<VideoTrackSelection>(plannedVideoPaths.Count);

        foreach (var videoPath in plannedVideoPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = await _probeService.ReadPrimaryVideoMetadataAsync(mkvMergePath, videoPath, cancellationToken);
            selections.Add(new VideoTrackSelection(
                videoPath,
                metadata.VideoTrackId,
                metadata.VideoWidth,
                metadata.VideoCodecLabel,
                MediaLanguageHelper.NormalizeMuxLanguageCode(metadata.VideoLanguage)));
        }

        return selections;
    }

    private async Task<IReadOnlyList<AudioSourcePlan>> BuildNormalAudioSourcesAsync(
        string mkvMergePath,
        string outputFilePath,
        IReadOnlyList<VideoTrackSelection> videoSelections,
        IReadOnlyList<int>? retainedEmbeddedAudioTrackIds,
        CancellationToken cancellationToken)
    {
        var audioSources = new List<AudioSourcePlan>();
        var processedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var videoSelection in videoSelections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!processedFilePaths.Add(videoSelection.FilePath))
            {
                continue;
            }

            if (string.Equals(videoSelection.FilePath, outputFilePath, StringComparison.OrdinalIgnoreCase)
                && retainedEmbeddedAudioTrackIds is { Count: > 0 })
            {
                var retainedEmbeddedSources = await BuildEmbeddedAudioSourcesAsync(
                    mkvMergePath,
                    outputFilePath,
                    retainedEmbeddedAudioTrackIds,
                    videoSelection.LanguageCode,
                    cancellationToken);
                audioSources.AddRange(retainedEmbeddedSources);
                continue;
            }

            var freshAudioSources = await BuildFreshNormalAudioSourcesAsync(
                mkvMergePath,
                videoSelection.FilePath,
                cancellationToken);
            audioSources.AddRange(freshAudioSources);
        }

        if (!processedFilePaths.Contains(outputFilePath)
            && retainedEmbeddedAudioTrackIds is { Count: > 0 })
        {
            audioSources.AddRange(await BuildEmbeddedAudioSourcesAsync(
                mkvMergePath,
                outputFilePath,
                retainedEmbeddedAudioTrackIds,
                fallbackLanguage: "de",
                cancellationToken));
        }

        return audioSources
            .Select((source, index) => source with { IsDefaultTrack = index == 0 })
            .ToList();
    }

    /// <summary>
    /// Liest aus einer frischen Videoquelle alle für den normalen Mux relevanten Audiospuren aus.
    /// </summary>
    /// <param name="mkvMergePath">Pfad zur verwendeten mkvmerge-Executable.</param>
    /// <param name="inputFilePath">Zu analysierende frische Videoquelle.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>
    /// Vollständig normalisierte normale Audiospuren der Quelle. Enthält bewusst mehrere
    /// Audiospuren derselben Datei, wenn die Quelle mehr als eine normale Spur liefert.
    /// </returns>
    private async Task<IReadOnlyList<AudioSourcePlan>> BuildFreshNormalAudioSourcesAsync(
        string mkvMergePath,
        string inputFilePath,
        CancellationToken cancellationToken)
    {
        var container = await _probeService.ReadContainerMetadataAsync(mkvMergePath, inputFilePath, cancellationToken);

        return AudioTrackClassifier.GetPreferredNormalAudioTracks(container.Tracks)
            .Select(track => new AudioSourcePlan(
                inputFilePath,
                track.TrackId,
                BuildNormalAudioTrackName(track.Language, track.CodecLabel),
                IsDefaultTrack: false,
                LanguageCode: MediaLanguageHelper.NormalizeMuxLanguageCode(track.Language)))
            .ToList();
    }

    private async Task<IReadOnlyList<AudioSourcePlan>> BuildEmbeddedAudioSourcesAsync(
        string mkvMergePath,
        string inputFilePath,
        IReadOnlyList<int> trackIds,
        string fallbackLanguage,
        CancellationToken cancellationToken)
    {
        var audioSources = new List<AudioSourcePlan>(trackIds.Count);

        foreach (var trackId in trackIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = await ReadEmbeddedAudioTrackMetadataAsync(
                mkvMergePath,
                inputFilePath,
                trackId,
                fallbackLanguage,
                cancellationToken);
            audioSources.Add(new AudioSourcePlan(
                inputFilePath,
                metadata.TrackId,
                BuildNormalAudioTrackName(metadata.Language, metadata.CodecLabel),
                IsDefaultTrack: false,
                LanguageCode: MediaLanguageHelper.NormalizeMuxLanguageCode(metadata.Language)));
        }

        return audioSources;
    }

    private static void ValidateRequest(SeriesEpisodeMuxRequest request)
    {
        if (request.HasPrimaryVideoSource && !File.Exists(request.MainVideoPath))
        {
            throw new FileNotFoundException($"Hauptvideo nicht gefunden: {request.MainVideoPath}");
        }

        foreach (var plannedVideoPath in request.PlannedVideoPaths ?? [])
        {
            if (!File.Exists(plannedVideoPath))
            {
                throw new FileNotFoundException($"Geplante Videoquelle nicht gefunden: {plannedVideoPath}");
            }
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

        ValidateOutputPath(request.OutputFilePath);
    }

    private static void ValidateOutputPath(string outputFilePath)
    {
        var outputDirectory = Path.GetDirectoryName(outputFilePath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new DirectoryNotFoundException($"Ausgabeziel konnte nicht bestimmt werden: {outputFilePath}");
        }
    }
}
