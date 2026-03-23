using System.Text;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

public sealed class SeriesEpisodeMuxPlan
{
    public SeriesEpisodeMuxPlan(
        string mkvMergePath,
        string outputFilePath,
        string title,
        IReadOnlyList<VideoSourcePlan> videoSources,
        string primaryAudioFilePath,
        int primaryAudioTrackId,
        IReadOnlyList<int>? primarySourceAudioTrackIds,
        IReadOnlyList<int>? primarySourceSubtitleTrackIds,
        bool includePrimarySourceAttachments,
        string? audioDescriptionFilePath,
        int? audioDescriptionTrackId,
        IReadOnlyList<SubtitleFile> subtitleFiles,
        IReadOnlyList<string> attachmentFilePaths,
        IReadOnlyList<string> preservedAttachmentNames,
        FileCopyPlan? workingCopy,
        EpisodeTrackMetadata metadata,
        IReadOnlyList<string> notes)
    {
        if (videoSources.Count == 0)
        {
            throw new ArgumentException("Mindestens eine Videospur muss vorhanden sein.", nameof(videoSources));
        }

        MkvMergePath = mkvMergePath;
        OutputFilePath = outputFilePath;
        Title = title;
        VideoSources = videoSources;
        PrimaryAudioFilePath = primaryAudioFilePath;
        PrimaryAudioTrackId = primaryAudioTrackId;
        PrimarySourceAudioTrackIds = primarySourceAudioTrackIds;
        PrimarySourceSubtitleTrackIds = primarySourceSubtitleTrackIds;
        IncludePrimarySourceAttachments = includePrimarySourceAttachments;
        AudioDescriptionFilePath = audioDescriptionFilePath;
        AudioDescriptionTrackId = audioDescriptionTrackId;
        SubtitleFiles = subtitleFiles;
        AttachmentFilePaths = attachmentFilePaths;
        PreservedAttachmentNames = preservedAttachmentNames;
        WorkingCopy = workingCopy;
        Metadata = metadata;
        Notes = notes;
    }

    private SeriesEpisodeMuxPlan(
        string mkvMergePath,
        string outputFilePath,
        string title,
        string skipReason,
        IReadOnlyList<string> notes)
    {
        MkvMergePath = mkvMergePath;
        OutputFilePath = outputFilePath;
        Title = title;
        SkipMux = true;
        SkipReason = skipReason;
        VideoSources = [];
        PrimaryAudioFilePath = string.Empty;
        PrimarySourceAudioTrackIds = null;
        PrimarySourceSubtitleTrackIds = null;
        IncludePrimarySourceAttachments = false;
        SubtitleFiles = [];
        AttachmentFilePaths = [];
        PreservedAttachmentNames = [];
        Metadata = new EpisodeTrackMetadata("Deutsch - Audio", "Deutsch (sehbehinderte) - Audio");
        Notes = notes;
    }

    public string MkvMergePath { get; }
    public string OutputFilePath { get; }
    public string Title { get; }
    public bool SkipMux { get; }
    public string? SkipReason { get; }
    public IReadOnlyList<VideoSourcePlan> VideoSources { get; }
    public string PrimaryAudioFilePath { get; }
    public int PrimaryAudioTrackId { get; }
    public IReadOnlyList<int>? PrimarySourceAudioTrackIds { get; }
    public IReadOnlyList<int>? PrimarySourceSubtitleTrackIds { get; }
    public bool IncludePrimarySourceAttachments { get; }
    public string? AudioDescriptionFilePath { get; }
    public int? AudioDescriptionTrackId { get; }
    public IReadOnlyList<SubtitleFile> SubtitleFiles { get; }
    public IReadOnlyList<string> AttachmentFilePaths { get; }
    public IReadOnlyList<string> PreservedAttachmentNames { get; }
    public FileCopyPlan? WorkingCopy { get; }
    public EpisodeTrackMetadata Metadata { get; }
    public IReadOnlyList<string> Notes { get; }

