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
        "filme",
        "hallo deutschland",
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
    /// <returns>Scanergebnis inklusive Move-Kandidaten und sicherer Ordner-Umbenennungen.</returns>
    public DownloadSortScanResult Scan(
        string rootDirectory,
        Action<DownloadSortScanProgress>? onProgress = null)
    {
        EnsureDirectoryExists(rootDirectory);

        ReportScanProgress(onProgress, "Pruefe vorhandene Serienordner...", 5);
        var folderRenames = BuildFolderRenamePlans(rootDirectory, onProgress);

        ReportScanProgress(onProgress, "Lese lose Download-Dateien...", 30);
        var rootGroups = EnumerateLogicalGroups(rootDirectory);

        var candidates = new List<DownloadSortCandidate>();
        for (var index = 0; index < rootGroups.Count; index++)
        {
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
    /// <returns>Aktueller Status mit Konflikt- oder Hinweistext.</returns>
    public DownloadSortTargetEvaluation EvaluateTarget(
        string rootDirectory,
        IReadOnlyList<string> filePaths,
        string? targetFolderName,
        IReadOnlyList<DownloadSortFolderRenamePlan> folderRenames)
    {
        EnsureDirectoryExists(rootDirectory);
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(folderRenames);

        if (filePaths.Count == 0)
        {
            return new DownloadSortTargetEvaluation(
                DownloadSortItemState.NeedsReview,
                "Zu diesem Eintrag wurden keine Dateien gefunden.");
        }

        var normalizedFolderName = NormalizeTargetFolderName(targetFolderName);
        if (string.IsNullOrWhiteSpace(normalizedFolderName))
        {
            return new DownloadSortTargetEvaluation(
                DownloadSortItemState.NeedsReview,
                "Kein Zielordner erkannt. Bitte pruefen.");
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

            var sourcePath = Path.Combine(rootDirectory, renamePlan.CurrentFolderName);
            var destinationPath = Path.Combine(rootDirectory, renamePlan.TargetFolderName);
            if (!Directory.Exists(sourcePath))
            {
                continue;
            }

            if (Directory.Exists(destinationPath))
            {
                logLines.Add($"UEBERSPRUNGEN: Ordner '{renamePlan.CurrentFolderName}' wurde nicht umbenannt, weil '{renamePlan.TargetFolderName}' bereits existiert.");
                continue;
            }

            try
            {
                Directory.Move(sourcePath, destinationPath);
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

            var targetFolderName = NormalizeTargetFolderName(request.TargetFolderName);
            if (string.IsNullOrWhiteSpace(targetFolderName))
            {
                skippedGroupCount++;
                logLines.Add($"UEBERSPRUNGEN: {request.DisplayName} -> kein gueltiger Zielordner.");
                continue;
            }

            var targetDirectory = Path.Combine(rootDirectory, targetFolderName);
            Directory.CreateDirectory(targetDirectory);

            var replacementDecision = EvaluateExistingTargetFiles(request.FilePaths, targetDirectory);
            if (replacementDecision.BlockingConflict is { } blockingConflict)
            {
                skippedGroupCount++;
                logLines.Add($"KONFLIKT: {request.DisplayName} -> {BuildBlockingConflictNote(blockingConflict)}");
                continue;
            }

            var groupMovedCount = 0;
            foreach (var filePath in request.FilePaths)
            {
                var destinationPath = Path.Combine(targetDirectory, Path.GetFileName(filePath));
                if (Path.GetFullPath(filePath).Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    File.Move(filePath, destinationPath, overwrite: true);
                    groupMovedCount++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logLines.Add($"FEHLER: '{Path.GetFileName(filePath)}' konnte nicht nach '{targetFolderName}' verschoben werden: {ex.Message}");
                }
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

            var operationLabel = string.Equals(targetFolderName, DefectiveFolderName, StringComparison.OrdinalIgnoreCase)
                ? "DEFEKT"
                : "SORTIERT";
            logLines.Add($"{operationLabel}: {request.DisplayName} -> {targetFolderName} ({request.FilePaths.Count} Datei(en))");
        }

        return new DownloadSortApplyResult(
            movedGroupCount,
            movedFileCount,
            renamedFolderCount,
            skippedGroupCount,
            logLines);
    }

    private IReadOnlyList<DownloadSortCandidate> BuildCandidates(
        string rootDirectory,
        DownloadFileGroup group,
        IReadOnlyList<DownloadSortFolderRenamePlan> folderRenames)
    {
        var defectiveVideoPaths = group.FilePaths
            .Where(path => VideoExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new
            {
                FilePath = path,
                Health = MediaFileHealth.CheckMp4File(path, CompanionTextMetadataReader.ReadDetailed(Path.ChangeExtension(path, ".txt")))
            })
            .Where(candidate => !candidate.Health.IsUsable)
            .ToList();

        if (defectiveVideoPaths.Count == 0)
        {
            return [BuildCandidate(rootDirectory, group.DisplayName, group.FilePaths, folderRenames)];
        }

        var defectiveNote = BuildDefectiveVideoNote(defectiveVideoPaths.Select(candidate => candidate.Health.Reason).ToList());
        var defectiveVideoPathSet = defectiveVideoPaths
            .Select(candidate => candidate.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<DownloadSortCandidate>();
        candidates.Add(new DownloadSortCandidate(
            group.DisplayName,
            defectiveVideoPaths.Select(candidate => candidate.FilePath).ToList(),
            DetectedSeriesName: null,
            SuggestedFolderName: DefectiveFolderName,
            DownloadSortItemState.Defective,
            defectiveNote));

        // Defekte Videos werden sofort isoliert, Begleitdateien dagegen nicht:
        // Untertitel/TXT sind oft weiter nutzbar und sollen im regulären Serienordner
        // für einen späteren Mux-Lauf erhalten bleiben. Das spätere Episode-Cleanup
        // räumt diese Dateien nach erfolgreicher Verarbeitung wieder mit weg.
        var remainingFilePaths = group.FilePaths
            .Except(defectiveVideoPaths.Select(candidate => candidate.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (remainingFilePaths.Count > 0)
        {
            candidates.Add(BuildCandidate(rootDirectory, group.DisplayName, remainingFilePaths, folderRenames));
        }

        return candidates;
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
            note);
    }

    private static string BuildDefectiveVideoNote(IReadOnlyList<string?> reasons)
    {
        var firstReason = reasons.FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
        return string.IsNullOrWhiteSpace(firstReason)
            ? "MP4 wirkt defekt oder unvollständig; nur die betroffene MP4 wird in den Defekt-Ordner verschoben."
            : firstReason + " Nur die betroffene MP4 wird in den Defekt-Ordner verschoben.";
    }

    private static IReadOnlyList<DownloadSortFolderRenamePlan> BuildFolderRenamePlans(
        string rootDirectory,
        Action<DownloadSortScanProgress>? onProgress)
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

            if (string.Equals(currentFolderName, DefectiveFolderName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var detectedFolderNames = EnumerateLogicalGroups(directoryPath)
                .Select(group => DetectFolderProposal(group.FilePaths).SuggestedFolderName)
                .Where(folderName => !string.IsNullOrWhiteSpace(folderName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (detectedFolderNames.Count != 1)
            {
                continue;
            }

            var targetFolderName = detectedFolderNames[0];
            if (string.Equals(currentFolderName, targetFolderName, StringComparison.OrdinalIgnoreCase)
                || existingDirectoryNames.Contains(targetFolderName!))
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

    private static IReadOnlyList<DownloadFileGroup> EnumerateLogicalGroups(string directoryPath)
    {
        return Directory.EnumerateFiles(directoryPath)
            .Where(IsSupportedSortFile)
            .GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .Select(group => new DownloadFileGroup(
                BuildGroupDisplayName(group.Key),
                group
                    .OrderBy(GetExtensionPriority)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .ToList();
    }

    private static bool IsSupportedSortFile(string filePath)
    {
        return SupportedExtensions.Contains(Path.GetExtension(filePath));
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
                var conflict = new DownloadSortTargetFileConflict(
                    filePath,
                    destinationPath,
                    new FileInfo(filePath).Length,
                    new FileInfo(destinationPath).Length);

                if (ShouldKeepExistingTargetFile(conflict))
                {
                    return new DownloadSortReplacementDecision(replaceableConflicts, conflict);
                }

                replaceableConflicts.Add(conflict);
            }
        }

        return new DownloadSortReplacementDecision(replaceableConflicts, null);
    }

    /// <summary>
    /// Schützt vorhandene Zielvideos nur dann vor dem Standard-Overwrite, wenn ein billiges,
    /// lokales Signal klar gegen die neue Datei spricht. Eine echte Auflösungsprüfung wäre hier
    /// unnötig teuer; bei MediathekView-Downloads ist eine deutlich größere Videodatei der
    /// pragmatische Proxy für möglicherweise höhere Qualität oder bessere Auflösung.
    /// </summary>
    private static bool ShouldKeepExistingTargetFile(DownloadSortTargetFileConflict conflict)
    {
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
        long TargetLengthBytes);
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
    string Note);

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
    string TargetFolderName);

/// <summary>
/// Zusammenfassung eines ausgeführten Sortierlaufs.
/// </summary>
internal sealed record DownloadSortApplyResult(
    int MovedGroupCount,
    int MovedFileCount,
    int RenamedFolderCount,
    int SkippedGroupCount,
    IReadOnlyList<string> LogLines);
