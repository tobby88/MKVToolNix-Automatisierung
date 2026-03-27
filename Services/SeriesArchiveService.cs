using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Verwaltet die Serienbibliothek als bevorzugtes Ziel und entscheidet, wie bestehende Archiv-MKV-Dateien integriert werden.
/// </summary>
public sealed class SeriesArchiveService
{
    public const string DefaultArchiveRootDirectory = @"Z:\Videos\Serien";

    private readonly MkvMergeProbeService _probeService;
    private readonly AppArchiveSettingsStore _archiveSettingsStore;

    public SeriesArchiveService(MkvMergeProbeService probeService, AppArchiveSettingsStore archiveSettingsStore)
    {
        _probeService = probeService;
        _archiveSettingsStore = archiveSettingsStore;
        ArchiveRootDirectory = NormalizeArchiveRootDirectory(_archiveSettingsStore.Load().DefaultSeriesArchiveRootPath);
    }

    /// <summary>
    /// Aktuell konfigurierter Wurzelpfad der Serienbibliothek.
    /// </summary>
    public string ArchiveRootDirectory { get; private set; }

    /// <summary>
    /// Baut den bevorzugten Ausgabepfad für eine Episode relativ zur Serienbibliothek oder zum Fallback-Ordner.
    /// </summary>
    /// <param name="fallbackDirectory">Verzeichnis, das genutzt wird, wenn die Serienbibliothek nicht erreichbar ist.</param>
    /// <param name="seriesName">Serienname der Episode.</param>
    /// <param name="seasonNumber">Normalisierte Staffelnummer oder Jahresstaffel.</param>
    /// <param name="episodeNumber">Normalisierte Episodennummer.</param>
    /// <param name="title">Episodentitel.</param>
    /// <returns>Vollständiger Zielpfad für die erzeugte MKV-Datei.</returns>
    public string BuildSuggestedOutputPath(string fallbackDirectory, string seriesName, string seasonNumber, string episodeNumber, string title)
    {
        if (!IsArchiveAvailable())
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

        return Path.Combine(targetDirectory, EpisodeFileNameHelper.BuildEpisodeFileName(seriesName, seasonNumber, episodeNumber, title));
    }

    /// <summary>
    /// Prüft, ob ein Ausgabepfad innerhalb der konfigurierten Serienbibliothek liegt.
    /// </summary>
    /// <param name="outputFilePath">Zu prüfender Ausgabepfad.</param>
    /// <returns><see langword="true"/>, wenn der Pfad innerhalb der Archivwurzel liegt.</returns>
    public bool IsArchivePath(string outputFilePath)
    {
        return PathComparisonHelper.IsPathWithinRoot(outputFilePath, ArchiveRootDirectory);
    }

    /// <summary>
    /// Speichert einen neuen Standardpfad für die Serienbibliothek dauerhaft in den App-Einstellungen.
    /// </summary>
    /// <param name="archiveRootDirectory">Neuer Bibliothekswurzelpfad.</param>
    public void ConfigureArchiveRootDirectory(string archiveRootDirectory)
    {
        var normalizedPath = NormalizeArchiveRootDirectory(archiveRootDirectory);
        _archiveSettingsStore.Save(new AppArchiveSettings
        {
            DefaultSeriesArchiveRootPath = normalizedPath
        });
        ArchiveRootDirectory = normalizedPath;
    }

    /// <summary>
    /// Prüft, ob die konfigurierte Serienbibliothek aktuell erreichbar ist.
    /// </summary>
    /// <returns><see langword="true"/>, wenn das Zielverzeichnis existiert.</returns>
    public bool IsArchiveAvailable()
    {
        return Directory.Exists(ArchiveRootDirectory);
    }

    /// <summary>
    /// Erzeugt den UI-Hinweistext für den Fall, dass die Bibliothek aktuell nicht erreichbar ist.
    /// </summary>
    /// <returns>Mehrzeilige Warnmeldung mit dem konfigurierten Bibliothekspfad.</returns>
    public string BuildArchiveUnavailableWarningMessage()
    {
        return "Die konfigurierte Serienbibliothek ist aktuell nicht erreichbar:"
            + Environment.NewLine
            + ArchiveRootDirectory
            + Environment.NewLine
            + Environment.NewLine
            + "Automatische Ausgabepfade verwenden deshalb vorerst den jeweiligen Quellordner.";
    }

