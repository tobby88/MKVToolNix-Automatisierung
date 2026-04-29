using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Plant und führt das Einsortieren loser MediathekView-Downloads in Serienunterordner aus.
/// </summary>
/// <remarks>
/// Der Dienst arbeitet bewusst nur auf der obersten Ebene eines Downloadordners.
/// Bereits einsortierte Unterordner werden nur untersucht, wenn daraus sichere
/// Umbenennungen auf einen kanonischen Seriennamen abgeleitet werden können.
/// </remarks>
internal sealed class DownloadSortService
{
    private const double SignificantlyLargerExistingVideoRatio = 1.2;
    private const string DefectiveFolderName = "defekt";
    private const string DoneFolderName = "done";

    private static readonly HashSet<string> ReservedTargetFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        DefectiveFolderName,
        DoneFolderName
    };

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".txt",
        ".srt",
        ".ass",
        ".vtt",
        ".ttml"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4"
    };

    private static readonly HashSet<string> GenericSeriesLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "backstage",
        "der samstagskrimi",
        "doku",
        "dokus",
        "dokumentation",
        "dokumentationen",
        "fernsehfilm",
        "fernsehfilme",
        "filme",
        "hallo deutschland",
        "krimi",
        "krimis",
        "reportage",
        "reportagen",
        "riverboat"
    };

    private static readonly IReadOnlyList<DownloadSeriesAliasGroup> AliasGroups =
    [
        new(
            "Die Heiland",
            [
                "Die Heiland",
                "Die Heiland - Wir sind Anwalt"
            ]),
        new(
            "Ein Fall für Zwei",
            [
                "Ein Fall für Zwei",
                "Ein Fall für zwei"
            ]),
        new(
            "Der Kommissar und das Meer",
            [
                "Der Kommissar und das Meer",
                "Der Kommissar und der See"
            ]),
        new(
            "Marie Brand",
            [
                "Marie Brand"
            ]),
        new(
            "Ostfriesenkrimis",
            [
                "Ostfriesenkrimis",
                "Ostfrieslandkrimis"
            ]),
        new(
            "Pettersson und Findus",
            [
                "Pettersson und Findus"
            ]),
        new(
            "SOKO Leipzig",
            [
                "SOKO Leipzig"
            ])
    ];

    private static readonly IReadOnlyList<DownloadSpecialTitleFolderRule> SpecialTitleFolderRules =
    [
        new(
            "Die Mucklas",
            "Pettersson und Findus")
    ];

    /// <summary>
    /// Analysiert die lose Wurzelebene eines Downloadordners und liefert Sortiervorschläge.
    /// </summary>
    /// <param name="rootDirectory">Oberordner, in dessen Wurzel lose Mediathek-Dateien liegen.</param>
    /// <param name="onProgress">Optionaler Callback für laufende Scan-Fortschrittsmeldungen.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal für lange Ordner-Scans.</param>
    /// <returns>Scanergebnis inklusive Move-Kandidaten und sicherer Ordner-Umbenennungen.</returns>
    public DownloadSortScanResult Scan(
        string rootDirectory,
        Action<DownloadSortScanProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureDirectoryExists(rootDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        ReportScanProgress(onProgress, "Pruefe vorhandene Serienordner...", 5);
        var folderRenames = BuildFolderRenamePlans(rootDirectory, onProgress, cancellationToken);

        ReportScanProgress(onProgress, "Lese lose Download-Dateien...", 30);
        var rootGroups = EnumerateLogicalGroups(rootDirectory, cancellationToken);

        var candidates = new List<DownloadSortCandidate>();
        for (var index = 0; index < rootGroups.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = rootGroups[index];
            ReportScanProgress(
                onProgress,
                $"Analysiere Paket {index + 1}/{rootGroups.Count}: {group.DisplayName}",
                InterpolateProgress(35, 90, index, rootGroups.Count));

            candidates.AddRange(BuildCandidates(rootDirectory, group, folderRenames));
        }

        ReportScanProgress(onProgress, "Sortiere Vorschlaege...", 95);
        var sortedCandidates = candidates
            .OrderBy(candidate => string.IsNullOrWhiteSpace(candidate.SuggestedFolderName))
            .ThenBy(candidate => candidate.SuggestedFolderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReportScanProgress(onProgress, "Scan abgeschlossen.", 100);
        return new DownloadSortScanResult(sortedCandidates, folderRenames);
    }

    /// <summary>
    /// Bewertet einen Zielordner für einen bereits gescannten Dateigruppen-Kandidaten erneut.
    /// </summary>
    /// <param name="rootDirectory">Download-Wurzel, in der sich lose Dateien und Serienordner befinden.</param>
    /// <param name="filePaths">Dateien des Kandidaten.</param>
    /// <param name="targetFolderName">Aktuell gewaehlter Zielordnername.</param>
    /// <param name="folderRenames">Beim Scan ermittelte sichere Ordnerumbenennungen.</param>
    /// <param name="defectiveFilePaths">Optional bereits als defekt markierte Dateien, die bei der Bewertung nicht als regulärer Inhalt zählen sollen.</param>
    /// <returns>Aktueller Status mit Konflikt- oder Hinweistext.</returns>
    public DownloadSortTargetEvaluation EvaluateTarget(
        string rootDirectory,
        IReadOnlyList<string> filePaths,
        string? targetFolderName,
        IReadOnlyList<DownloadSortFolderRenamePlan> folderRenames,
        IReadOnlyList<string>? defectiveFilePaths = null)
    {
        EnsureDirectoryExists(rootDirectory);
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(folderRenames);

        var defectiveFilePathSet = CreateDefectiveFilePathSet(defectiveFilePaths);
        var regularFilePaths = filePaths
            .Where(path => !defectiveFilePathSet.Contains(path))
            .ToList();
        if (regularFilePaths.Count == 0)
        {
            return defectiveFilePathSet.Count == 0
                ? new DownloadSortTargetEvaluation(
                    DownloadSortItemState.NeedsReview,
                    "Zu diesem Eintrag wurden keine Dateien gefunden.")
                : new DownloadSortTargetEvaluation(
                    DownloadSortItemState.Defective,
                    string.Empty);
        }

        return EvaluateRegularTarget(rootDirectory, regularFilePaths, targetFolderName, folderRenames);
    }

    /// <summary>
    /// Kennzeichnet Zielordnernamen, die ausschließlich für interne Sonderpfade des
    /// Einsortierers reserviert sind und deshalb nicht als normaler Serienordner dienen dürfen.
    /// </summary>
    /// <param name="targetFolderName">Zu prüfender Zielordnername.</param>
    /// <returns><see langword="true"/>, wenn der Name für normale Einsortierziele gesperrt ist.</returns>
    internal static bool IsReservedTargetFolderName(string? targetFolderName)
    {
        return ReservedTargetFolderNames.Contains(NormalizeTargetFolderName(targetFolderName));
    }

    /// <summary>
    /// Bewertet ausschließlich die regulär einsortierbaren Dateien eines Pakets gegen den aktuell
    /// gewählten Zielordner. Defekt-Dateien werden davor bereits abgezogen.
    /// </summary>
    private static DownloadSortTargetEvaluation EvaluateRegularTarget(
        string rootDirectory,
        IReadOnlyList<string> filePaths,
        string? targetFolderName,
        IReadOnlyList<DownloadSortFolderRenamePlan> folderRenames)
    {
        var normalizedFolderName = NormalizeTargetFolderName(targetFolderName);
        if (string.IsNullOrWhiteSpace(normalizedFolderName))
        {
            return new DownloadSortTargetEvaluation(
                DownloadSortItemState.NeedsReview,
                "Kein Zielordner erkannt. Bitte pruefen.");
        }

        if (IsReservedTargetFolderName(normalizedFolderName))
        {
            return new DownloadSortTargetEvaluation(
                DownloadSortItemState.NeedsReview,
                $"Der Ordner '{normalizedFolderName}' ist fuer interne Workflow-Dateien reserviert. Bitte einen normalen Serienordner waehlen.");
        }

        var renamePlan = folderRenames.FirstOrDefault(plan =>
            string.Equals(plan.TargetFolderName, normalizedFolderName, StringComparison.OrdinalIgnoreCase));

        var effectiveExistingDirectory = Path.Combine(rootDirectory, normalizedFolderName);
        if (!Directory.Exists(effectiveExistingDirectory)
            && renamePlan is not null)
        {
            effectiveExistingDirectory = Path.Combine(rootDirectory, renamePlan.CurrentFolderName);
        }

        var replacementDecision = EvaluateExistingTargetFiles(filePaths, effectiveExistingDirectory);
        if (replacementDecision.BlockingConflict is { } blockingConflict)
        {
            return new DownloadSortTargetEvaluation(
                DownloadSortItemState.Conflict,
                BuildBlockingConflictNote(blockingConflict));
        }

        if (replacementDecision.ReplaceableConflicts.Count > 0)
        {
            return new DownloadSortTargetEvaluation(
                DownloadSortItemState.ReadyWithReplacement,
                MergeNotes(
                    BuildReplacementNote(replacementDecision.ReplaceableConflicts),
                    renamePlan is null ? string.Empty : BuildFolderRenameNote(renamePlan)));
        }

        if (renamePlan is not null)
        {
            return new DownloadSortTargetEvaluation(
                DownloadSortItemState.Ready,
                BuildFolderRenameNote(renamePlan));
        }

        return new DownloadSortTargetEvaluation(
            DownloadSortItemState.Ready,
            string.Empty);
    }

    /// <summary>
    /// Fuehrt die geplanten Ordnerumbenennungen und Dateiverschiebungen aus.
    /// </summary>
    /// <param name="rootDirectory">Wurzel des Downloadordners.</param>
    /// <param name="moveRequests">Ausgewählte Sortieroperationen.</param>
    /// <param name="folderRenames">Vorab auszuführende sichere Ordnerumbenennungen.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal zwischen einzelnen Gruppen.</param>
    /// <returns>Kompakte Ausführungszusammenfassung für GUI und Log.</returns>
    public DownloadSortApplyResult Apply(
        string rootDirectory,
        IReadOnlyList<DownloadSortMoveRequest> moveRequests,
        IReadOnlyList<DownloadSortFolderRenamePlan> folderRenames,
        CancellationToken cancellationToken = default)
    {
        EnsureDirectoryExists(rootDirectory);
        ArgumentNullException.ThrowIfNull(moveRequests);
        ArgumentNullException.ThrowIfNull(folderRenames);

        var logLines = new List<string>();
        var renamedFolderCount = 0;
        var movedGroupCount = 0;
        var movedFileCount = 0;
        var skippedGroupCount = 0;

        foreach (var renamePlan in folderRenames
                     .Where(plan => moveRequests.Any(request =>
                         string.Equals(
                             NormalizeTargetFolderName(request.TargetFolderName),
                             plan.TargetFolderName,
                             StringComparison.OrdinalIgnoreCase)))
                     .DistinctBy(plan => plan.CurrentFolderName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryBuildSafeRootChildPath(rootDirectory, renamePlan.CurrentFolderName, out var sourcePath)
                || !TryBuildSafeRootChildPath(rootDirectory, renamePlan.TargetFolderName, out var destinationPath)
                || IsReservedTargetFolderName(renamePlan.TargetFolderName))
            {
                logLines.Add($"UEBERSPRUNGEN: Ordner-Umbenennung '{renamePlan.CurrentFolderName}' -> '{renamePlan.TargetFolderName}' ist kein sicherer direkter Download-Unterordner.");
                continue;
            }

            if (!Directory.Exists(sourcePath))
            {
                continue;
            }

            var isCaseOnlyRename = IsCaseOnlyPathChange(sourcePath, destinationPath);
            if (!isCaseOnlyRename && Directory.Exists(destinationPath))
            {
                logLines.Add($"UEBERSPRUNGEN: Ordner '{renamePlan.CurrentFolderName}' wurde nicht umbenannt, weil '{renamePlan.TargetFolderName}' bereits existiert.");
                continue;
            }

            try
            {
                MoveDirectoryWithCaseOnlySupport(sourcePath, destinationPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logLines.Add($"FEHLER: Ordner '{renamePlan.CurrentFolderName}' konnte nicht umbenannt werden: {ex.Message}");
                continue;
            }

            renamedFolderCount++;
            logLines.Add($"ORDNER: '{renamePlan.CurrentFolderName}' -> '{renamePlan.TargetFolderName}'");
        }

        foreach (var request in moveRequests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryNormalizeDirectRootFilePaths(rootDirectory, request.FilePaths, out var safeFilePaths, out var unsafeFilePath))
            {
                skippedGroupCount++;
                logLines.Add($"UEBERSPRUNGEN: {request.DisplayName} -> Quelldatei '{Path.GetFileName(unsafeFilePath)}' liegt nicht direkt im gewaehlten Download-Ordner.");
                continue;
            }

            var defectiveFilePathSet = CreateDefectiveFilePathSet(request.DefectiveFilePaths);
            var regularFilePaths = safeFilePaths
                .Where(path => !defectiveFilePathSet.Contains(path))
                .ToList();
            var targetFolderName = NormalizeTargetFolderName(request.TargetFolderName);
            if (regularFilePaths.Count > 0 && string.IsNullOrWhiteSpace(targetFolderName))
            {
                skippedGroupCount++;
                logLines.Add($"UEBERSPRUNGEN: {request.DisplayName} -> kein gueltiger Zielordner.");
                continue;
            }

            if (regularFilePaths.Count > 0 && IsReservedTargetFolderName(targetFolderName))
            {
                skippedGroupCount++;
                logLines.Add($"UEBERSPRUNGEN: {request.DisplayName} -> Ordner '{targetFolderName}' ist fuer interne Workflow-Dateien reserviert.");
                continue;
            }

            var blockingLooseVideoPath = FindBlockingLooseVideoCompanion(rootDirectory, regularFilePaths);
            if (!string.IsNullOrWhiteSpace(blockingLooseVideoPath))
            {
                skippedGroupCount++;
                logLines.Add(
                    $"UEBERSPRUNGEN: {request.DisplayName} -> passende lose MP4 '{Path.GetFileName(blockingLooseVideoPath)}' liegt noch im Download-Ordner und wurde fuer diesen Eintrag nicht mit ausgewaehlt.");
                continue;
            }

            DownloadSortReplacementDecision replacementDecision;
            string? targetDirectory = null;
            if (regularFilePaths.Count > 0)
            {
                targetDirectory = Path.Combine(rootDirectory, targetFolderName);
                Directory.CreateDirectory(targetDirectory);
                replacementDecision = EvaluateExistingTargetFiles(regularFilePaths, targetDirectory);
            }
            else
            {
                replacementDecision = new DownloadSortReplacementDecision([], null);
            }

            if (replacementDecision.BlockingConflict is { } blockingConflict)
            {
                skippedGroupCount++;
                logLines.Add($"KONFLIKT: {request.DisplayName} -> {BuildBlockingConflictNote(blockingConflict)}");
                continue;
            }

            var defectiveFilePaths = request.FilePaths.Where(defectiveFilePathSet.Contains).ToList();
            var defectiveDirectory = Path.Combine(rootDirectory, DefectiveFolderName);
            var defectiveTargetConflict = FindExistingTargetFile(defectiveFilePaths, defectiveDirectory);
            if (!string.IsNullOrWhiteSpace(defectiveTargetConflict))
            {
                skippedGroupCount++;
                logLines.Add(
                    $"KONFLIKT: {request.DisplayName} -> Im Ordner '{DefectiveFolderName}' existiert bereits '{Path.GetFileName(defectiveTargetConflict)}'.");
                continue;
            }

            var groupMovedCount = 0;
            if (targetDirectory is not null)
            {
                groupMovedCount += MoveFiles(regularFilePaths, targetDirectory, targetFolderName, logLines);
            }

            if (defectiveFilePaths.Count > 0)
            {
                Directory.CreateDirectory(defectiveDirectory);
                groupMovedCount += MoveFiles(
                    defectiveFilePaths,
                    defectiveDirectory,
                    DefectiveFolderName,
                    logLines);
            }

            movedFileCount += groupMovedCount;

            if (groupMovedCount == 0)
            {
                skippedGroupCount++;
                logLines.Add($"UEBERSPRUNGEN: {request.DisplayName} -> keine Datei konnte verschoben werden.");
                continue;
            }

            movedGroupCount++;
            if (replacementDecision.ReplaceableConflicts.Count > 0)
            {
                logLines.Add($"ERSETZT: {request.DisplayName} -> {FormatConflictFileList(replacementDecision.ReplaceableConflicts)}");
            }

            if (regularFilePaths.Count > 0)
            {
                var operationLabel = string.Equals(targetFolderName, DefectiveFolderName, StringComparison.OrdinalIgnoreCase)
                    ? "DEFEKT"
                    : "SORTIERT";
                logLines.Add($"{operationLabel}: {request.DisplayName} -> {targetFolderName} ({regularFilePaths.Count} Datei(en))");
            }

            if (defectiveFilePathSet.Count > 0)
            {
                logLines.Add($"DEFEKT: {request.DisplayName} -> {DefectiveFolderName} ({defectiveFilePathSet.Count} Datei(en))");
            }
        }

        return new DownloadSortApplyResult(
            movedGroupCount,
            movedFileCount,
            renamedFolderCount,
            skippedGroupCount,
            logLines);
    }

    /// <summary>
    /// Verschiebt eine vorbereitete Teilmenge von Dateien in genau einen Zielordner und protokolliert
    /// einzelne Move-Fehler, ohne den restlichen Gruppenlauf abzubrechen.
    /// </summary>
    private static int MoveFiles(
        IReadOnlyList<string> filePaths,
        string targetDirectory,
        string targetFolderName,
        ICollection<string> logLines)
    {
        var movedCount = 0;
        foreach (var filePath in filePaths)
        {
            var destinationPath = Path.Combine(targetDirectory, Path.GetFileName(filePath));
            if (Path.GetFullPath(filePath).Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (MoveFileSafely(filePath, destinationPath, targetFolderName, logLines))
                {
                    movedCount++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logLines.Add($"FEHLER: '{Path.GetFileName(filePath)}' konnte nicht nach '{targetFolderName}' verschoben werden: {ex.Message}");
            }
        }

        return movedCount;
    }

    /// <summary>
    /// Verschiebt eine einzelne Datei und ersetzt gleichnamige Zieldateien nur, wenn deren
    /// Snapshot zwischen Konfliktprüfung und eigentlichem Ersetzen unverändert geblieben ist.
    /// </summary>
    private static bool MoveFileSafely(
        string sourcePath,
        string destinationPath,
        string targetFolderName,
        ICollection<string> logLines)
    {
        if (!File.Exists(sourcePath))
        {
            logLines.Add($"UEBERSPRUNGEN: '{Path.GetFileName(sourcePath)}' existiert nicht mehr. Bitte neu scannen.");
            return false;
        }

        if (!File.Exists(destinationPath))
        {
            File.Move(sourcePath, destinationPath, overwrite: false);
            return true;
        }

        var initialTargetSnapshot = FileStateSnapshot.TryCreate(destinationPath);
        if (initialTargetSnapshot is null)
        {
            File.Move(sourcePath, destinationPath, overwrite: false);
            return true;
        }

        if (!TryGetFileLength(sourcePath, out var sourceLengthBytes))
        {
            logLines.Add($"UEBERSPRUNGEN: '{Path.GetFileName(sourcePath)}' existiert nicht mehr. Bitte neu scannen.");
            return false;
        }

        var conflict = new DownloadSortTargetFileConflict(
            sourcePath,
            destinationPath,
            sourceLengthBytes,
            initialTargetSnapshot.Value.Length);
        if (ShouldKeepExistingTargetFile(conflict))
        {
            logLines.Add($"KONFLIKT: '{Path.GetFileName(sourcePath)}' wurde nicht nach '{targetFolderName}' verschoben: {BuildBlockingConflictNote(conflict)}");
            return false;
        }

        var temporaryPath = CreateTemporaryReplacementPath(destinationPath);
        File.Move(sourcePath, temporaryPath, overwrite: false);
        try
        {
            var latestTargetSnapshot = FileStateSnapshot.TryCreate(destinationPath);
            if (!initialTargetSnapshot.Equals(latestTargetSnapshot))
            {
                File.Move(temporaryPath, sourcePath, overwrite: false);
                logLines.Add($"KONFLIKT: '{Path.GetFileName(sourcePath)}' wurde nicht nach '{targetFolderName}' verschoben, weil sich die Zieldatei während des Sortierens geändert hat.");
                return false;
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return true;
        }
        catch
        {
            if (File.Exists(temporaryPath) && !File.Exists(sourcePath))
            {
                File.Move(temporaryPath, sourcePath, overwrite: false);
            }

            throw;
        }
    }

    /// <summary>
    /// Erzeugt einen versteckten temporären Pfad direkt im Zielordner, damit das spätere
    /// Ersetzen auf demselben Volume bleibt und nicht durch Cross-Volume-Moves überrascht wird.
    /// </summary>
    private static string CreateTemporaryReplacementPath(string destinationPath)
    {
        var targetDirectory = Path.GetDirectoryName(destinationPath) ?? ".";
        var targetFileName = Path.GetFileName(destinationPath);
        for (var index = 0; index < 10_000; index++)
        {
            var candidate = Path.Combine(targetDirectory, $".{targetFileName}.replace-{Guid.NewGuid():N}.tmp");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Es konnte kein temporärer Ersatzpfad für '{targetFileName}' erzeugt werden.");
    }

    private static string? FindExistingTargetFile(IReadOnlyList<string> filePaths, string targetDirectory)
    {
        foreach (var filePath in filePaths)
        {
            var destinationPath = Path.Combine(targetDirectory, Path.GetFileName(filePath));
            if (PathComparisonHelper.AreSamePath(filePath, destinationPath))
            {
                continue;
            }

            if (File.Exists(destinationPath))
            {
                return destinationPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Verhindert, dass reine Sidecar-Einträge TXT-/Untertiteldateien still einsortieren, obwohl
    /// die zugehörige lose MP4 derselben Basis noch separat im Download-Root liegt. Das schützt
    /// sowohl abgewählte Episoden als auch Dateien, die erst nach dem Scan vollständig erschienen sind.
    /// Defekte MP4-Dateien bleiben davon bewusst ausgenommen, damit deren gesunde Begleiter weiterhin
    /// regulär einsortiert werden können.
    /// </summary>
    private static string? FindBlockingLooseVideoCompanion(
        string rootDirectory,
        IReadOnlyList<string> filePaths)
    {
        if (filePaths.Any(path => VideoExtensions.Contains(Path.GetExtension(path))))
        {
            return null;
        }

        foreach (var filePath in filePaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var companionVideoPath = BuildLogicalVideoCompanionPath(filePath);
            if (!File.Exists(companionVideoPath)
                || filePaths.Contains(companionVideoPath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!PathComparisonHelper.IsPathWithinRoot(companionVideoPath, rootDirectory))
            {
                continue;
            }

            var health = MediaFileHealth.CheckMp4File(
                companionVideoPath,
                CompanionTextMetadataReader.ReadDetailed(Path.ChangeExtension(companionVideoPath, ".txt")));
            if (health.IsUsable)
            {
                return companionVideoPath;
            }
        }

        return null;
    }

    private IReadOnlyList<DownloadSortCandidate> BuildCandidates(
        string rootDirectory,
        DownloadFileGroup group,
        IReadOnlyList<DownloadSortFolderRenamePlan> folderRenames)
    {
        var defectiveVideoCandidates = group.FilePaths
            .Where(path => VideoExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new
            {
                FilePath = path,
                Health = MediaFileHealth.CheckMp4File(path, CompanionTextMetadataReader.ReadDetailed(Path.ChangeExtension(path, ".txt")))
            })
            .Where(candidate => !candidate.Health.IsUsable)
            .ToList();

        if (defectiveVideoCandidates.Count == 0)
        {
            return [BuildCandidate(rootDirectory, group.DisplayName, group.FilePaths, folderRenames)];
        }

        var defectiveFilePaths = defectiveVideoCandidates
            .Select(candidate => candidate.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remainingFilePaths = group.FilePaths
            .Except(defectiveFilePaths, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Ein einzelnes MP4+TXT-Paar ohne weitere Dateien bildet keinen nutzbaren
        // Serienkandidaten mehr. In diesem Sonderfall darf die TXT direkt mit der
        // defekten MP4 im Defekt-Ordner landen.
        if (ShouldRouteOnlyTxtCompanionToDefective(group.FilePaths, defectiveFilePaths))
        {
            var textCompanionPath = Path.ChangeExtension(defectiveVideoCandidates[0].FilePath, ".txt");
            if (remainingFilePaths.Remove(textCompanionPath))
            {
                defectiveFilePaths.Add(textCompanionPath);
            }
        }

        var seriesCandidate = remainingFilePaths.Count == 0
            ? null
            : BuildCandidate(rootDirectory, group.DisplayName, remainingFilePaths, folderRenames);
        var targetFolderName = seriesCandidate?.SuggestedFolderName ?? DefectiveFolderName;
        var orderedDefectiveFilePaths = defectiveFilePaths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var evaluation = EvaluateTarget(
            rootDirectory,
            group.FilePaths,
            targetFolderName,
            folderRenames,
            orderedDefectiveFilePaths);
        var note = BuildDefectiveCandidateNote(
            defectiveVideoCandidates.Select(candidate => candidate.Health.Reason).ToList(),
            remainingFilePaths,
            targetFolderName,
            defectiveFilePaths);
        var persistentNote = MergeNotes(seriesCandidate?.PersistentNote ?? string.Empty, note);

        return
        [
            new DownloadSortCandidate(
                group.DisplayName,
                group.FilePaths,
                seriesCandidate?.DetectedSeriesName,
                targetFolderName,
                evaluation.State,
                MergeNotes(persistentNote, evaluation.Note),
                IsInitiallySelected: DownloadSortItemStates.IsSortable(evaluation.State),
                DefectiveFilePaths: orderedDefectiveFilePaths,
                PersistentNote: persistentNote,
                ContainsDefectiveFiles: true)
        ];
    }

    private DownloadSortCandidate BuildCandidate(
        string rootDirectory,
        string displayName,
        IReadOnlyList<string> filePaths,
        IReadOnlyList<DownloadSortFolderRenamePlan> folderRenames)
    {
        var proposal = DetectFolderProposal(filePaths);
        var evaluation = EvaluateTarget(rootDirectory, filePaths, proposal.SuggestedFolderName, folderRenames);
        var note = MergeNotes(proposal.Note, evaluation.Note);

        return new DownloadSortCandidate(
            displayName,
            filePaths,
            proposal.DetectedSeriesName,
            proposal.SuggestedFolderName,
            evaluation.State,
            note,
            IsInitiallySelected: DownloadSortItemStates.IsSortable(evaluation.State),
            PersistentNote: proposal.Note);
    }

    private static bool ShouldRouteOnlyTxtCompanionToDefective(
        IReadOnlyList<string> groupFilePaths,
        IReadOnlyCollection<string> defectiveVideoPaths)
    {
        if (defectiveVideoPaths.Count != 1 || groupFilePaths.Count != 2)
        {
            return false;
        }

        var defectiveVideoPath = defectiveVideoPaths.Single();
        var textCompanionPath = Path.ChangeExtension(defectiveVideoPath, ".txt");
        return groupFilePaths.Any(path => string.Equals(path, textCompanionPath, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildDefectiveCandidateNote(
        IReadOnlyList<string?> reasons,
        IReadOnlyList<string> remainingFilePaths,
        string targetFolderName,
        IReadOnlySet<string> defectiveFilePaths)
    {
        var firstReason = reasons.FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
        var baseText = string.IsNullOrWhiteSpace(firstReason)
            ? "MP4 wirkt defekt oder unvollständig."
            : firstReason;

        if (remainingFilePaths.Count == 0)
        {
            return $"{baseText} Alle zugehörigen Dateien werden gemeinsam in den Defekt-Ordner verschoben.";
        }

        var routedCompanionNames = defectiveFilePaths
            .Where(path => !VideoExtensions.Contains(Path.GetExtension(path)))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList();
        var companionNote = routedCompanionNames.Count == 0
            ? string.Empty
            : $" {FormatFileNameList(routedCompanionNames)} geht zusammen mit der defekten MP4 in den Defekt-Ordner.";

        return $"{baseText} Die betroffene MP4 wird in den Defekt-Ordner verschoben, nutzbare Begleitdateien nach '{targetFolderName}'.{companionNote}";
    }

    private static string FormatFileNameList(IReadOnlyList<string> fileNames)
    {
        return fileNames.Count switch
        {
            0 => "Keine Datei",
            1 => $"'{fileNames[0]}'",
            2 => $"'{fileNames[0]}' und '{fileNames[1]}'",
            _ => string.Join(", ", fileNames.Take(fileNames.Count - 1).Select(name => $"'{name}'")) + $" und '{fileNames[^1]}'"
        };
    }

    private static IReadOnlyList<DownloadSortFolderRenamePlan> BuildFolderRenamePlans(
        string rootDirectory,
        Action<DownloadSortScanProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        var directoryPaths = Directory.GetDirectories(rootDirectory);
        var existingDirectoryNames = directoryPaths
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var renameCandidates = new List<DownloadSortFolderRenamePlan>();

        for (var index = 0; index < directoryPaths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryPath = directoryPaths[index];
            var currentFolderName = Path.GetFileName(directoryPath);
            if (string.IsNullOrWhiteSpace(currentFolderName))
            {
                continue;
            }

            ReportScanProgress(
                onProgress,
                $"Pruefe Serienordner {index + 1}/{directoryPaths.Length}: {currentFolderName}",
                InterpolateProgress(8, 28, index, Math.Max(1, directoryPaths.Length)));

            if (IsReservedTargetFolderName(currentFolderName))
            {
                continue;
            }

            var detectedFolderNames = EnumerateLogicalGroups(directoryPath, cancellationToken)
                .Select(group => DetectFolderProposal(group.FilePaths).SuggestedFolderName)
                .Where(folderName => !string.IsNullOrWhiteSpace(folderName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (detectedFolderNames.Count != 1)
            {
                continue;
            }

            var targetFolderName = detectedFolderNames[0];
            var matchesTargetIgnoringCase = string.Equals(currentFolderName, targetFolderName, StringComparison.OrdinalIgnoreCase);
            if (string.Equals(currentFolderName, targetFolderName, StringComparison.Ordinal)
                || (!matchesTargetIgnoringCase && existingDirectoryNames.Contains(targetFolderName!)))
            {
                continue;
            }

            renameCandidates.Add(new DownloadSortFolderRenamePlan(
                currentFolderName,
                targetFolderName!,
                $"Ordnerinhalt passt fachlich zu '{targetFolderName}'."));
        }

        return renameCandidates
            .GroupBy(candidate => candidate.TargetFolderName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .OrderBy(plan => plan.CurrentFolderName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int InterpolateProgress(int startPercent, int endPercent, int index, int totalCount)
    {
        if (totalCount <= 0)
        {
            return endPercent;
        }

        var ratio = Math.Clamp(index / (double)totalCount, 0, 1);
        return (int)Math.Round(startPercent + ((endPercent - startPercent) * ratio));
    }

    private static void ReportScanProgress(
        Action<DownloadSortScanProgress>? onProgress,
        string statusText,
        int progressPercent)
    {
        onProgress?.Invoke(new DownloadSortScanProgress(
            statusText,
            Math.Clamp(progressPercent, 0, 100)));
    }

    private static DownloadFolderProposal DetectFolderProposal(IReadOnlyList<string> filePaths)
    {
        var representativePath = SelectRepresentativePath(filePaths);
        var textFilePath = filePaths.FirstOrDefault(path => path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        var textDetails = CompanionTextMetadataReader.ReadDetailed(textFilePath);
        var fileParts = ParseDownloadFileName(representativePath);

        var exactCandidates = new (string? Value, string Note)[]
        {
            (textDetails.Topic, "Serienordner aus TXT-Thema abgeleitet."),
            (fileParts.SeriesPrefix, "Serienordner aus Dateiname abgeleitet.")
        };

        foreach (var candidate in exactCandidates)
        {
            if (!TryResolveSpecificSeries(candidate.Value, out var resolvedSeriesName, out var aliasApplied))
            {
                continue;
            }

            return new DownloadFolderProposal(
                resolvedSeriesName,
                resolvedSeriesName,
                aliasApplied
                    ? $"{candidate.Note} Alias wurde auf den kanonischen Ordnernamen vereinheitlicht."
                    : candidate.Note);
        }

        foreach (var fragment in new[] { textDetails.Title, fileParts.Remainder, Path.GetFileNameWithoutExtension(representativePath) })
        {
            if (!TryResolveSeriesFromTitleFragment(fragment, out var resolvedSeriesName))
            {
                continue;
            }

            return new DownloadFolderProposal(
                resolvedSeriesName,
                resolvedSeriesName,
                "Serienordner aus einem bekannten Alias im Titel abgeleitet.");
        }

        return new DownloadFolderProposal(
            null,
            string.Empty,
            "Serienordner konnte nicht sicher erkannt werden.");
    }

    private static bool TryResolveSpecificSeries(string? candidate, out string resolvedSeriesName, out bool aliasApplied)
    {
        aliasApplied = false;
        resolvedSeriesName = string.Empty;

        var normalizedCandidate = NormalizeSeriesCandidate(candidate);
        if (string.IsNullOrWhiteSpace(normalizedCandidate))
        {
            return false;
        }

        if (IsGenericSeriesLabel(normalizedCandidate))
        {
            return false;
        }

        if (TryResolveExactAlias(normalizedCandidate, out var canonicalSeriesName))
        {
            resolvedSeriesName = canonicalSeriesName;
            aliasApplied = !string.Equals(normalizedCandidate, canonicalSeriesName, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        if (TryResolveContainedAliasPrefix(normalizedCandidate, out canonicalSeriesName))
        {
            resolvedSeriesName = canonicalSeriesName;
            aliasApplied = true;
            return true;
        }

        resolvedSeriesName = normalizedCandidate;
        return true;
    }

    private static bool TryResolveSeriesFromTitleFragment(string? fragment, out string resolvedSeriesName)
    {
        resolvedSeriesName = string.Empty;
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return false;
        }

        var normalizedFragment = EpisodeMetadataMatchingHeuristics.NormalizeText(fragment);
        if (string.IsNullOrWhiteSpace(normalizedFragment))
        {
            return false;
        }

        if (TryResolveSpecialTitleFolderRule(normalizedFragment, out resolvedSeriesName))
        {
            return true;
        }

        var match = AliasGroups
            .SelectMany(group => group.Aliases.Select(alias => new
            {
                group.CanonicalFolderName,
                Alias = alias,
                NormalizedAlias = EpisodeMetadataMatchingHeuristics.NormalizeText(alias)
            }))
            .Where(entry => IsContainedSeriesAliasMatch(fragment, normalizedFragment, entry.Alias, entry.NormalizedAlias))
            .OrderByDescending(entry => entry.NormalizedAlias.Length)
            .FirstOrDefault();

        if (match is null)
        {
            return false;
        }

        resolvedSeriesName = match.CanonicalFolderName;
        return true;
    }

    private static bool TryResolveSpecialTitleFolderRule(string normalizedFragment, out string targetFolderName)
    {
        foreach (var rule in SpecialTitleFolderRules)
        {
            var normalizedMarker = EpisodeMetadataMatchingHeuristics.NormalizeText(rule.TitleMarker);
            if (string.IsNullOrWhiteSpace(normalizedMarker))
            {
                continue;
            }

            if (normalizedFragment.StartsWith(normalizedMarker + " ", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedFragment, normalizedMarker, StringComparison.OrdinalIgnoreCase))
            {
                targetFolderName = rule.TargetFolderName;
                return true;
            }
        }

        targetFolderName = string.Empty;
        return false;
    }

    private static bool IsContainedSeriesAliasMatch(
        string fragment,
        string normalizedFragment,
        string alias,
        string normalizedAlias)
    {
        if (string.IsNullOrWhiteSpace(normalizedAlias))
        {
            return false;
        }

        // Generische Mediathek-Rubriken wie "Filme" dürfen nur dann aus dem Titel
        // auf eine konkrete Serie fallen, wenn die Serie am Anfang steht oder im
        // redaktionellen Text klar markiert ist. Bekannte Sondertitel werden separat
        // behandelt; blosse beilaeufige Erwaehnungen sollen dagegen keine automatische
        // Zielordnerentscheidung ausloesen.
        if (normalizedFragment.StartsWith(normalizedAlias + " ", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedFragment, normalizedAlias, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var escapedAlias = Regex.Escape(alias);
        return Regex.IsMatch(
            fragment,
            $@"[""'\u201E\u201C_]\s*{escapedAlias}\s*[""'\u201C\u201D_]",
            RegexOptions.IgnoreCase);
    }

    private static bool TryResolveExactAlias(string candidate, out string canonicalSeriesName)
    {
        var normalizedCandidate = EpisodeMetadataMatchingHeuristics.NormalizeText(candidate);
        foreach (var group in AliasGroups)
        {
            if (group.Aliases.Any(alias =>
                    string.Equals(
                        EpisodeMetadataMatchingHeuristics.NormalizeText(alias),
                        normalizedCandidate,
                        StringComparison.OrdinalIgnoreCase)))
            {
                canonicalSeriesName = group.CanonicalFolderName;
                return true;
            }
        }

        canonicalSeriesName = string.Empty;
        return false;
    }

    private static bool TryResolveContainedAliasPrefix(string candidate, out string canonicalSeriesName)
    {
        var normalizedCandidate = EpisodeMetadataMatchingHeuristics.NormalizeText(candidate);
        foreach (var group in AliasGroups)
        {
            foreach (var alias in group.Aliases)
            {
                var normalizedAlias = EpisodeMetadataMatchingHeuristics.NormalizeText(alias);
                if (string.IsNullOrWhiteSpace(normalizedAlias))
                {
                    continue;
                }

                if (normalizedCandidate.StartsWith(normalizedAlias + " ", StringComparison.OrdinalIgnoreCase))
                {
                    canonicalSeriesName = group.CanonicalFolderName;
                    return true;
                }
            }
        }

        canonicalSeriesName = string.Empty;
        return false;
    }

    private static string NormalizeSeriesCandidate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = SeriesEpisodeMuxPlanner.NormalizeSeparators(value);
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"^\s*Der Samstagskrimi\s*[-:]\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"^\s*Filme\s*[-:]\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"^\s*Backstage\s*[-:]\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"^\s*Riverboat\s*[-:]\s*", string.Empty, RegexOptions.IgnoreCase);
        return normalized.Trim();
    }

    private static bool IsGenericSeriesLabel(string value)
    {
        return GenericSeriesLabels.Contains(EpisodeMetadataMatchingHeuristics.NormalizeText(value));
    }

    private static ParsedDownloadName ParseDownloadFileName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var withoutId = Regex.Replace(fileName, @"-\d+$", string.Empty);
        var separatorIndex = withoutId.IndexOf('-', StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return new ParsedDownloadName(
                NormalizeSeriesCandidate(withoutId),
                SeriesEpisodeMuxPlanner.NormalizeEpisodeTitle(withoutId));
        }

        var seriesPrefix = NormalizeSeriesCandidate(withoutId[..separatorIndex]);
        var remainder = withoutId[(separatorIndex + 1)..].Trim();
        return new ParsedDownloadName(
            seriesPrefix,
            SeriesEpisodeMuxPlanner.NormalizeSeparators(remainder));
    }

    private static IReadOnlyList<DownloadFileGroup> EnumerateLogicalGroups(
        string directoryPath,
        CancellationToken cancellationToken)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in Directory.EnumerateFiles(directoryPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSupportedSortFile(filePath))
            {
                continue;
            }

            var groupKey = BuildLogicalSortGroupKey(filePath);
            if (!groups.TryGetValue(groupKey, out var groupFilePaths))
            {
                groupFilePaths = [];
                groups.Add(groupKey, groupFilePaths);
            }

            groupFilePaths.Add(filePath);
        }

        return groups
            .Select(group => new DownloadFileGroup(
                BuildGroupDisplayName(group.Key),
                group.Value
                    .OrderBy(GetExtensionPriority)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .ToList();
    }

    private static bool IsSupportedSortFile(string filePath)
    {
        return SupportedExtensions.Contains(Path.GetExtension(filePath));
    }

    private static string BuildLogicalSortGroupKey(string filePath)
    {
        var key = Path.GetFileNameWithoutExtension(filePath);
        if (VideoExtensions.Contains(Path.GetExtension(filePath)))
        {
            return key;
        }

        return StripSidecarLanguageSuffixes(key);
    }

    private static string BuildLogicalVideoCompanionPath(string sidecarPath)
    {
        var directory = Path.GetDirectoryName(sidecarPath) ?? string.Empty;
        var logicalStem = StripSidecarLanguageSuffixes(Path.GetFileNameWithoutExtension(sidecarPath));
        return Path.Combine(directory, logicalStem + ".mp4");
    }

    private static string StripSidecarLanguageSuffixes(string stem)
    {
        var normalized = stem;
        while (true)
        {
            var stripped = Regex.Replace(
                normalized,
                @"\.(?:de|deu|ger|en|eng|nds|fr|fra|fre|es|spa|it|ita|nl|nld|dut|sv|swe|da|dan|no|nor|fi|fin|pl|pol|pt|por|tr|tur|forced|sdh|cc|hoh|hi)$",
                string.Empty,
                RegexOptions.IgnoreCase);
            if (string.Equals(stripped, normalized, StringComparison.Ordinal))
            {
                return normalized;
            }

            normalized = stripped;
        }
    }

    private static int GetExtensionPriority(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".mp4" => 0,
            ".txt" => 1,
            ".srt" => 2,
            ".ass" => 3,
            ".vtt" => 4,
            ".ttml" => 5,
            _ => 9
        };
    }

    private static string SelectRepresentativePath(IReadOnlyList<string> filePaths)
    {
        return filePaths
            .OrderBy(GetExtensionPriority)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static string BuildGroupDisplayName(string groupKey)
    {
        var withoutId = Regex.Replace(groupKey, @"-\d+$", string.Empty);
        return SeriesEpisodeMuxPlanner.NormalizeSeparators(withoutId);
    }

    private static string NormalizeTargetFolderName(string? targetFolderName)
    {
        if (string.IsNullOrWhiteSpace(targetFolderName))
        {
            return string.Empty;
        }

        return EpisodeFileNameHelper.SanitizePathSegment(targetFolderName.Trim());
    }

    private static bool TryBuildSafeRootChildPath(string rootDirectory, string folderName, out string childPath)
    {
        childPath = string.Empty;
        var normalizedFolderName = NormalizeTargetFolderName(folderName);
        if (string.IsNullOrWhiteSpace(normalizedFolderName)
            || !string.Equals(normalizedFolderName, folderName.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        var rootPath = Path.GetFullPath(rootDirectory);
        var candidatePath = Path.GetFullPath(Path.Combine(rootPath, normalizedFolderName));
        if (!PathComparisonHelper.IsPathWithinRoot(candidatePath, rootPath)
            || Path.GetDirectoryName(candidatePath) is not { } parentPath
            || !PathComparisonHelper.AreSamePath(parentPath, rootPath))
        {
            return false;
        }

        childPath = candidatePath;
        return true;
    }

    /// <summary>
    /// Benennt Ordner auch dann zuverlässig um, wenn sich auf Windows nur die Schreibweise ändert.
    /// </summary>
    private static void MoveDirectoryWithCaseOnlySupport(string sourcePath, string destinationPath)
    {
        if (!IsCaseOnlyPathChange(sourcePath, destinationPath))
        {
            Directory.Move(sourcePath, destinationPath);
            return;
        }

        var temporaryPath = CreateTemporaryDirectoryRenamePath(sourcePath);
        Directory.Move(sourcePath, temporaryPath);
        try
        {
            Directory.Move(temporaryPath, destinationPath);
        }
        catch
        {
            if (Directory.Exists(temporaryPath) && !Directory.Exists(sourcePath))
            {
                Directory.Move(temporaryPath, sourcePath);
            }

            throw;
        }
    }

    private static bool IsCaseOnlyPathChange(string sourcePath, string destinationPath)
    {
        return PathComparisonHelper.AreSamePath(sourcePath, destinationPath)
            && !string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(destinationPath),
                StringComparison.Ordinal);
    }

    private static string CreateTemporaryDirectoryRenamePath(string sourcePath)
    {
        var parentDirectory = Path.GetDirectoryName(sourcePath) ?? ".";
        var folderName = Path.GetFileName(sourcePath);
        for (var index = 0; index < 10_000; index++)
        {
            var candidate = Path.Combine(parentDirectory, $".{folderName}.rename-{Guid.NewGuid():N}.tmp");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Es konnte kein temporärer Umbenennungspfad für '{folderName}' erzeugt werden.");
    }

    /// <summary>
    /// Begrenzung für Apply-Aufrufe aus der UI: Einsortieren verschiebt nur lose Dateien direkt
    /// aus dem gewählten Downloadordner, nicht aus fremden oder bereits einsortierten Ordnern.
    /// </summary>
    private static bool TryNormalizeDirectRootFilePaths(
        string rootDirectory,
        IReadOnlyList<string> filePaths,
        out List<string> normalizedFilePaths,
        out string unsafeFilePath)
    {
        normalizedFilePaths = [];
        unsafeFilePath = string.Empty;
        var rootPath = Path.GetFullPath(rootDirectory);
        foreach (var filePath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                unsafeFilePath = filePath ?? string.Empty;
                return false;
            }

            if (!TryGetFullPath(filePath, out var fullPath))
            {
                unsafeFilePath = filePath;
                return false;
            }

            if (!PathComparisonHelper.IsPathWithinRoot(fullPath, rootPath)
                || Path.GetDirectoryName(fullPath) is not { } parentPath
                || !PathComparisonHelper.AreSamePath(parentPath, rootPath)
                || string.IsNullOrWhiteSpace(Path.GetFileName(fullPath)))
            {
                unsafeFilePath = filePath;
                return false;
            }

            normalizedFilePaths.Add(fullPath);
        }

        return true;
    }

    private static bool TryGetFullPath(string filePath, out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(filePath);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            fullPath = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Normalisiert die optionale Defekt-Teilmengenliste in ein case-insensitives Lookup, damit
    /// Bewertung und Ausführung reguläre und defekte Dateien konsistent voneinander trennen.
    /// </summary>
    private static HashSet<string> CreateDefectiveFilePathSet(IReadOnlyList<string>? defectiveFilePaths)
    {
        return (defectiveFilePaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => TryGetFullPath(path, out var fullPath) ? fullPath : path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static DownloadSortReplacementDecision EvaluateExistingTargetFiles(
        IReadOnlyList<string> filePaths,
        string targetDirectory)
    {
        if (!Directory.Exists(targetDirectory))
        {
            return DownloadSortReplacementDecision.Empty;
        }

        var replaceableConflicts = new List<DownloadSortTargetFileConflict>();
        foreach (var filePath in filePaths)
        {
            var destinationPath = Path.Combine(targetDirectory, Path.GetFileName(filePath));
            if (File.Exists(destinationPath)
                && !Path.GetFullPath(filePath).Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetFileLength(destinationPath, out var targetLengthBytes))
                {
                    continue;
                }

                if (!TryGetFileLength(filePath, out var sourceLengthBytes))
                {
                    return new DownloadSortReplacementDecision(
                        replaceableConflicts,
                        new DownloadSortTargetFileConflict(
                            filePath,
                            destinationPath,
                            SourceLengthBytes: 0,
                            targetLengthBytes,
                            SourceMissing: true));
                }

                var conflict = new DownloadSortTargetFileConflict(
                    filePath,
                    destinationPath,
                    sourceLengthBytes,
                    targetLengthBytes);

                if (ShouldKeepExistingTargetFile(conflict))
                {
                    return new DownloadSortReplacementDecision(replaceableConflicts, conflict);
                }

                replaceableConflicts.Add(conflict);
            }
        }

        return new DownloadSortReplacementDecision(replaceableConflicts, null);
    }

    private static bool TryGetFileLength(string filePath, out long lengthBytes)
    {
        try
        {
            lengthBytes = new FileInfo(filePath).Length;
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            lengthBytes = 0;
            return false;
        }
    }

    /// <summary>
    /// Schützt vorhandene Zielvideos nur dann vor dem Standard-Overwrite, wenn ein billiges,
    /// lokales Signal klar gegen die neue Datei spricht. Eine echte Auflösungsprüfung wäre hier
    /// unnötig teuer; bei MediathekView-Downloads ist eine deutlich größere Videodatei der
    /// pragmatische Proxy für möglicherweise höhere Qualität oder bessere Auflösung.
    /// </summary>
    private static bool ShouldKeepExistingTargetFile(DownloadSortTargetFileConflict conflict)
    {
        if (conflict.SourceMissing)
        {
            return true;
        }

        if (!VideoExtensions.Contains(Path.GetExtension(conflict.SourcePath)))
        {
            return false;
        }

        if (conflict.SourceLengthBytes <= 0)
        {
            return conflict.TargetLengthBytes > 0;
        }

        return conflict.TargetLengthBytes >= conflict.SourceLengthBytes * SignificantlyLargerExistingVideoRatio;
    }

    private static string BuildReplacementNote(IReadOnlyList<DownloadSortTargetFileConflict> conflicts)
    {
        return conflicts.Count == 1
            ? "Gleichnamige Zieldatei wird ersetzt."
            : "Gleichnamige Zieldateien werden ersetzt.";
    }

    private static string BuildFolderRenameNote(DownloadSortFolderRenamePlan renamePlan)
    {
        return $"Vorhandener Serienordner wird vorab von '{renamePlan.CurrentFolderName}' nach '{renamePlan.TargetFolderName}' vereinheitlicht.";
    }

    private static string BuildBlockingConflictNote(DownloadSortTargetFileConflict conflict)
    {
        if (conflict.SourceMissing)
        {
            return $"Quelldatei '{Path.GetFileName(conflict.SourcePath)}' existiert nicht mehr. Bitte neu scannen.";
        }

        return $"Vorhandene Zieldatei '{Path.GetFileName(conflict.TargetPath)}' ist deutlich größer ({FormatFileSize(conflict.TargetLengthBytes)} statt {FormatFileSize(conflict.SourceLengthBytes)}). Bitte prüfen.";
    }

    private static string FormatConflictFileList(IReadOnlyList<DownloadSortTargetFileConflict> conflicts)
    {
        var shownNames = conflicts
            .Take(3)
            .Select(conflict => Path.GetFileName(conflict.TargetPath))
            .ToList();
        var suffix = conflicts.Count > shownNames.Count
            ? $" und {conflicts.Count - shownNames.Count} weitere"
            : string.Empty;
        return string.Join(", ", shownNames) + suffix;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["Bytes", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private static string MergeNotes(string left, string right)
    {
        return string.IsNullOrWhiteSpace(left)
            ? right
            : string.IsNullOrWhiteSpace(right)
                ? left
                : left == right
                    ? left
                    : $"{left} {right}";
    }

    private static void EnsureDirectoryExists(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Download-Ordner nicht gefunden: {directoryPath}");
        }
    }

    private sealed record DownloadSeriesAliasGroup(
        string CanonicalFolderName,
        IReadOnlyList<string> Aliases);

    private sealed record DownloadSpecialTitleFolderRule(
        string TitleMarker,
        string TargetFolderName);

    private sealed record ParsedDownloadName(
        string SeriesPrefix,
        string Remainder);

    private sealed record DownloadFolderProposal(
        string? DetectedSeriesName,
        string SuggestedFolderName,
        string Note);

    private sealed record DownloadFileGroup(
        string DisplayName,
        IReadOnlyList<string> FilePaths);

    private sealed record DownloadSortReplacementDecision(
        IReadOnlyList<DownloadSortTargetFileConflict> ReplaceableConflicts,
        DownloadSortTargetFileConflict? BlockingConflict)
    {
        public static DownloadSortReplacementDecision Empty { get; } = new([], null);
    }

    private sealed record DownloadSortTargetFileConflict(
        string SourcePath,
        string TargetPath,
        long SourceLengthBytes,
        long TargetLengthBytes,
        bool SourceMissing = false);
}

/// <summary>
/// Zustandsbewertung eines Sortierkandidaten.
/// </summary>
internal enum DownloadSortItemState
{
    /// <summary>
    /// Der Eintrag kann ohne gleichnamige Zieldateien direkt einsortiert werden.
    /// </summary>
    Ready,

    /// <summary>
    /// Der Eintrag kann direkt einsortiert werden, ersetzt dabei aber gleichnamige Zieldateien.
    /// </summary>
    ReadyWithReplacement,

    /// <summary>
    /// Dem Eintrag fehlt eine sichere Zielordnerentscheidung.
    /// </summary>
    NeedsReview,

    /// <summary>
    /// Der Eintrag hat einen blockierenden Konflikt und wird nicht automatisch ausgeführt.
    /// </summary>
    Conflict,

    /// <summary>
    /// Der Eintrag enthält offensichtlich defekte oder unvollständige MP4-Dateien, die separat wegsortiert werden.
    /// </summary>
    Defective
}

/// <summary>
/// Kleine Zustandshelfer für das Download-Sortiermodul.
/// </summary>
internal static class DownloadSortItemStates
{
    /// <summary>
    /// Gibt an, ob ein Eintrag ohne weitere Pflichtprüfung ausgeführt werden darf.
    /// </summary>
    /// <param name="state">Aktueller Zustand des Sortiereintrags.</param>
    /// <returns><see langword="true"/>, wenn der Eintrag einsortiert werden darf.</returns>
    internal static bool IsSortable(DownloadSortItemState state)
    {
        return state is DownloadSortItemState.Ready or DownloadSortItemState.ReadyWithReplacement or DownloadSortItemState.Defective;
    }
}

/// <summary>
/// Ergebnis eines kompletten Download-Scans.
/// </summary>
internal sealed record DownloadSortScanResult(
    IReadOnlyList<DownloadSortCandidate> Items,
    IReadOnlyList<DownloadSortFolderRenamePlan> FolderRenames);

/// <summary>
/// Fortschrittsmeldung des Download-Sortier-Scans.
/// </summary>
/// <param name="StatusText">Kurztext für den aktuell laufenden Scan-Schritt.</param>
/// <param name="ProgressPercent">Grob interpolierter Gesamtfortschritt von 0 bis 100.</param>
internal sealed record DownloadSortScanProgress(
    string StatusText,
    int ProgressPercent);

/// <summary>
/// Vorschlag für einen losen Download mitsamt Zielordner und aktuellem Status.
/// </summary>
internal sealed record DownloadSortCandidate(
    string DisplayName,
    IReadOnlyList<string> FilePaths,
    string? DetectedSeriesName,
    string SuggestedFolderName,
    DownloadSortItemState State,
    string Note,
    bool IsInitiallySelected = true,
    IReadOnlyList<string>? DefectiveFilePaths = null,
    string PersistentNote = "",
    bool ContainsDefectiveFiles = false);

/// <summary>
/// Sichere Vorab-Umbenennung eines bestehenden Serienordners auf einen kanonischen Namen.
/// </summary>
internal sealed record DownloadSortFolderRenamePlan(
    string CurrentFolderName,
    string TargetFolderName,
    string Reason);

/// <summary>
/// Erneute Statusbewertung für einen manuell angepassten Zielordner.
/// </summary>
internal sealed record DownloadSortTargetEvaluation(
    DownloadSortItemState State,
    string Note);

/// <summary>
/// Ausgewählte Dateigruppe, die in einen Zielordner verschoben werden soll.
/// </summary>
internal sealed record DownloadSortMoveRequest(
    string DisplayName,
    IReadOnlyList<string> FilePaths,
    string TargetFolderName,
    IReadOnlyList<string>? DefectiveFilePaths = null);

/// <summary>
/// Zusammenfassung eines ausgeführten Sortierlaufs.
/// </summary>
internal sealed record DownloadSortApplyResult(
    int MovedGroupCount,
    int MovedFileCount,
    int RenamedFolderCount,
    int SkippedGroupCount,
    IReadOnlyList<string> LogLines);
