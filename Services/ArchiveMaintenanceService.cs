using System.Text.RegularExpressions;
using System.Net.Http;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Fachschnittstelle für die Archivpflege. Sie hält das ViewModel testbar, ohne echte
/// mkvmerge-Prozesse starten zu müssen, und beschreibt die beiden langen Operationen.
/// </summary>
internal interface IArchiveMaintenanceService
{
    /// <summary>
    /// Scannt rekursiv alle MKV-Dateien unterhalb eines Archivordners und bewertet sie einzeln.
    /// </summary>
    Task<ArchiveMaintenanceScanResult> ScanAsync(
        string rootDirectory,
        IProgress<ArchiveMaintenanceProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Führt die freigegebenen Header- und Umbenennungsänderungen für eine einzelne MKV aus.
    /// </summary>
    Task<ArchiveMaintenanceApplyResult> ApplyAsync(
        ArchiveMaintenanceApplyRequest request,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Scannt vorhandene Archiv-MKV-Dateien, bewertet sie gegen die gemeinsamen Mux-Headerregeln
/// und führt nach expliziter Auswahl nur direkte Header- oder Umbenennungsänderungen aus.
/// </summary>
internal sealed class ArchiveMaintenanceService : IArchiveMaintenanceService
{
    private static readonly Regex EpisodeFileNamePattern = new(
        @"^\s*(?<series>.+?)\s+-\s+S(?<season>\d{2,4}|xx)E(?<episode>\d{2,4}(?:-E\d{2,4})?|xx)\s+-\s+(?<title>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SeasonFolderPattern = new(@"^Season\s+(?:\d+|xx)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SubtitleSidecarSuffixPattern = new(
        @"^(?:\.(?:de|deu|ger|en|eng|nds|fr|fra|fre|es|spa|it|ita|nl|nld|dut|sv|swe|da|dan|no|nor|fi|fin|pl|pol|pt|por|tr|tur|forced|sdh|cc|hoh|hi))*\.(?:srt|ass|ssa|vtt|ttml|sub|idx)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[] SidecarSuffixes =
    [
        ".nfo",
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        "-thumb.jpg",
        "-poster.jpg"
    ];

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
        var mediaFiles = new List<string>();
        foreach (var mediaFile in Directory.EnumerateFiles(rootDirectory, "*.mkv", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            mediaFiles.Add(mediaFile);
        }

        mediaFiles.Sort(StringComparer.OrdinalIgnoreCase);
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
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await ApplyCoreAsync(request, output, cancellationToken);
        }
        finally
        {
            // Auch ein fehlgeschlagener/abgebrochener Header-Eingriff kann die Datei bereits
            // verändert haben. Ein Folgescan darf dann keinen alten Probe-Snapshot verwenden.
            _probeService.Invalidate([request.FilePath, request.RenameOperation?.TargetPath ?? request.FilePath]);
        }
    }

    private async Task<ArchiveMaintenanceApplyResult> ApplyCoreAsync(
        ArchiveMaintenanceApplyRequest request,
        IProgress<string>? output,
        CancellationToken cancellationToken)
    {
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
        var outputLock = new object();
        var currentPath = request.FilePath;
        void AddOutputLine(string line)
        {
            // stdout und stderr können gleichzeitig melden.
            lock (outputLock)
            {
                outputLines.Add(line);
                output?.Report(line);
            }
        }

        if (request.RenameOperation is { } plannedRename)
        {
            try
            {
                if (!PathComparisonHelper.AreSamePath(request.FilePath, plannedRename.SourcePath))
                {
                    throw new IOException("Die Umbenennungsquelle stimmt nicht mit der bearbeiteten MKV überein.");
                }

                ValidateRename(plannedRename);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                return new ArchiveMaintenanceApplyResult(request.FilePath, currentPath, false,
                    $"Umbenennen fehlgeschlagen: {ex.Message}", outputLines);
            }
        }

        if (request.NfoTextEdit is { } plannedTextEdit)
        {
            var nfo = (_nfoProviderIds ?? new EmbyNfoProviderIdService()).ReadEpisodeMetadata(currentPath);
            if (!nfo.NfoExists || nfo.WarningMessage is not null
                || !string.Equals(nfo.Title?.Trim() ?? string.Empty, plannedTextEdit.CurrentTitle?.Trim() ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(nfo.SortTitle?.Trim() ?? string.Empty, plannedTextEdit.CurrentSortTitle?.Trim() ?? string.Empty, StringComparison.Ordinal)
                || nfo.IsTitleLocked != plannedTextEdit.CurrentTitleLocked
                || nfo.IsSortTitleLocked != plannedTextEdit.CurrentSortTitleLocked)
            {
                return new ArchiveMaintenanceApplyResult(request.FilePath, currentPath, false,
                    "NFO-Titel oder Sperren haben sich seit dem Scan geändert oder sind nicht lesbar. Bitte neu scannen.", outputLines);
            }
        }

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
                AddOutputLine,
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

            AddOutputLine("Header aktualisiert.");
        }

        if (request.ProviderIdEdit is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var providerIdService = _nfoProviderIds ?? new EmbyNfoProviderIdService();
            var updateResult = providerIdService.UpdateProviderIds(
                currentPath,
                request.ProviderIdEdit.ProviderIds,
                request.ProviderIdEdit.RemoveImdbId);
            AddOutputLine(updateResult.Message);
            if (!updateResult.Success)
            {
                return new ArchiveMaintenanceApplyResult(
                    request.FilePath,
                    currentPath,
                    Success: false,
                    Message: updateResult.Message,
                    outputLines);
            }
        }

        if (request.NfoTextEdit is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var providerIdService = _nfoProviderIds ?? new EmbyNfoProviderIdService();
            var updateResult = providerIdService.UpdateTextFields(
                currentPath,
                new EmbyNfoTextFields(
                    request.NfoTextEdit.ExpectedTitle,
                    request.NfoTextEdit.ExpectedSortTitle,
                    request.NfoTextEdit.ExpectedTitleLocked,
                    request.NfoTextEdit.ExpectedSortTitleLocked));
            AddOutputLine(updateResult.Message);
            if (!updateResult.Success)
            {
                return new ArchiveMaintenanceApplyResult(
                    request.FilePath,
                    currentPath,
                    Success: false,
                    Message: updateResult.Message,
                    outputLines);
            }
        }

        if (request.RenameOperation is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                currentPath = ApplyRename(request.RenameOperation);
                AddOutputLine($"Datei umbenannt: {Path.GetFileName(request.RenameOperation.SourcePath)} -> {Path.GetFileName(currentPath)}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                if (!File.Exists(request.FilePath) && File.Exists(request.RenameOperation.TargetPath))
                {
                    currentPath = request.RenameOperation.TargetPath;
                }

                var message = $"Umbenennen fehlgeschlagen: {ex.Message}";
                AddOutputLine(message);
                return new ArchiveMaintenanceApplyResult(
                    request.FilePath,
                    currentPath,
                    Success: false,
                    Message: message,
                    outputLines);
            }
        }

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
                ProviderIds: EmbyProviderIds.Empty,
                NfoExists: false,
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
        var nfoResult = new EmbyNfoProviderIdService().ReadEpisodeMetadata(filePath);
        var expectedTitle = ResolveExpectedTitleFromNfoAndTvdb(nfoResult,
            expectedMetadata?.Title ?? parsedName?.Title ?? Path.GetFileNameWithoutExtension(filePath));
        if (expectedMetadata is not null)
        {
            expectedMetadata = expectedMetadata with { Title = expectedTitle };
        }
        var normalization = ArchiveHeaderNormalizationService.BuildForArchiveFile(
            filePath,
            container,
            expectedTitle,
            expectedMetadata?.OriginalLanguage);
        var renameMetadata = expectedMetadata ?? (parsedName is not null && nfoResult.IsTitleLocked
            ? new ArchiveExpectedEpisodeMetadata(expectedTitle, parsedName.SeasonNumber, parsedName.EpisodeNumber, null)
            : null);
        var renameOperation = BuildRenameOperation(filePath, parsedName, renameMetadata);
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
            nfoResult.ProviderIds,
            nfoResult.NfoExists,
            issues,
            changeNotes,
            ErrorMessage: nfoResult.WarningMessage is null ? null : $"NFO konnte nicht sicher gelesen werden: {nfoResult.WarningMessage}",
            nfoResult.Title,
            nfoResult.SortTitle,
            nfoResult.IsTitleLocked,
            nfoResult.IsSortTitleLocked);
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

        var nfoResult = _nfoProviderIds.ReadEpisodeMetadata(filePath);
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
                ResolveExpectedTitleFromNfoAndTvdb(nfoResult, episode.Name),
                episode.SeasonNumber?.ToString("00") ?? parsedName.SeasonNumber,
                EpisodeFileNameHelper.IsEpisodeRange(parsedName.EpisodeNumber)
                    ? parsedName.EpisodeNumber
                    : episode.EpisodeNumber?.ToString("00") ?? parsedName.EpisodeNumber,
                mapping.OriginalLanguage);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException)
        {
            return null;
        }
    }

    internal static EpisodeMetadataGuess? TryBuildMetadataGuess(string filePath)
    {
        var parts = TryParseEpisodeFileName(filePath);
        return parts is null
            ? null
            : new EpisodeMetadataGuess(
                parts.SeriesName,
                parts.Title,
                parts.SeasonNumber,
                parts.EpisodeNumber);
    }

    internal static string ResolveExpectedTitleFromNfoAndTvdb(
        EmbyNfoMetadataReadResult nfoResult,
        string tvdbTitle)
    {
        return nfoResult.IsTitleLocked && !string.IsNullOrWhiteSpace(nfoResult.Title)
            ? nfoResult.Title.Trim()
            : tvdbTitle.Trim();
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
        var targetPath = BuildTargetMediaPath(filePath, expectedFileName);
        return CreateRenameOperation(filePath, targetPath);
    }

    private static bool ShouldRenamePath(string sourcePath, string targetPath)
    {
        return !string.Equals(
            NormalizePathForExactComparison(sourcePath),
            NormalizePathForExactComparison(targetPath),
            StringComparison.Ordinal);
    }

    private static string NormalizePathForExactComparison(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsCaseOnlyRename(string sourcePath, string targetPath)
    {
        return PathComparisonHelper.AreSamePath(sourcePath, targetPath)
            && ShouldRenamePath(sourcePath, targetPath);
    }

    private static bool TargetExistsAsDifferentFile(string sourcePath, string targetPath)
    {
        return PathComparisonHelper.FileExistsAsDifferentEntry(sourcePath, targetPath);
    }

    private static string CreateTemporaryCaseRenamePath(string directory)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = Path.Combine(directory, $".case-rename-{Guid.NewGuid():N}.tmp");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Es konnte kein temporärer Dateiname für die Groß-/Kleinschreibungs-Umbenennung erzeugt werden.");
    }

    private static void MoveFileWithCaseRenameSupport(string sourcePath, string targetPath)
    {
        if (!IsCaseOnlyRename(sourcePath, targetPath))
        {
            File.Move(sourcePath, targetPath);
            return;
        }

        // Windows-Dateisysteme behandeln den Zielpfad bei reinen Case-Renames oft als bereits vorhanden.
        // Der Zwischenschritt erzwingt deshalb eine echte Verzeichnisaktualisierung.
        var temporaryPath = CreateTemporaryCaseRenamePath(Path.GetDirectoryName(targetPath) ?? ".");
        File.Move(sourcePath, temporaryPath);
        try
        {
            File.Move(temporaryPath, targetPath);
        }
        catch
        {
            if (File.Exists(temporaryPath) && !File.Exists(sourcePath))
            {
                File.Move(temporaryPath, sourcePath);
            }

            throw;
        }
    }

    private static ArchiveRenameOperation? CreateRenameOperation(string sourcePath, string targetPath)
    {
        return ShouldRenamePath(sourcePath, targetPath)
            ? new ArchiveRenameOperation(sourcePath, targetPath, BuildSidecarRenameOperations(sourcePath, targetPath))
            : null;
    }

    private static IReadOnlyList<ArchiveSidecarRenameOperation> BuildSidecarRenameOperations(string sourceMediaPath, string targetMediaPath)
    {
        var sourceBasePath = Path.Combine(
            Path.GetDirectoryName(sourceMediaPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(sourceMediaPath));
        var targetBasePath = Path.Combine(
            Path.GetDirectoryName(targetMediaPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(targetMediaPath));
        var operations = SidecarSuffixes
            .Select(suffix => new ArchiveSidecarRenameOperation(sourceBasePath + suffix, targetBasePath + suffix))
            .Where(operation => File.Exists(operation.SourcePath))
            .ToList();
        var directory = Path.GetDirectoryName(sourceMediaPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            var sourceStem = Path.GetFileNameWithoutExtension(sourceMediaPath);
            foreach (var path in Directory.EnumerateFiles(directory, sourceStem + ".*", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(path);
                if (!fileName.StartsWith(sourceStem, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var suffix = fileName[sourceStem.Length..];
                if (SubtitleSidecarSuffixPattern.IsMatch(suffix))
                {
                    operations.Add(new ArchiveSidecarRenameOperation(path, targetBasePath + suffix));
                }
            }
        }

        return operations;
    }

    /// <summary>
    /// Baut eine sichere Umbenennungsoperation für einen manuell gesetzten Ziel-Dateinamen.
    /// Begleitdateien werden dabei nach denselben Regeln wie bei automatischen TVDB-Korrekturen mitgeführt.
    /// </summary>
    internal static ArchiveRenameOperation? BuildManualRenameOperation(string sourceMediaPath, string targetFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMediaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFileName);
        WindowsPathValidation.ValidateFileName(targetFileName.Trim());
        if (targetFileName.Trim().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(Path.GetExtension(targetFileName.Trim()), ".mkv", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Der Zielname muss ein einzelner gültiger MKV-Dateiname sein.", nameof(targetFileName));
        }

        var targetPath = BuildTargetMediaPath(sourceMediaPath, targetFileName.Trim());
        WindowsPathValidation.ValidateFilePath(targetPath);
        return CreateRenameOperation(sourceMediaPath, targetPath);
    }

    private static string BuildTargetMediaPath(string sourceMediaPath, string targetFileName)
    {
        var sourceDirectory = Path.GetDirectoryName(sourceMediaPath) ?? string.Empty;
        var targetDirectory = ResolveTargetDirectoryForEpisodeFileName(sourceDirectory, targetFileName);
        return Path.Combine(targetDirectory, targetFileName);
    }

    private static string ResolveTargetDirectoryForEpisodeFileName(string sourceDirectory, string targetFileName)
    {
        var targetParts = TryParseEpisodeFileName(targetFileName);
        if (targetParts is null)
        {
            return sourceDirectory;
        }

        var currentDirectoryName = Path.GetFileName(sourceDirectory);
        var parentDirectory = Path.GetDirectoryName(sourceDirectory);
        var seriesDirectory = IsSeasonDirectoryName(currentDirectoryName)
            ? parentDirectory
            : string.Equals(
                currentDirectoryName,
                EpisodeFileNameHelper.SanitizePathSegment(targetParts.SeriesName),
                StringComparison.OrdinalIgnoreCase)
                ? sourceDirectory
                : null;
        if (string.IsNullOrWhiteSpace(seriesDirectory))
        {
            return sourceDirectory;
        }

        return Path.Combine(seriesDirectory, BuildSeasonFolderName(targetParts.SeasonNumber));
    }

    private static bool IsSeasonDirectoryName(string directoryName)
    {
        return string.Equals(directoryName, "Specials", StringComparison.OrdinalIgnoreCase)
               || SeasonFolderPattern.IsMatch(directoryName);
    }

    private static string BuildSeasonFolderName(string seasonNumber)
    {
        var normalizedSeasonNumber = EpisodeFileNameHelper.NormalizeSeasonNumber(seasonNumber);
        if (normalizedSeasonNumber == "00")
        {
            return "Specials";
        }

        return int.TryParse(normalizedSeasonNumber, out var parsedSeason) && parsedSeason > 0
            ? $"Season {parsedSeason}"
            : "Season xx";
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

    private static void ValidateRename(ArchiveRenameOperation renameOperation)
    {
        WindowsPathValidation.ValidateFilePath(renameOperation.TargetPath);
        if (!File.Exists(renameOperation.SourcePath))
        {
            throw new FileNotFoundException("Die umzubenennende MKV wurde nicht gefunden.", renameOperation.SourcePath);
        }

        if (Directory.Exists(renameOperation.TargetPath)
            || TargetExistsAsDifferentFile(renameOperation.SourcePath, renameOperation.TargetPath))
        {
            throw new IOException($"Die Ziel-MKV existiert bereits: {renameOperation.TargetPath}");
        }

        foreach (var sidecar in renameOperation.Sidecars)
        {
            WindowsPathValidation.ValidateFilePath(sidecar.TargetPath);
            if (!File.Exists(sidecar.SourcePath))
            {
                throw new FileNotFoundException("Eine geplante Begleitdatei fehlt. Bitte neu scannen.", sidecar.SourcePath);
            }

            if (Directory.Exists(sidecar.TargetPath)
                || TargetExistsAsDifferentFile(sidecar.SourcePath, sidecar.TargetPath))
            {
                throw new IOException($"Eine Ziel-Begleitdatei existiert bereits: {sidecar.TargetPath}");
            }
        }
    }

    private static string ApplyRename(ArchiveRenameOperation renameOperation)
    {
        ValidateRename(renameOperation);

        Directory.CreateDirectory(Path.GetDirectoryName(renameOperation.TargetPath) ?? ".");
        foreach (var sidecarDirectory in renameOperation.Sidecars
                     .Select(sidecar => Path.GetDirectoryName(sidecar.TargetPath))
                     .Where(directory => !string.IsNullOrWhiteSpace(directory))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(sidecarDirectory!);
        }

        var movedFiles = new List<ArchiveSidecarRenameOperation>();
        try
        {
            // Die Gruppe wird nicht mitten im Rename abgebrochen: erst vollständig
            // verschieben oder rückwärts zurückrollen, damit MKV und NFO zusammenbleiben.
            foreach (var move in new[] { new ArchiveSidecarRenameOperation(renameOperation.SourcePath, renameOperation.TargetPath) }
                         .Concat(renameOperation.Sidecars))
            {
                if (!ShouldRenamePath(move.SourcePath, move.TargetPath))
                {
                    continue;
                }

                MoveFileWithCaseRenameSupport(move.SourcePath, move.TargetPath);
                movedFiles.Add(move);
            }
        }
        catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            var rollbackErrors = new List<string>();
            foreach (var move in movedFiles.AsEnumerable().Reverse())
            {
                try
                {
                    MoveFileWithCaseRenameSupport(move.TargetPath, move.SourcePath);
                }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    rollbackErrors.Add($"{move.TargetPath}: {rollbackError.Message}");
                }
            }

            if (rollbackErrors.Count > 0)
            {
                throw new IOException($"{moveError.Message} Rückverschieben unvollständig: {string.Join("; ", rollbackErrors)}", moveError);
            }

            throw;
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
    EmbyProviderIds ProviderIds,
    bool NfoExists,
    IReadOnlyList<ArchiveMaintenanceIssue> Issues,
    IReadOnlyList<string> ChangeNotes,
    string? ErrorMessage,
    string? NfoTitle = null,
    string? NfoSortTitle = null,
    bool NfoTitleLocked = false,
    bool NfoSortTitleLocked = false)
{
    public bool HasWritableChanges => ContainerTitleEdit is not null || TrackHeaderEdits.Count > 0 || RenameOperation is not null;

    public bool RequiresRemux => Issues.Any(issue => issue.Kind == ArchiveMaintenanceIssueKind.RemuxRequired);

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
}

internal sealed record ArchiveMaintenanceApplyRequest(
    string FilePath,
    ArchiveRenameOperation? RenameOperation,
    ContainerTitleEditOperation? ContainerTitleEdit,
    IReadOnlyList<TrackHeaderEditOperation> TrackHeaderEdits,
    ArchiveProviderIdEditOperation? ProviderIdEdit,
    ArchiveNfoTextEditOperation? NfoTextEdit = null)
{
    public bool HasWritableChanges => RenameOperation is not null
                                      || ContainerTitleEdit is not null
                                      || TrackHeaderEdits.Count > 0
                                      || ProviderIdEdit is not null
                                      || NfoTextEdit is not null;
}

internal sealed record ArchiveProviderIdEditOperation(
    EmbyProviderIds ProviderIds,
    bool RemoveImdbId);

internal sealed record ArchiveNfoTextEditOperation(
    string? CurrentTitle,
    string? ExpectedTitle,
    string? CurrentSortTitle,
    string? ExpectedSortTitle,
    bool CurrentTitleLocked,
    bool ExpectedTitleLocked,
    bool CurrentSortTitleLocked,
    bool ExpectedSortTitleLocked);

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