    public static SeriesEpisodeMuxPlan CreateSkip(
        string mkvMergePath,
        string outputFilePath,
        string title,
        string skipReason,
        IReadOnlyList<string> notes)
    {
        return new SeriesEpisodeMuxPlan(mkvMergePath, outputFilePath, title, skipReason, notes);
    }

    public IReadOnlyList<string> BuildArguments()
    {
        if (SkipMux)
        {
            return [];
        }

        var arguments = new List<string>
        {
            "--output",
            OutputFilePath,
            "--title",
            Title
        };

        var primaryVideo = VideoSources[0];
        var primaryVideoTrackId = primaryVideo.TrackId.ToString();
        var primaryAudioTrackId = PrimaryAudioTrackId.ToString();
        var primaryVideoFilePath = ResolveRuntimeFilePath(primaryVideo.FilePath);

        if (PrimarySourceAudioTrackIds is { Count: > 0 })
        {
            arguments.AddRange(["--audio-tracks", string.Join(",", PrimarySourceAudioTrackIds)]);
        }

        if (PrimarySourceSubtitleTrackIds is { Count: 0 })
        {
            arguments.Add("--no-subtitles");
        }
        else if (PrimarySourceSubtitleTrackIds is { Count: > 0 })
        {
            arguments.AddRange(["--subtitle-tracks", string.Join(",", PrimarySourceSubtitleTrackIds)]);
        }

        if (!IncludePrimarySourceAttachments)
        {
            arguments.Add("--no-attachments");
        }

        arguments.AddRange(
        [
            "--language",
            $"{primaryVideoTrackId}:de",
            "--track-name",
            $"{primaryVideoTrackId}:{primaryVideo.TrackName}",
            "--default-track-flag",
            $"{primaryVideoTrackId}:yes",
            "--stereo-mode",
            $"{primaryVideoTrackId}:mono",
            "--original-flag",
            $"{primaryVideoTrackId}:yes",

            "--language",
            $"{primaryAudioTrackId}:de",
            "--track-name",
            $"{primaryAudioTrackId}:{Metadata.AudioTrackName}",
            "--default-track-flag",
            $"{primaryAudioTrackId}:yes",
            "--original-flag",
            $"{primaryAudioTrackId}:yes",

            primaryVideoFilePath
        ]);

        foreach (var videoSource in VideoSources.Skip(1))
        {
            var trackId = videoSource.TrackId.ToString();
            var runtimeVideoFilePath = ResolveRuntimeFilePath(videoSource.FilePath);
            arguments.AddRange(
            [
                "--no-audio",
                "--no-subtitles",
                "--no-attachments",
                "--language",
                $"{trackId}:de",
                "--track-name",
                $"{trackId}:{videoSource.TrackName}",
                "--default-track-flag",
                $"{trackId}:no",
                "--stereo-mode",
                $"{trackId}:mono",
                "--original-flag",
                $"{trackId}:yes",
                runtimeVideoFilePath
            ]);
        }

        if (!string.IsNullOrWhiteSpace(AudioDescriptionFilePath) && AudioDescriptionTrackId is not null)
        {
            var adTrackId = AudioDescriptionTrackId.Value.ToString();
            var runtimeAudioDescriptionFilePath = ResolveRuntimeFilePath(AudioDescriptionFilePath);
            arguments.AddRange(
            [
                "--no-video",
                "--no-subtitles",
                "--no-attachments",
                "--audio-tracks",
                adTrackId,
                "--language",
                $"{adTrackId}:de",
                "--track-name",
                $"{adTrackId}:{Metadata.AudioDescriptionTrackName}",
                "--default-track-flag",
                $"{adTrackId}:no",
                "--visual-impaired-flag",
                $"{adTrackId}:yes",
                "--original-flag",
                $"{adTrackId}:yes",
                runtimeAudioDescriptionFilePath
            ]);
        }

        foreach (var subtitle in SubtitleFiles)
        {
            if (subtitle.IsEmbedded && subtitle.EmbeddedTrackId is int embeddedTrackId)
            {
                var runtimeSubtitleFilePath = ResolveRuntimeFilePath(subtitle.FilePath);
                arguments.AddRange(
                [
                    "--no-video",
                    "--no-audio",
                    "--no-attachments",
                    "--subtitle-tracks",
                    embeddedTrackId.ToString(),
                    runtimeSubtitleFilePath
                ]);
                continue;
            }

            arguments.AddRange(
            [
                "--language",
                "0:de",
                "--track-name",
                $"0:{subtitle.TrackName}",
                "--default-track-flag",
                "0:no",
                "--hearing-impaired-flag",
                "0:yes",
                "--original-flag",
                "0:yes",
                subtitle.FilePath
            ]);
        }

        foreach (var attachmentFilePath in AttachmentFilePaths)
        {
            arguments.AddRange(
            [
                "--attachment-mime-type",
                "text/plain",
                "--attach-file",
                attachmentFilePath
            ]);
        }

        return arguments;
    }

