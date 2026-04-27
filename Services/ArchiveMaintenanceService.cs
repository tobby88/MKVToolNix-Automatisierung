using System.Text.RegularExpressions;
using System.Net.Http;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Scannt vorhandene Archiv-MKV-Dateien, bewertet sie gegen die gemeinsamen Mux-Headerregeln
/// und führt nach expliziter Auswahl nur direkte Header- oder Umbenennungsänderungen aus.
/// </summary>
internal sealed class ArchiveMaintenanceService
{
    private static readonly Regex EpisodeFileNamePattern = new(
        @"^\s*(?<series>.+?)\s+-\s+S(?<season>\d{2,4}|xx)E(?<episode>\d{2,4}(?:-E\d{2,4})?|xx)\s+-\s+(?<title>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] SidecarExtensions = [".nfo", ".jpg", ".jpeg", ".png", ".webp"];

    private readonly MkvMergeProbeService _probeService;
    private readonly IMkvToolNixLocator _toolLocator;
    private readonly MuxExecutionService _executionService;
    private readonly EpisodeMetadataLookupService? _metadataLookup;
    private readonly EmbyNfoProviderIdService? _nfoProviderIds;

    /// <summary>
    /// Initialisiert die Archivpflege mit Probe-, Tool- und Prozessdiensten.
    /// </summary>
    public ArchiveMaintenanceService(
        MkvMergeProbeService probeService,
        IMkvToolNixLocator toolLocator,
        MuxExecutionService executionService,
        EpisodeMetadataLookupService? metadataLookup = null,
        EmbyNfoProviderIdService? nfoProviderIds = null)
    {
        _probeService = probeService;
        _toolLocator = toolLocator;
        _executionService = executionService;
        _metadataLookup = metadataLookup;
        _nfoProviderIds = nfoProviderIds;
    }

    /// <summary>
    /// Scannt rekursiv alle MKV-Dateien unterhalb eines Archivordners und bewertet sie einzeln.
    /// </summary>
    public async Task<ArchiveMaintenanceScanResult> ScanAsync(
        string rootDirectory,
        IProgress<ArchiveMaintenanceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (!Directory.Exists(rootDirectory))
        {
            throw new DirectoryNotFoundException($"Der Archivordner wurde nicht gefunden: {rootDirectory}");
        }

        var mkvMergePath = _toolLocator.FindMkvMergePath();
        var mediaFiles = Directory
            .EnumerateFiles(rootDirectory, "*.mkv", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var items = new List<ArchiveMaintenanceItemAnalysis>(mediaFiles.Count);

        for (var index = 0; index < mediaFiles.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = mediaFiles[index];
            progress?.Report(new ArchiveMaintenanceProgress(
                $"Prüfe {index + 1}/{mediaFiles.Count}: {Path.GetFileName(filePath)}",
                mediaFiles.Count == 0 ? 100 : index * 100 / mediaFiles.Count));

            items.Add(await AnalyzeFileAsync(mkvMergePath, filePath, cancellationToken));
        }

        progress?.Report(new ArchiveMaintenanceProgress(
            $"Archivpflege-Scan abgeschlossen: {items.Count} Datei(en).",
            100));
        return new ArchiveMaintenanceScanResult(rootDirectory, items);
    }

    /// <summary>
    /// Führt die freigegebenen Header- und Umbenennungsänderungen für eine einzelne MKV aus.
    /// </summary>
    public async Task<ArchiveMaintenanceApplyResult> ApplyAsync(
        ArchiveMaintenanceApplyRequest request,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.HasWritableChanges)
        {
            return new ArchiveMaintenanceApplyResult(
                request.FilePath,
                request.FilePath,
                Success: true,
                Message: "Keine schreibbaren Änderungen vorhanden.",
                OutputLines: []);
        }

        var outputLines = new List<string>();
        var currentPath = request.FilePath;
        if (request.ContainerTitleEdit is not null || request.TrackHeaderEdits.Count > 0)
        {
            var mkvPropEditPath = _toolLocator.FindMkvPropEditPath();
            var arguments = SeriesEpisodeMuxHeaderEditArgumentBuilder.Build(
                currentPath,
                request.ContainerTitleEdit,
                request.TrackHeaderEdits);
            var exitCode = await _executionService.ExecuteAsync(
                mkvPropEditPath,
                arguments,
                "mkvpropedit",
                line =>
                {
                    outputLines.Add(line);
                    output?.Report(line);
                },
                cancellationToken);
            if (exitCode != 0)
            {
                return new ArchiveMaintenanceApplyResult(
                    request.FilePath,
                    currentPath,
                    Success: false,
                    Message: $"mkvpropedit wurde mit Exitcode {exitCode} beendet.",
                    outputLines);
            }

            outputLines.Add("Header aktualisiert.");
        }

        if (request.RenameOperation is not null)
        {
            currentPath = ApplyRename(request.RenameOperation);
            outputLines.Add($"Datei umbenannt: {Path.GetFileName(request.RenameOperation.SourcePath)} -> {Path.GetFileName(currentPath)}");
        }

        _probeService.Invalidate([request.FilePath, currentPath]);
        return new ArchiveMaintenanceApplyResult(
            request.FilePath,
            currentPath,
            Success: true,
            Message: "Archivpflege-Änderungen angewendet.",
            outputLines);
    }

    private async Task<ArchiveMaintenanceItemAnalysis> AnalyzeFileAsync(
        string mkvMergePath,
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var container = await _probeService.ReadContainerMetadataAsync(mkvMergePath, filePath, cancellationToken);
            var expectedMetadata = await TryResolveExpectedMetadataFromNfoAsync(filePath, cancellationToken);
            return AnalyzeContainer(filePath, container, expectedMetadata);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new ArchiveMaintenanceItemAnalysis(
                filePath,
                Path.GetFileNameWithoutExtension(filePath),
                ContainerTitle: Path.GetFileNameWithoutExtension(filePath),
                RenameOperation: null,
                ContainerTitleEdit: null,
                TrackHeaderEdits: [],
                TrackHeaderCorrectionCandidates: [],
                Issues: [],
                ChangeNotes: [],
                ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// Bewertet einen bereits gelesenen Container ohne Toolzugriff. Dieser Pfad hält die
    /// Fachregeln testbar und wird vom rekursiven Scan nach dem eigentlichen Probe-Aufruf genutzt.
    /// </summary>
    internal static ArchiveMaintenanceItemAnalysis AnalyzeContainer(
        string filePath,
        ContainerMetadata container,
        ArchiveExpectedEpisodeMetadata? expectedMetadata = null)
    {
        var parsedName = TryParseEpisodeFileName(filePath);
        var expectedTitle = expectedMetadata?.Title ?? parsedName?.Title ?? Path.GetFileNameWithoutExtension(filePath);
        var normalization = ArchiveHeaderNormalizationService.BuildForArchiveFile(
            filePath,
            container,
            expectedTitle,
            expectedMetadata?.OriginalLanguage);
        var renameOperation = BuildRenameOperation(filePath, parsedName, expectedMetadata);
        var issues = BuildRemuxIssues(container);
        var changeNotes = ArchiveHeaderNormalizationService
            .BuildHeaderChangeNotes(normalization.ContainerTitleEdit, normalization.TrackHeaderEdits)
            .Concat(renameOperation is null
                ? []
                : [$"Dateiname: {Path.GetFileName(renameOperation.SourcePath)} -> {Path.GetFileName(renameOperation.TargetPath)}"])
            .ToList();

        return new ArchiveMaintenanceItemAnalysis(
            filePath,
            expectedTitle,
            container.Title,
            renameOperation,
            normalization.ContainerTitleEdit,
            normalization.TrackHeaderEdits,
            BuildTrackHeaderCorrectionCandidates(container, normalization.TrackHeaderEdits),
            issues,
            changeNotes,
            ErrorMessage: null);
    }

    private async Task<ArchiveExpectedEpisodeMetadata?> TryResolveExpectedMetadataFromNfoAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (_metadataLookup is null || _nfoProviderIds is null)
        {
            return null;
        }

        var parsedName = TryParseEpisodeFileName(filePath);
        if (parsedName is null)
        {
            return null;
        }

        var nfoResult = _nfoProviderIds.ReadProviderIds(filePath);
        if (!nfoResult.NfoExists
            || !int.TryParse(nfoResult.ProviderIds.TvdbId, out var tvdbEpisodeId))
        {
            return null;
        }

        var mapping = _metadataLookup.FindSeriesMapping(parsedName.SeriesName);
        if (mapping is null)
        {
            return null;
        }

        try
        {
            var episodes = await _metadataLookup.LoadEpisodesAsync(mapping.TvdbSeriesId, cancellationToken);
            var episode = episodes.FirstOrDefault(candidate => candidate.Id == tvdbEpisodeId);
            if (episode is null || string.IsNullOrWhiteSpace(episode.Name))
            {
                return null;
            }

            return new ArchiveExpectedEpisodeMetadata(
                episode.Name.Trim(),
                episode.SeasonNumber?.ToString("00") ?? parsedName.SeasonNumber,
                episode.EpisodeNumber?.ToString("00") ?? parsedName.EpisodeNumber,
                mapping.OriginalLanguage);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException)
        {
            return null;
        }
    }

    private static ArchiveEpisodeFileNameParts? TryParseEpisodeFileName(string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);
        var match = EpisodeFileNamePattern.Match(stem);
        if (!match.Success)
        {
            return null;
        }

        return new ArchiveEpisodeFileNameParts(
            match.Groups["series"].Value.Trim(),
            match.Groups["season"].Value.Trim(),
            match.Groups["episode"].Value.Trim(),
            match.Groups["title"].Value.Trim());
    }

    private static ArchiveRenameOperation? BuildRenameOperation(
        string filePath,
        ArchiveEpisodeFileNameParts? parsedName,
        ArchiveExpectedEpisodeMetadata? expectedMetadata)
    {
        if (parsedName is null)
        {
            return null;
        }

        var expectedFileName = EpisodeFileNameHelper.BuildEpisodeFileName(
            parsedName.SeriesName,
            expectedMetadata?.SeasonNumber ?? parsedName.SeasonNumber,
            expectedMetadata?.EpisodeNumber ?? parsedName.EpisodeNumber,
            expectedMetadata?.Title ?? parsedName.Title);
        var targetPath = Path.Combine(
            Path.GetDirectoryName(filePath) ?? string.Empty,
            expectedFileName);
        return PathComparisonHelper.AreSamePath(filePath, targetPath)
            ? null
            : new ArchiveRenameOperation(filePath, targetPath, BuildSidecarRenameOperations(filePath, targetPath));
    }

    private static IReadOnlyList<ArchiveSidecarRenameOperation> BuildSidecarRenameOperations(string sourceMediaPath, string targetMediaPath)
    {
        var sourceBasePath = Path.Combine(
            Path.GetDirectoryName(sourceMediaPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(sourceMediaPath));
        var targetBasePath = Path.Combine(
            Path.GetDirectoryName(targetMediaPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(targetMediaPath));
        return SidecarExtensions
            .Select(extension => new ArchiveSidecarRenameOperation(sourceBasePath + extension, targetBasePath + extension))
            .Where(operation => File.Exists(operation.SourcePath))
            .ToList();
    }

    /// <summary>
    /// Baut eine sichere Umbenennungsoperation für einen manuell gesetzten Ziel-Dateinamen.
    /// Begleitdateien werden dabei nach denselben Regeln wie bei automatischen TVDB-Korrekturen mitgeführt.
    /// </summary>
    internal static ArchiveRenameOperation? BuildManualRenameOperation(string sourceMediaPath, string targetFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMediaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFileName);

        var targetPath = Path.Combine(
            Path.GetDirectoryName(sourceMediaPath) ?? string.Empty,
            targetFileName.Trim());
        return PathComparisonHelper.AreSamePath(sourceMediaPath, targetPath)
            ? null
            : new ArchiveRenameOperation(sourceMediaPath, targetPath, BuildSidecarRenameOperations(sourceMediaPath, targetPath));
    }

    private static IReadOnlyList<ArchiveTrackHeaderCorrectionCandidate> BuildTrackHeaderCorrectionCandidates(
        ContainerMetadata container,
        IReadOnlyList<TrackHeaderEditOperation> automaticEdits)
    {
        var automaticValueEdits = automaticEdits
            .SelectMany(edit => ResolveCorrectionValueEdits(edit).Select(value => new
            {
                edit.Selector,
                Value = value
            }))
            .ToDictionary(entry => (entry.Selector, entry.Value.PropertyName), entry => entry.Value);

        return container.Tracks
            .Select((track, index) =>
            {
                var selector = $"track:{index + 1}";
                return new ArchiveTrackHeaderCorrectionCandidate(
                    selector,
                    BuildTrackCorrectionDisplayLabel(track),
                    track.TrackName,
                    BuildTrackHeaderValueCandidates(selector, track, automaticValueEdits));
            })
            .ToList();
    }

    private static IReadOnlyList<TrackHeaderValueEdit> ResolveCorrectionValueEdits(TrackHeaderEditOperation edit)
    {
        return edit.ValueEdits is { Count: > 0 }
            ? edit.ValueEdits
            :
            [
                new TrackHeaderValueEdit(
                    "name",
                    "Name",
                    edit.CurrentTrackName,
                    edit.ExpectedTrackName,
                    edit.ExpectedTrackName)
            ];
    }

    private static IReadOnlyList<ArchiveTrackHeaderValueCandidate> BuildTrackHeaderValueCandidates(
        string selector,
        ContainerTrackMetadata track,
        IReadOnlyDictionary<(string Selector, string PropertyName), TrackHeaderValueEdit> automaticValueEdits)
    {
        return
        [
            BuildTextCorrectionValue(selector, "name", "Name", track.TrackName, automaticValueEdits),
            BuildTextCorrectionValue(selector, "language", "Sprache", MediaLanguageHelper.NormalizeMuxLanguageCode(track.Language), automaticValueEdits),
            BuildFlagCorrectionValue(selector, "flag-default", "Standard", track.IsDefaultTrack, automaticValueEdits),
            BuildFlagCorrectionValue(selector, "flag-visual-impaired", "Sehbehindert", track.IsVisualImpaired, automaticValueEdits),
            BuildFlagCorrectionValue(selector, "flag-hearing-impaired", "Hörgeschädigt", track.IsHearingImpaired, automaticValueEdits),
            BuildFlagCorrectionValue(selector, "flag-forced", "Forced", track.IsForcedTrack, automaticValueEdits),
            BuildFlagCorrectionValue(selector, "flag-original", "Originalsprache", track.IsOriginalLanguage, automaticValueEdits)
        ];
    }

    private static ArchiveTrackHeaderValueCandidate BuildTextCorrectionValue(
        string selector,
        string propertyName,
        string displayName,
        string currentValue,
        IReadOnlyDictionary<(string Selector, string PropertyName), TrackHeaderValueEdit> automaticValueEdits)
    {
        return automaticValueEdits.TryGetValue((selector, propertyName), out var automaticEdit)
            ? new ArchiveTrackHeaderValueCandidate(
                propertyName,
                displayName,
                automaticEdit.CurrentDisplayValue,
                automaticEdit.ExpectedDisplayValue,
                automaticEdit.ExpectedMkvPropEditValue,
                IsFlag: false)
            : new ArchiveTrackHeaderValueCandidate(
                propertyName,
                displayName,
                currentValue,
                currentValue,
                currentValue,
                IsFlag: false);
    }

    private static ArchiveTrackHeaderValueCandidate BuildFlagCorrectionValue(
        string selector,
        string propertyName,
        string displayName,
        bool currentValue,
        IReadOnlyDictionary<(string Selector, string PropertyName), TrackHeaderValueEdit> automaticValueEdits)
    {
        return automaticValueEdits.TryGetValue((selector, propertyName), out var automaticEdit)
            ? new ArchiveTrackHeaderValueCandidate(
                propertyName,
                displayName,
                automaticEdit.CurrentDisplayValue,
                automaticEdit.ExpectedDisplayValue,
                automaticEdit.ExpectedMkvPropEditValue,
                IsFlag: true)
            : new ArchiveTrackHeaderValueCandidate(
                propertyName,
                displayName,
                FormatBooleanHeaderValue(currentValue),
                FormatBooleanHeaderValue(currentValue),
                currentValue ? "1" : "0",
                IsFlag: true);
    }

    private static string BuildTrackCorrectionDisplayLabel(ContainerTrackMetadata track)
    {
        if (!string.IsNullOrWhiteSpace(track.TrackName))
        {
            return track.TrackName.Trim();
        }

        var typeLabel = track.Type.ToLowerInvariant() switch
        {
            "video" => "Video",
            "audio" => "Audio",
            "subtitles" => "Untertitel",
            _ => "Track"
        };
        return $"{typeLabel} {track.TrackId}";
    }

    private static IReadOnlyList<ArchiveMaintenanceIssue> BuildRemuxIssues(ContainerMetadata container)
    {
        var issues = new List<ArchiveMaintenanceIssue>();
        issues.AddRange(BuildDuplicateAudioDescriptionIssues(container));
        issues.AddRange(BuildDuplicateSubtitleIssues(container));
        return issues;
    }

    private static IEnumerable<ArchiveMaintenanceIssue> BuildDuplicateAudioDescriptionIssues(ContainerMetadata container)
    {
        return container.Tracks
            .Where(track => string.Equals(track.Type, "audio", StringComparison.OrdinalIgnoreCase))
            .Where(AudioTrackClassifier.IsAudioDescriptionTrack)
            .GroupBy(track => MediaLanguageHelper.NormalizeMuxLanguageCode(track.Language), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new ArchiveMaintenanceIssue(
                ArchiveMaintenanceIssueKind.RemuxRequired,
                $"Doppelte AD-Spuren für {MediaLanguageHelper.GetLanguageDisplayName(group.Key)}: {string.Join(", ", group.Select(track => track.TrackId))}."));
    }

    private static IEnumerable<ArchiveMaintenanceIssue> BuildDuplicateSubtitleIssues(ContainerMetadata container)
    {
        return container.Tracks
            .Where(track => string.Equals(track.Type, "subtitles", StringComparison.OrdinalIgnoreCase))
            .GroupBy(track => BuildSubtitleSlotKey(track), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new ArchiveMaintenanceIssue(
                ArchiveMaintenanceIssueKind.RemuxRequired,
                $"Doppelte Untertitel im Slot {BuildSubtitleSlotLabel(group.First())}: {string.Join(", ", group.Select(track => track.TrackId))}."));
    }

    private static string BuildSubtitleSlotKey(ContainerTrackMetadata track)
    {
        var kind = SubtitleKind.FromExistingCodec(track.CodecLabel);
        var accessibility = ArchiveHeaderNormalizationService.IsHearingImpairedSubtitleTrack(track)
            ? SubtitleAccessibility.HearingImpaired
            : SubtitleAccessibility.Standard;
        return $"{kind.DisplayName}|{MediaLanguageHelper.NormalizeMuxLanguageCode(track.Language)}|{accessibility}|{track.IsForcedTrack}";
    }

    private static string BuildSubtitleSlotLabel(ContainerTrackMetadata track)
    {
        var kind = SubtitleKind.FromExistingCodec(track.CodecLabel);
        var accessibility = ArchiveHeaderNormalizationService.IsHearingImpairedSubtitleTrack(track)
            ? "hörgeschädigt"
            : "normal";
        var forced = track.IsForcedTrack ? ", forced" : string.Empty;
        return $"{MediaLanguageHelper.GetLanguageDisplayName(track.Language)} {kind.DisplayName}, {accessibility}{forced}";
    }

    private static string FormatBooleanHeaderValue(bool value)
    {
        return value ? "ja" : "nein";
    }

    private static string ApplyRename(ArchiveRenameOperation renameOperation)
    {
        if (!File.Exists(renameOperation.SourcePath))
        {
            throw new FileNotFoundException("Die umzubenennende MKV wurde nicht gefunden.", renameOperation.SourcePath);
        }

        if (File.Exists(renameOperation.TargetPath))
        {
            throw new IOException($"Die Ziel-MKV existiert bereits: {renameOperation.TargetPath}");
        }

        foreach (var sidecar in renameOperation.Sidecars)
        {
            if (File.Exists(sidecar.TargetPath))
            {
                throw new IOException($"Eine Ziel-Begleitdatei existiert bereits: {sidecar.TargetPath}");
            }
        }

        File.Move(renameOperation.SourcePath, renameOperation.TargetPath);
        foreach (var sidecar in renameOperation.Sidecars)
        {
            File.Move(sidecar.SourcePath, sidecar.TargetPath);
        }

        return renameOperation.TargetPath;
    }
}

internal sealed record ArchiveMaintenanceProgress(string StatusText, int ProgressPercent);

internal sealed record ArchiveMaintenanceScanResult(
    string RootDirectory,
    IReadOnlyList<ArchiveMaintenanceItemAnalysis> Items);

internal sealed record ArchiveMaintenanceItemAnalysis(
    string FilePath,
    string ExpectedTitle,
    string ContainerTitle,
    ArchiveRenameOperation? RenameOperation,
    ContainerTitleEditOperation? ContainerTitleEdit,
    IReadOnlyList<TrackHeaderEditOperation> TrackHeaderEdits,
    IReadOnlyList<ArchiveTrackHeaderCorrectionCandidate> TrackHeaderCorrectionCandidates,
    IReadOnlyList<ArchiveMaintenanceIssue> Issues,
    IReadOnlyList<string> ChangeNotes,
    string? ErrorMessage)
{
    public bool HasWritableChanges => ContainerTitleEdit is not null || TrackHeaderEdits.Count > 0 || RenameOperation is not null;

    public bool RequiresRemux => Issues.Any(issue => issue.Kind == ArchiveMaintenanceIssueKind.RemuxRequired);

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
}

internal sealed record ArchiveMaintenanceApplyRequest(
    string FilePath,
    ArchiveRenameOperation? RenameOperation,
    ContainerTitleEditOperation? ContainerTitleEdit,
    IReadOnlyList<TrackHeaderEditOperation> TrackHeaderEdits)
{
    public bool HasWritableChanges => RenameOperation is not null || ContainerTitleEdit is not null || TrackHeaderEdits.Count > 0;
}

internal sealed record ArchiveMaintenanceApplyResult(
    string OriginalFilePath,
    string CurrentFilePath,
    bool Success,
    string Message,
    IReadOnlyList<string> OutputLines);

internal sealed record ArchiveMaintenanceIssue(
    ArchiveMaintenanceIssueKind Kind,
    string Message);

internal enum ArchiveMaintenanceIssueKind
{
    RemuxRequired
}

internal sealed record ArchiveTrackHeaderCorrectionCandidate(
    string Selector,
    string DisplayLabel,
    string CurrentTrackName,
    IReadOnlyList<ArchiveTrackHeaderValueCandidate> Values);

internal sealed record ArchiveTrackHeaderValueCandidate(
    string PropertyName,
    string DisplayName,
    string CurrentDisplayValue,
    string ExpectedDisplayValue,
    string ExpectedMkvPropEditValue,
    bool IsFlag);

internal sealed record ArchiveRenameOperation(
    string SourcePath,
    string TargetPath,
    IReadOnlyList<ArchiveSidecarRenameOperation> Sidecars);

internal sealed record ArchiveSidecarRenameOperation(
    string SourcePath,
    string TargetPath);

internal sealed record ArchiveEpisodeFileNameParts(
    string SeriesName,
    string SeasonNumber,
    string EpisodeNumber,
    string Title);

internal sealed record ArchiveExpectedEpisodeMetadata(
    string Title,
    string SeasonNumber,
    string EpisodeNumber,
    string? OriginalLanguage);
