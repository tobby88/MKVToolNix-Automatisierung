using System.Text;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

public sealed class SeriesEpisodeMuxPlan
{
    public SeriesEpisodeMuxPlan(
        string mkvMergePath,
        string outputFilePath,
        string title,
        string primaryVideoFilePath,
        int primaryVideoTrackId,
        int primaryAudioTrackId,
        string? audioDescriptionFilePath,
        int? audioDescriptionTrackId,
        IReadOnlyList<SubtitleFile> subtitleFiles,
        string? attachmentFilePath,
        EpisodeTrackMetadata metadata)
    {
        MkvMergePath = mkvMergePath;
        OutputFilePath = outputFilePath;
        Title = title;
        PrimaryVideoFilePath = primaryVideoFilePath;
        PrimaryVideoTrackId = primaryVideoTrackId;
        PrimaryAudioTrackId = primaryAudioTrackId;
        AudioDescriptionFilePath = audioDescriptionFilePath;
        AudioDescriptionTrackId = audioDescriptionTrackId;
        SubtitleFiles = subtitleFiles;
        AttachmentFilePath = attachmentFilePath;
        Metadata = metadata;
    }

    public string MkvMergePath { get; }
    public string OutputFilePath { get; }
    public string Title { get; }
    public string PrimaryVideoFilePath { get; }
    public int PrimaryVideoTrackId { get; }
    public int PrimaryAudioTrackId { get; }
    public string? AudioDescriptionFilePath { get; }
    public int? AudioDescriptionTrackId { get; }
    public IReadOnlyList<SubtitleFile> SubtitleFiles { get; }
    public string? AttachmentFilePath { get; }
    public EpisodeTrackMetadata Metadata { get; }

    public IReadOnlyList<string> BuildArguments()
    {
        var videoTrackId = PrimaryVideoTrackId.ToString();
        var audioTrackId = PrimaryAudioTrackId.ToString();
        var arguments = new List<string>
        {
            "--output",
            OutputFilePath,
            "--title",
            Title,

            "--language",
            $"{videoTrackId}:de",
            "--track-name",
            $"{videoTrackId}:{Metadata.VideoTrackName}",
            "--stereo-mode",
            $"{videoTrackId}:mono",
            "--original-flag",
            $"{videoTrackId}:yes",

            "--language",
            $"{audioTrackId}:de",
            "--track-name",
            $"{audioTrackId}:{Metadata.AudioTrackName}",
            "--original-flag",
            $"{audioTrackId}:yes",

            PrimaryVideoFilePath
        };

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

        if (!string.IsNullOrWhiteSpace(AttachmentFilePath))
        {
            arguments.AddRange(
            [
                "--attachment-mime-type",
                "text/plain",
                "--attach-file",
                AttachmentFilePath
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
        builder.AppendLine($"Video: {PrimaryVideoFilePath}");
        builder.AppendLine($"AD: {AudioDescriptionFilePath ?? "keine"}");
        builder.AppendLine($"Untertitel: {(SubtitleFiles.Count == 0 ? "keine" : string.Join(", ", SubtitleFiles.Select(file => Path.GetFileName(file.FilePath))))}");
        builder.AppendLine($"Anhang: {AttachmentFilePath ?? "keiner"}");
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