    private string ResolveRuntimeFilePath(string filePath)
    {
        if (WorkingCopy is not null
            && string.Equals(filePath, WorkingCopy.SourceFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return WorkingCopy.DestinationFilePath;
        }

        return filePath;
    }

    public string GetCommandLinePreview()
    {
        return string.Join(Environment.NewLine, BuildArguments().Select(EscapeArgument));
    }

    public EpisodeUsageSummary BuildUsageSummary()
    {
        if (SkipMux)
        {
            return EpisodeUsageSummary.CreatePending(
                SkipReason ?? "Die Zieldatei ist bereits vollständig.",
                "keine weiteren Aktionen");
        }

        var archiveAction = WorkingCopy is not null
            ? string.Equals(VideoSources[0].FilePath, WorkingCopy.SourceFilePath, StringComparison.OrdinalIgnoreCase)
                ? "Vorhandene Zieldatei bleibt Basis"
                : "Vorhandene Zieldatei wird mit neuer Hauptquelle aktualisiert"
            : File.Exists(OutputFilePath)
                ? "MKV in der Serienbibliothek vorhanden, wird aktualisiert"
                : "MKV in der Serienbibliothek noch nicht vorhanden";

        var archiveDetails = WorkingCopy is not null
            ? WorkingCopy.IsReusable
                ? $"Arbeitskopie aktuell vorhanden: {Path.GetFileName(WorkingCopy.DestinationFilePath)}"
                : $"Arbeitskopie wird erstellt: {Path.GetFileName(WorkingCopy.DestinationFilePath)}"
            : File.Exists(OutputFilePath)
                ? Path.GetFileName(OutputFilePath)
                : "Neue MKV wird direkt erstellt";

        return new EpisodeUsageSummary(
            archiveAction,
            archiveDetails,
            Path.GetFileName(VideoSources[0].FilePath),
            VideoSources.Count > 1
                ? string.Join(Environment.NewLine, VideoSources.Skip(1).Select(source => Path.GetFileName(source.FilePath)))
                : "(keine)",
            Path.GetFileName(PrimaryAudioFilePath),
            string.IsNullOrWhiteSpace(AudioDescriptionFilePath) ? "(keine)" : Path.GetFileName(AudioDescriptionFilePath),
            SubtitleFiles.Count == 0
                ? "(keine)"
                : string.Join(Environment.NewLine, SubtitleFiles.Select(file => file.PreviewLabel)),
            BuildAttachmentPreview());
    }

    public string BuildCompactSummaryText()
    {
        var summary = BuildUsageSummary();
        return string.Join(Environment.NewLine,
        [
            $"{summary.ArchiveAction}: {summary.ArchiveDetails}",
            $"Hauptvideo: {summary.MainVideo}",
            $"Weitere Videos: {summary.AdditionalVideos}",
            $"Audio: {summary.Audio}",
            $"AD: {summary.AudioDescription}",
            $"Untertitel: {summary.Subtitles}",
            $"Anhänge: {summary.Attachments}"
        ]);
    }

    public IReadOnlyList<string> GetReferencedInputFiles()
    {
        if (SkipMux)
        {
            return [];
        }

        var filePaths = new List<string>();
        filePaths.AddRange(VideoSources.Select(video => video.FilePath));

        if (!string.IsNullOrWhiteSpace(AudioDescriptionFilePath))
        {
            filePaths.Add(AudioDescriptionFilePath);
        }

        filePaths.AddRange(SubtitleFiles
            .Where(subtitle => !subtitle.IsEmbedded)
            .Select(subtitle => subtitle.FilePath));
        filePaths.AddRange(AttachmentFilePaths);

        return filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string BuildPreviewText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"mkvmerge.exe: {MkvMergePath}");
        builder.AppendLine();
        builder.AppendLine($"Titel: {Title}");
        builder.AppendLine($"Ausgabe: {OutputFilePath}");

        if (SkipMux)
        {
            builder.AppendLine();
            builder.AppendLine($"Kein Mux nötig: {SkipReason}");
        }
        else
        {
            builder.AppendLine("Videos:");

            foreach (var videoSource in VideoSources)
            {
                var defaultText = videoSource.IsDefaultTrack ? " (Standard)" : string.Empty;
                builder.AppendLine($"- {Path.GetFileName(videoSource.FilePath)} -> {videoSource.TrackName}{defaultText}");
            }

            builder.AppendLine($"Audio: {Path.GetFileName(PrimaryAudioFilePath)} -> {Metadata.AudioTrackName}");
            builder.AppendLine($"AD: {(AudioDescriptionFilePath is null ? "keine" : Path.GetFileName(AudioDescriptionFilePath))}");
            builder.AppendLine($"Untertitel: {(SubtitleFiles.Count == 0 ? "keine" : string.Join(", ", SubtitleFiles.Select(file => file.PreviewLabel)))}");
            builder.AppendLine($"Anhänge: {BuildAttachmentPreview()}");

            if (WorkingCopy is not null)
            {
                builder.AppendLine($"Arbeitskopie vorab: {WorkingCopy.SourceFilePath} -> {WorkingCopy.DestinationFilePath}");
            }

            builder.AppendLine();
            builder.AppendLine("Argumente:");
            builder.AppendLine(GetCommandLinePreview());
        }

        if (Notes.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Hinweise:");
            foreach (var note in Notes)
            {
                builder.AppendLine($"- {note}");
            }
        }

        return builder.ToString();
    }

    private static string EscapeArgument(string argument)
    {
        return argument.Contains(' ') ? $"\"{argument}\"" : argument;
    }

    private string BuildAttachmentPreview()
    {
        var parts = new List<string>();

        if (IncludePrimarySourceAttachments && PreservedAttachmentNames.Count > 0)
        {
            parts.AddRange(PreservedAttachmentNames.Select(name => $"{name} (aus Zieldatei)"));
        }

        parts.AddRange(AttachmentFilePaths
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>());

        return parts.Count == 0 ? "keine" : string.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}

public sealed record EpisodeUsageSummary(
    string ArchiveAction,
    string ArchiveDetails,
    string MainVideo,
    string AdditionalVideos,
    string Audio,
    string AudioDescription,
    string Subtitles,
    string Attachments)
{
    public static EpisodeUsageSummary CreatePending(string archiveAction, string archiveDetails)
    {
        return new EpisodeUsageSummary(
            archiveAction,
            archiveDetails,
            "(wird berechnet)",
            "(wird berechnet)",
            "(wird berechnet)",
            "(wird berechnet)",
            "(wird berechnet)",
            "(wird berechnet)");
    }
}