    /// <summary>
    /// Entscheidet, wie eine vorhandene Archivdatei in einen neuen Mux-Lauf eingebunden werden soll.
    /// </summary>
    /// <param name="mkvMergePath">Pfad zur verwendeten mkvmerge-Executable.</param>
    /// <param name="request">Aktuelle Nutzereingaben für den Mux-Lauf.</param>
    /// <param name="detected">Automatisch erkannte Quellen und Vorschläge.</param>
    /// <param name="plannedVideoPaths">Geplante Videodateien für den Lauf.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Archiventscheidung inklusive eventueller Arbeitskopie, zu übernehmender Tracks und Notizen.</returns>
    public async Task<ArchiveIntegrationDecision> PrepareAsync(
        string mkvMergePath,
        SeriesEpisodeMuxRequest request,
        AutoDetectedEpisodeFiles detected,
        IReadOnlyList<string> plannedVideoPaths,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var outputPath = request.OutputFilePath;
        if (!File.Exists(outputPath))
        {
            return ArchiveIntegrationDecision.CreateForFreshTarget(outputPath);
        }

        var existingContainer = await _probeService.ReadContainerMetadataAsync(mkvMergePath, outputPath, cancellationToken);
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
            cancellationToken.ThrowIfCancellationRequested();
            newVideoMetadata.Add((videoPath, await _probeService.ReadPrimaryVideoMetadataAsync(mkvMergePath, videoPath, cancellationToken)));
        }

        var newPrimaryVideo = newVideoMetadata.FirstOrDefault();
        if (newPrimaryVideo == default)
        {
            return ArchiveIntegrationDecision.CreateForFreshTarget(outputPath);
        }

        var bestExistingVideo = existingVideoTracks
            .OrderByDescending(track => track.VideoWidth)
            .ThenBy(track => MediaCodecPreferenceHelper.GetVideoCodecPreferenceRank(track.CodecLabel))
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
            .Where(plan => existingSubtitleKinds.All(kind => !string.Equals(kind.DisplayName, plan.Kind.DisplayName, StringComparison.OrdinalIgnoreCase)))
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
            .Select(entry => new SubtitleFile(
                outputPath,
                entry.Kind!,
                entry.Track.TrackId,
                BuildEmbeddedSubtitleLabel(entry.Track, entry.Kind!)))
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
                PrimarySubtitleTrackIds: finalSubtitlePlans.Count > 0 ? [] : null,
                IncludePrimaryAttachments: true,
                AdditionalVideoPaths: newAdditionalVideoPaths,
                AudioDescriptionFilePath: existingAudioDescription is null
                    ? request.AudioDescriptionPath
                    : outputPath,
                AudioDescriptionTrackId: existingAudioDescription?.TrackId,
                SubtitleFiles: finalSubtitlePlans,
                AttachmentFilePaths: BuildAttachmentPathsForUsedVideos(newAdditionalVideoPaths),
                FallbackToRequestAttachments: false,
                PreservedAttachmentNames: existingContainer.Attachments.Select(attachment => attachment.FileName).ToList(),
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
            FallbackToRequestAttachments: true,
            PreservedAttachmentNames: [],
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

    private static string EpisodeMetadataPath(string fallbackDirectory, string seriesName, string seasonNumber, string episodeNumber, string title)
    {
        return Path.Combine(fallbackDirectory, EpisodeFileNameHelper.BuildEpisodeFileName(seriesName, seasonNumber, episodeNumber, title));
    }

    private static string NormalizeArchiveRootDirectory(string? archiveRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(archiveRootDirectory))
        {
            return DefaultArchiveRootDirectory;
        }

        try
        {
            var fullPath = Path.GetFullPath(archiveRootDirectory.Trim());
            var rootPath = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return DefaultArchiveRootDirectory;
        }
    }

    private static string SanitizePathPart(string value)
    {
        return string.Concat(value.Select(character => Path.GetInvalidPathChars().Contains(character) || character == ':' ? '_' : character)).Trim();
    }
}

/// <summary>
/// Beschreibt, wie eine bereits vorhandene Archivdatei in einen neuen Mux-Plan eingebunden werden soll.
/// </summary>
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
    bool FallbackToRequestAttachments,
    IReadOnlyList<string> PreservedAttachmentNames,
    IReadOnlyList<string> Notes)
{
    /// <summary>
    /// Erstellt eine Archiventscheidung für ein komplett neues Ziel ohne bestehende Archivdatei.
    /// </summary>
    /// <param name="outputPath">Zielpfad der neu zu erzeugenden MKV-Datei.</param>
    /// <returns>Standardentscheidung für einen frischen Mux-Lauf.</returns>
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
            FallbackToRequestAttachments: true,
            PreservedAttachmentNames: [],
            Notes: []);
    }

    /// <summary>
    /// Erstellt eine Archiventscheidung, die den eigentlichen Mux-Lauf vollständig überspringt.
    /// </summary>
    /// <param name="outputPath">Pfad der bereits vollständigen Zieldatei.</param>
    /// <param name="skipReason">Fachliche Begründung für das Überspringen.</param>
    /// <param name="notes">Zusätzliche Hinweise für UI und Vorschau.</param>
    /// <returns>Entscheidung für einen No-Op-Lauf.</returns>
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
            FallbackToRequestAttachments: false,
            PreservedAttachmentNames: [],
            Notes: notes);
    }
}

