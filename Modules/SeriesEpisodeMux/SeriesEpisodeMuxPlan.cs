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
        string? audioDescriptionFilePath,
        int? audioDescriptionTrackId,
        IReadOnlyList<SubtitleFile> subtitleFiles,
        IReadOnlyList<string> attachmentFilePaths,
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
        AudioDescriptionFilePath = audioDescriptionFilePath;
        AudioDescriptionTrackId = audioDescriptionTrackId;
        SubtitleFiles = subtitleFiles;
        AttachmentFilePaths = attachmentFilePaths;
        Metadata = metadata;
        Notes = notes;
    }

    public string MkvMergePath { get; }
    public string OutputFilePath { get; }
    public string Title { get; }
    public IReadOnlyList<VideoSourcePlan> VideoSources { get; }
    public string PrimaryAudioFilePath { get; }
    public int PrimaryAudioTrackId { get; }
    public string? AudioDescriptionFilePath { get; }
    public int? AudioDescriptionTrackId { get; }
    public IReadOnlyList<SubtitleFile> SubtitleFiles { get; }
    public IReadOnlyList<string> AttachmentFilePaths { get; }
    public EpisodeTrackMetadata Metadata { get; }
    public IReadOnlyList<string> Notes { get; }

    public IReadOnlyList<string> BuildArguments()
    {
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

            primaryVideo.FilePath
        ]);

        foreach (var videoSource in VideoSources.Skip(1))
        {
            var trackId = videoSource.TrackId.ToString();
            arguments.AddRange(
            [
                "--no-audio",
                "--no-subtitles",
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
                videoSource.FilePath
            ]);
        }

        if (!string.IsNullOrWhiteSpace(AudioDescriptionFilePath) && AudioDescriptionTrackId is not null)
        {
            var adTrackId = AudioDescriptionTrackId.Value.ToString();
            arguments.AddRange(
            [
                "--no-video",
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
                AudioDescriptionFilePath
            ]);
        }

        foreach (var subtitle in SubtitleFiles)
        {
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

    public string GetCommandLinePreview()
    {
        return string.Join(Environment.NewLine, BuildArguments().Select(EscapeArgument));
    }

    public string BuildPreviewText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"mkvmerge.exe: {MkvMergePath}");
        builder.AppendLine();
        builder.AppendLine($"Titel: {Title}");
        builder.AppendLine("Videos:");

        foreach (var videoSource in VideoSources)
        {
            var defaultText = videoSource.IsDefaultTrack ? " (Standard)" : string.Empty;
            builder.AppendLine($"- {Path.GetFileName(videoSource.FilePath)} -> {videoSource.TrackName}{defaultText}");
        }

        builder.AppendLine($"Audio: {Path.GetFileName(PrimaryAudioFilePath)} -> {Metadata.AudioTrackName}");
        builder.AppendLine($"AD: {(AudioDescriptionFilePath is null ? "keine" : Path.GetFileName(AudioDescriptionFilePath))}");
        builder.AppendLine($"Untertitel: {(SubtitleFiles.Count == 0 ? "keine" : string.Join(", ", SubtitleFiles.Select(file => Path.GetFileName(file.FilePath))))}");
        builder.AppendLine($"Anhaenge: {(AttachmentFilePaths.Count == 0 ? "keine" : string.Join(", ", AttachmentFilePaths.Select(Path.GetFileName)))}");

        if (Notes.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Hinweise:");
            foreach (var note in Notes)
            {
                builder.AppendLine($"- {note}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Argumente:");
        builder.AppendLine(GetCommandLinePreview());
        return builder.ToString();
    }

    private static string EscapeArgument(string argument)
    {
        return argument.Contains(' ') ? $"\"{argument}\"" : argument;
    }
}
