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

    public string BuildCompactSummaryText()
    {
        if (SkipMux)
        {
            return SkipReason ?? "Die Archivdatei ist bereits vollstaendig.";
        }

        var lines = new List<string>();

        if (WorkingCopy is not null)
        {
            if (string.Equals(VideoSources[0].FilePath, WorkingCopy.SourceFilePath, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("Archiv-MKV vorhanden: Sie bleibt Basis; vor dem Mux wird eine Arbeitskopie erstellt.");
            }
            else
            {
                lines.Add("Archiv-MKV vorhanden: Neue Hauptquelle wird verwendet; fehlende Spuren werden aus dem Archiv ergaenzt.");
            }
        }
        else if (File.Exists(OutputFilePath))
        {
            lines.Add("Zieldatei vorhanden: Sie wird beim Mux aktualisiert oder ersetzt.");
        }
        else
        {
            lines.Add("Zieldatei noch nicht vorhanden: Die Episode wird direkt neu erstellt.");
        }

        lines.Add($"Hauptvideo: {Path.GetFileName(VideoSources[0].FilePath)}");

        if (VideoSources.Count > 1)
        {
            lines.Add("Weitere Videos: " + string.Join(", ", VideoSources.Skip(1).Select(source => Path.GetFileName(source.FilePath))));
        }

        lines.Add($"Audio: {Path.GetFileName(PrimaryAudioFilePath)}");
        lines.Add($"AD: {(string.IsNullOrWhiteSpace(AudioDescriptionFilePath) ? "keine" : Path.GetFileName(AudioDescriptionFilePath))}");
        lines.Add("Untertitel: " + (SubtitleFiles.Count == 0
            ? "keine"
            : string.Join(", ", SubtitleFiles.Select(file => file.PreviewLabel))));
        lines.Add("Anhaenge: " + BuildAttachmentPreview());

        return string.Join(Environment.NewLine, lines);
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
            builder.AppendLine($"Kein Mux noetig: {SkipReason}");
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
            builder.AppendLine($"Anhaenge: {BuildAttachmentPreview()}");

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
            parts.AddRange(PreservedAttachmentNames.Select(name => $"{name} (Archiv)"));
        }

        parts.AddRange(AttachmentFilePaths
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>());

        return parts.Count == 0 ? "keine" : string.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
