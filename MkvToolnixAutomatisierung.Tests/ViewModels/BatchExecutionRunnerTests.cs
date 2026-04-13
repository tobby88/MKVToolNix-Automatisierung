using System.IO;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class BatchExecutionRunnerTests : IDisposable
{
    private readonly string _tempDirectory;

    public BatchExecutionRunnerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void BuildCopyPreparation_DeduplicatesSources_AndFiltersReusableCopies()
    {
        var sourceA = CreateFile("source-a.mkv");
        var sourceB = CreateFile("source-b.mkv");
        var destinationA = Path.Combine(_tempDirectory, "work-a.mkv");
        var destinationB = Path.Combine(_tempDirectory, "work-b.mkv");

        var fileCopy = new StubFileCopyService
        {
            NeedsCopyOverride = plan => string.Equals(plan.SourceFilePath, sourceA, StringComparison.OrdinalIgnoreCase)
        };
        var runner = new BatchExecutionRunner(fileCopy, new StubMuxWorkflowCoordinator(), new StubCleanupService());
        var planA1 = CreatePlan(Path.Combine(_tempDirectory, "out-a1.mkv"), CreateCopyPlan(sourceA, destinationA, 10));
        var planA2 = CreatePlan(Path.Combine(_tempDirectory, "out-a2.mkv"), CreateCopyPlan(sourceA, destinationA, 10));
        var planB = CreatePlan(Path.Combine(_tempDirectory, "out-b.mkv"), CreateCopyPlan(sourceB, destinationB, 20));
        var item = CreateBatchEpisodeItem(Path.Combine(_tempDirectory, "out.mkv"));

        var preparation = runner.BuildCopyPreparation(
        [
            new BatchExecutionWorkItem(item, planA1, []),
            new BatchExecutionWorkItem(item, planA2, []),
            new BatchExecutionWorkItem(item, planB, [])
        ]);

        Assert.Equal(2, preparation.CopyPlans.Count);
        Assert.Single(preparation.CopyPlansToExecute);
        Assert.Equal(sourceA, preparation.CopyPlansToExecute[0].SourceFilePath);
        Assert.Equal(10, preparation.TotalCopyBytes);
    }

    [Fact]
    public async Task PrepareWorkingCopiesAsync_LogsReuseMessage_WhenNothingNeedsCopying()
    {
        var source = CreateFile("source.mkv");
        var destination = Path.Combine(_tempDirectory, "work.mkv");
        var fileCopy = new StubFileCopyService
        {
            NeedsCopyOverride = _ => false
        };
        var runner = new BatchExecutionRunner(fileCopy, new StubMuxWorkflowCoordinator(), new StubCleanupService());
        var item = CreateBatchEpisodeItem(Path.Combine(_tempDirectory, "out.mkv"));
        var preparation = runner.BuildCopyPreparation(
        [
            new BatchExecutionWorkItem(item, CreatePlan(Path.Combine(_tempDirectory, "out.mkv"), CreateCopyPlan(source, destination, 10)), [])
        ]);
        var logLines = new List<string>();

        await runner.PrepareWorkingCopiesAsync(
            preparation,
            new BatchRunProgressTracker(1, (_, _) => { }),
            logLines.Add);

        Assert.Empty(fileCopy.CopiedPlans);
        Assert.Contains(logLines, line => line.Contains("Arbeitskopien", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecutePlansAsync_TracksSuccessfulRuns_NewOutputs_AndDoneFiles()
    {
        var outputPath = Path.Combine(_tempDirectory, "out", "Episode.mkv");
        var cleanupSource = CreateFile("cleanup.txt");
        var movedDoneFile = Path.Combine(_tempDirectory, "done", "cleanup.txt");
        var muxWorkflow = new StubMuxWorkflowCoordinator
        {
            ExecuteMuxOverride = (plan, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(plan.OutputFilePath)!);
                File.WriteAllText(plan.OutputFilePath, "muxed");
                return Task.FromResult(new MuxExecutionResult(0, HasWarning: false, LastProgressPercent: 100));
            }
        };
        var cleanup = new StubCleanupService
        {
            MoveResult = new FileMoveResult([movedDoneFile], [])
        };
        var runner = new BatchExecutionRunner(new StubFileCopyService(), muxWorkflow, cleanup);
        var item = CreateBatchEpisodeItem(outputPath);
        var logs = new List<string>();

        var outcome = await runner.ExecutePlansAsync(
        [
            new BatchExecutionWorkItem(item, CreatePlan(outputPath), [cleanupSource])
        ],
            Path.Combine(_tempDirectory, "done"),
            new BatchRunProgressTracker(1, (_, _) => { }),
            logs.Add);

        Assert.Equal(1, outcome.SuccessCount);
        Assert.Equal(0, outcome.WarningCount);
        Assert.Equal(0, outcome.ErrorCount);
        Assert.Single(outcome.NewOutputFiles);
        Assert.Equal(outputPath, outcome.NewOutputFiles[0]);
        Assert.Single(outcome.MovedDoneFiles);
        Assert.Equal(movedDoneFile, outcome.MovedDoneFiles[0]);
        Assert.Equal(EpisodeArchiveState.Existing, item.ArchiveState);
        Assert.Equal(BatchEpisodeStatusKind.Success, item.StatusKind);
        Assert.Contains(logs, line => line.StartsWith("STARTE:", StringComparison.Ordinal));
        Assert.Contains(logs, line => line.StartsWith("DONE:", StringComparison.Ordinal));
        Assert.Equal(cleanupSource, Assert.Single(cleanup.LastMoveSourceFiles));
        Assert.Equal(_tempDirectory, cleanup.LastDeleteEmptyParentRoot);
    }

    [Fact]
    public async Task ExecutePlansAsync_SkipPlan_StillMovesCleanupFiles_AndCountsItemAsUpToDate()
    {
        var outputPath = Path.Combine(_tempDirectory, "skip", "Episode.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, "already-current");
        var cleanupSource = CreateFile("skip-cleanup.txt");
        var movedDoneFile = Path.Combine(_tempDirectory, "done", "skip-cleanup.txt");
        var cleanup = new StubCleanupService
        {
            MoveResult = new FileMoveResult([movedDoneFile], [])
        };
        var runner = new BatchExecutionRunner(new StubFileCopyService(), new StubMuxWorkflowCoordinator(), cleanup);
        var item = CreateBatchEpisodeItem(outputPath);
        var logs = new List<string>();

        var outcome = await runner.ExecutePlansAsync(
        [
            new BatchExecutionWorkItem(
                item,
                SeriesEpisodeMuxPlan.CreateSkip(
                    mkvMergePath: @"C:\Tools\mkvmerge.exe",
                    outputFilePath: outputPath,
                    title: "Pilot",
                    skipReason: "Zieldatei bereits aktuell.",
                    notes: ["Nur Cleanup nötig."]),
                [cleanupSource])
        ],
            Path.Combine(_tempDirectory, "done"),
            new BatchRunProgressTracker(1, (_, _) => { }),
            logs.Add);

        Assert.Equal(0, outcome.SuccessCount);
        Assert.Equal(0, outcome.WarningCount);
        Assert.Equal(0, outcome.ErrorCount);
        Assert.Equal(1, outcome.UpToDateCount);
        Assert.Single(outcome.MovedDoneFiles);
        Assert.Empty(outcome.NewOutputFiles);
        Assert.Equal(BatchEpisodeStatusKind.UpToDate, item.StatusKind);
        Assert.Contains(logs, line => line.Contains("KEIN MUX", StringComparison.Ordinal));
        Assert.Contains(logs, line => line.StartsWith("DONE:", StringComparison.Ordinal));
        Assert.Equal(cleanupSource, Assert.Single(cleanup.LastMoveSourceFiles));
    }

    [Fact]
    public async Task ExecutePlansAsync_MovesWorkingCopyToDone_WhenBatchMuxSucceeded()
    {
        var outputPath = Path.Combine(_tempDirectory, "work-copy", "Episode.mkv");
        var workingCopySource = CreateFile("existing-archive.mkv", "archive");
        var workingCopyDestination = CreateFile("Episode - Arbeitskopie.mkv", "working-copy");
        var movedWorkingCopy = Path.Combine(_tempDirectory, "done", "Episode - Arbeitskopie.mkv");
        var muxWorkflow = new StubMuxWorkflowCoordinator
        {
            ExecuteMuxOverride = (plan, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(plan.OutputFilePath)!);
                File.WriteAllText(plan.OutputFilePath, "muxed");
                return Task.FromResult(new MuxExecutionResult(0, HasWarning: false, LastProgressPercent: 100));
            }
        };
        var cleanup = new StubCleanupService
        {
            MoveResult = new FileMoveResult([movedWorkingCopy], [])
        };
        var runner = new BatchExecutionRunner(new StubFileCopyService(), muxWorkflow, cleanup);
        var item = CreateBatchEpisodeItem(outputPath);
        var plan = CreatePlan(outputPath, CreateCopyPlan(
            workingCopySource,
            workingCopyDestination,
            new FileInfo(workingCopySource).Length));

        var outcome = await runner.ExecutePlansAsync(
        [
            new BatchExecutionWorkItem(item, plan, [])
        ],
            Path.Combine(_tempDirectory, "done"),
            new BatchRunProgressTracker(1, (_, _) => { }),
            _ => { });

        Assert.Equal(1, outcome.SuccessCount);
        Assert.Equal(movedWorkingCopy, Assert.Single(outcome.MovedDoneFiles));
        Assert.Equal(workingCopyDestination, Assert.Single(cleanup.LastMoveSourceFiles));
        Assert.Equal(MuxWorkflowTemporaryCleanup.KeepWorkingCopy, Assert.Single(muxWorkflow.TemporaryCleanupModes));
    }

    [Fact]
    public async Task ExecutePlansAsync_TreatsExitCodeOneWithOutputFileAsWarning()
    {
        var outputPath = Path.Combine(_tempDirectory, "warning", "Episode.mkv");
        var muxWorkflow = new StubMuxWorkflowCoordinator
        {
            ExecuteMuxOverride = (plan, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(plan.OutputFilePath)!);
                File.WriteAllText(plan.OutputFilePath, "muxed with warning");
                return Task.FromResult(new MuxExecutionResult(1, HasWarning: false, LastProgressPercent: 100));
            }
        };
        var runner = new BatchExecutionRunner(new StubFileCopyService(), muxWorkflow, new StubCleanupService());
        var item = CreateBatchEpisodeItem(outputPath);
        var logs = new List<string>();

        var outcome = await runner.ExecutePlansAsync(
        [
            new BatchExecutionWorkItem(item, CreatePlan(outputPath), [])
        ],
            Path.Combine(_tempDirectory, "done"),
            new BatchRunProgressTracker(1, (_, _) => { }),
            logs.Add);

        Assert.Equal(0, outcome.SuccessCount);
        Assert.Equal(1, outcome.WarningCount);
        Assert.Equal(0, outcome.ErrorCount);
        Assert.Single(outcome.NewOutputFiles);
        Assert.Equal(EpisodeArchiveState.Existing, item.ArchiveState);
        Assert.Equal(BatchEpisodeStatusKind.Warning, item.StatusKind);
        Assert.Equal("Warnung (Datei erstellt, Exit-Code 1)", item.Status);
        Assert.Contains(logs, line => line.Contains("Exit-Code 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecutePlansAsync_TreatsMkvMergeWarningsAsWarning_AndExplainsSource()
    {
        var outputPath = Path.Combine(_tempDirectory, "warning-lines", "Episode.mkv");
        var muxWorkflow = new StubMuxWorkflowCoordinator
        {
            ExecuteMuxOverride = (plan, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(plan.OutputFilePath)!);
                File.WriteAllText(plan.OutputFilePath, "muxed with warnings");
                return Task.FromResult(new MuxExecutionResult(0, HasWarning: true, LastProgressPercent: 100));
            }
        };
        var runner = new BatchExecutionRunner(new StubFileCopyService(), muxWorkflow, new StubCleanupService());
        var item = CreateBatchEpisodeItem(outputPath);
        var logs = new List<string>();

        var outcome = await runner.ExecutePlansAsync(
        [
            new BatchExecutionWorkItem(item, CreatePlan(outputPath), [])
        ],
            Path.Combine(_tempDirectory, "done"),
            new BatchRunProgressTracker(1, (_, _) => { }),
            logs.Add);

        Assert.Equal(0, outcome.SuccessCount);
        Assert.Equal(1, outcome.WarningCount);
        Assert.Equal(0, outcome.ErrorCount);
        Assert.Equal(BatchEpisodeStatusKind.Warning, item.StatusKind);
        Assert.Equal("Warnung (mkvmerge meldet Warnungen)", item.Status);
        Assert.Contains(logs, line => line.Contains("mkvmerge meldet Warnungen", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecutePlansAsync_RecordsErrors_WhenMuxExecutionThrows()
    {
        var outputPath = Path.Combine(_tempDirectory, "error", "Episode.mkv");
        var muxWorkflow = new StubMuxWorkflowCoordinator
        {
            ExecuteMuxOverride = (_, _) => throw new InvalidOperationException("boom")
        };
        var runner = new BatchExecutionRunner(new StubFileCopyService(), muxWorkflow, new StubCleanupService());
        var item = CreateBatchEpisodeItem(outputPath);
        var logs = new List<string>();

        var outcome = await runner.ExecutePlansAsync(
        [
            new BatchExecutionWorkItem(item, CreatePlan(outputPath), [])
        ],
            Path.Combine(_tempDirectory, "done"),
            new BatchRunProgressTracker(1, (_, _) => { }),
            logs.Add);

        Assert.Equal(0, outcome.SuccessCount);
        Assert.Equal(0, outcome.WarningCount);
        Assert.Equal(1, outcome.ErrorCount);
        Assert.Equal(BatchEpisodeStatusKind.Error, item.StatusKind);
        Assert.Contains(logs, line => line.Contains("FEHLER: boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecutePlansAsync_MarksCurrentItemCancelled_WhenUserCancelsRun()
    {
        var outputPath = Path.Combine(_tempDirectory, "cancelled", "Episode.mkv");
        var executionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var muxWorkflow = new StubMuxWorkflowCoordinator
        {
            ExecuteMuxOverride = async (_, cancellationToken) =>
            {
                executionStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new MuxExecutionResult(0, HasWarning: false, LastProgressPercent: 0);
            }
        };
        var runner = new BatchExecutionRunner(new StubFileCopyService(), muxWorkflow, new StubCleanupService());
        var item = CreateBatchEpisodeItem(outputPath);
        var logs = new List<string>();
        using var cancellationSource = new CancellationTokenSource();

        var executionTask = runner.ExecutePlansAsync(
        [
            new BatchExecutionWorkItem(item, CreatePlan(outputPath), [])
        ],
            Path.Combine(_tempDirectory, "done"),
            new BatchRunProgressTracker(1, (_, _) => { }),
            logs.Add,
            cancellationSource.Token);

        await executionStarted.Task;
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executionTask);
        Assert.Equal(BatchEpisodeStatusKind.Cancelled, item.StatusKind);
        Assert.Contains(logs, line => line.Contains("ABGEBROCHEN", StringComparison.Ordinal));
    }

    [Fact]
    public void RefreshArchivePresence_PreservesExistingUsageSummary_DuringStatusChanges()
    {
        var outputPath = Path.Combine(_tempDirectory, "existing", "Episode.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, "existing");
        var item = CreateBatchEpisodeItem(outputPath);
        var usageSummary = new EpisodeUsageSummary(
            "Ziel bereits vorhanden",
            "Vergleich fertig",
            new EpisodeUsageEntry("Video alt", null, null),
            new EpisodeUsageEntry("(keine)", null, null),
            new EpisodeUsageEntry("Audio alt", null, null),
            new EpisodeUsageEntry("(keine)", null, null),
            new EpisodeUsageEntry("SRT alt", null, null),
            new EpisodeUsageEntry("Anhang alt", null, null));

        item.SetPlanSummary("Letzte berechnete Planung");
        item.SetUsageSummary(usageSummary);

        item.RefreshArchivePresence(BatchEpisodeStatusKind.Running);

        Assert.Equal(BatchEpisodeStatusKind.Running, item.StatusKind);
        Assert.Equal("Letzte berechnete Planung", item.PlanSummaryText);
        Assert.Same(usageSummary, item.UsageSummary);
        Assert.Equal("Video alt", item.UsageSummary!.MainVideo.CurrentText);
    }

    [Fact]
    public void RefreshArchivePresence_PreservesCustomWarningStatusText_WhenStatusKindStaysTheSame()
    {
        var outputPath = Path.Combine(_tempDirectory, "existing-warning", "Episode.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, "existing");
        var item = CreateBatchEpisodeItem(outputPath);

        item.SetStatus(BatchEpisodeStatusKind.Warning, "Warnung (mkvmerge meldet Warnungen)");
        item.RefreshArchivePresence(BatchEpisodeStatusKind.Warning);

        Assert.Equal(BatchEpisodeStatusKind.Warning, item.StatusKind);
        Assert.Equal("Warnung (mkvmerge meldet Warnungen)", item.Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private BatchEpisodeItemViewModel CreateBatchEpisodeItem(string outputPath)
    {
        var mainVideoPath = CreateFile("episode-source.mkv");
        var localGuess = new EpisodeMetadataGuess("Beispielserie", "Pilot", "01", "01");
        var detected = new AutoDetectedEpisodeFiles(
            mainVideoPath,
            [],
            null,
            [],
            [],
            [],
            outputPath,
            "Pilot",
            "Beispielserie",
            "01",
            "01",
            RequiresManualCheck: false,
            ManualCheckFilePaths: [],
            Notes: []);
        var metadataResolution = new EpisodeMetadataResolutionResult(
            localGuess,
            new TvdbEpisodeSelection(42, "Beispielserie", 100, "Pilot", "01", "01"),
            "TVDB automatisch erkannt",
            100,
            RequiresReview: false,
            QueryWasAttempted: true,
            QuerySucceeded: true);

        return BatchEpisodeItemViewModel.CreateFromDetection(
            mainVideoPath,
            localGuess,
            detected,
            metadataResolution,
            outputPath,
            BatchEpisodeStatusKind.Ready,
            isSelected: true);
    }

    private SeriesEpisodeMuxPlan CreatePlan(string outputPath, FileCopyPlan? workingCopy = null)
    {
        var sourceVideoPath = CreateFile(Guid.NewGuid().ToString("N") + ".mkv");
        return new SeriesEpisodeMuxPlan(
            mkvMergePath: @"C:\Tools\mkvmerge.exe",
            outputFilePath: outputPath,
            title: "Pilot",
            videoSources:
            [
                new VideoSourcePlan(sourceVideoPath, 0, "Deutsch - Video", IsDefaultTrack: true)
            ],
            audioSources:
            [
                new AudioSourcePlan(sourceVideoPath, 1, "Deutsch - Audio", IsDefaultTrack: true)
            ],
            primarySourceAudioTrackIds: [1],
            primarySourceSubtitleTrackIds: [],
            primarySourceAttachmentIds: null,
            includePrimarySourceAttachments: false,
            attachmentSourcePath: null,
            attachmentSourceAttachmentIds: null,
            audioDescriptionFilePath: null,
            audioDescriptionTrackId: null,
            audioDescriptionTrackName: null,
            audioDescriptionLanguageCode: null,
            subtitleFiles: [],
            attachmentFilePaths: [],
            preservedAttachmentNames: [],
            usageComparison: ArchiveUsageComparison.Empty,
            workingCopy: workingCopy,
            notes: []);
    }

    private FileCopyPlan CreateCopyPlan(string sourcePath, string destinationPath, long fileSizeBytes)
    {
        return new FileCopyPlan(sourcePath, destinationPath, fileSizeBytes, DateTime.UtcNow);
    }

    private string CreateFile(string fileName, string content = "data")
    {
        var path = Path.Combine(_tempDirectory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class StubFileCopyService : IFileCopyService
    {
        public Func<FileCopyPlan, bool>? NeedsCopyOverride { get; init; }
        public Func<FileCopyPlan, CancellationToken, Task>? CopyOverride { get; init; }

        public List<FileCopyPlan> CopiedPlans { get; } = [];

        public bool NeedsCopy(FileCopyPlan copyPlan)
        {
            return NeedsCopyOverride?.Invoke(copyPlan) ?? !copyPlan.IsReusable;
        }

        public Task CopyAsync(
            FileCopyPlan copyPlan,
            Action<long, long>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            CopiedPlans.Add(copyPlan);
            if (CopyOverride is not null)
            {
                return CopyOverride(copyPlan, cancellationToken);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubCleanupService : IEpisodeCleanupService
    {
        public FileMoveResult MoveResult { get; init; } = new FileMoveResult([], []);

        public IReadOnlyList<string> LastMoveSourceFiles { get; private set; } = [];
        public string? LastDeleteEmptyParentRoot { get; private set; }

        public Task<FileMoveResult> MoveFilesToDirectoryAsync(
            IReadOnlyList<string> sourceFilePaths,
            string targetDirectory,
            Action<int, int, string>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            LastMoveSourceFiles = sourceFilePaths.ToList();
            return Task.FromResult(MoveResult);
        }

        public Task<FileRecycleResult> RecycleFilesAsync(
            IReadOnlyList<string> filePaths,
            Action<int, int, string>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FileRecycleResult([], []));
        }

        public void DeleteTemporaryFile(string? filePath)
        {
        }

        public void DeleteDirectoryIfEmpty(string? directoryPath)
        {
        }

        public void DeleteEmptyParentDirectories(IEnumerable<string> sourceFilePaths, string? stopAtRoot)
        {
            LastDeleteEmptyParentRoot = stopAtRoot;
        }
    }

    private sealed class StubMuxWorkflowCoordinator : IMuxWorkflowCoordinator
    {
        public Func<SeriesEpisodeMuxPlan, CancellationToken, Task<MuxExecutionResult>>? ExecuteMuxOverride { get; init; }
        public List<MuxWorkflowTemporaryCleanup> TemporaryCleanupModes { get; } = [];

        public bool NeedsWorkingCopyPreparation(SeriesEpisodeMuxPlan plan)
        {
            return plan.WorkingCopy is not null && !plan.WorkingCopy.IsReusable;
        }

        public Task PrepareWorkingCopyAsync(
            SeriesEpisodeMuxPlan plan,
            Action<WorkingCopyPreparationUpdate>? onUpdate = null,
            CancellationToken cancellationToken = default)
        {
            onUpdate?.Invoke(new WorkingCopyPreparationUpdate(100, ReusesExistingCopy: plan.WorkingCopy?.IsReusable ?? false));
            return Task.CompletedTask;
        }

        public Task<MuxExecutionResult> ExecuteMuxAsync(
            SeriesEpisodeMuxPlan plan,
            Action<string>? onOutput = null,
            Action<MuxExecutionUpdate>? onUpdate = null,
            CancellationToken cancellationToken = default,
            MuxWorkflowTemporaryCleanup temporaryCleanup = MuxWorkflowTemporaryCleanup.DeleteWorkingCopy)
        {
            TemporaryCleanupModes.Add(temporaryCleanup);
            if (ExecuteMuxOverride is null)
            {
                return Task.FromResult(new MuxExecutionResult(0, HasWarning: false, LastProgressPercent: 100));
            }

            return ExecuteMuxOverride(plan, cancellationToken);
        }
    }
}
