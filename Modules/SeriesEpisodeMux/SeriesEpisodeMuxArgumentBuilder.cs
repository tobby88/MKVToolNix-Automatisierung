namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

/// <summary>
/// Baut aus einem vollständig aufgelösten Plan die konkrete mkvmerge-Argumentliste.
/// </summary>
internal static class SeriesEpisodeMuxArgumentBuilder
{
    public static IReadOnlyList<string> Build(SeriesEpisodeMuxPlan plan)
    {
        if (plan.SkipMux)
        {
            return [];
        }

        var arguments = new List<string>
        {
            "--output",
            plan.OutputFilePath,
            "--title",
            plan.Title
        };

        AppendPrimarySourceOptions(arguments, plan);
        AppendPrimaryVideo(arguments, plan);
        AppendAdditionalVideos(arguments, plan);
        AppendAudioDescription(arguments, plan);
        AppendSubtitles(arguments, plan);
        AppendAttachments(arguments, plan);

        return arguments;
    }

    private static void AppendPrimarySourceOptions(List<string> arguments, SeriesEpisodeMuxPlan plan)
    {
        if (plan.PrimarySourceAudioTrackIds is { Count: > 0 })
        {
            arguments.AddRange(["--audio-tracks", string.Join(",", plan.PrimarySourceAudioTrackIds)]);
        }

        if (plan.PrimarySourceSubtitleTrackIds is { Count: 0 })
        {
            arguments.Add("--no-subtitles");
        }
        else if (plan.PrimarySourceSubtitleTrackIds is { Count: > 0 })
        {
            arguments.AddRange(["--subtitle-tracks", string.Join(",", plan.PrimarySourceSubtitleTrackIds)]);
        }

        if (!plan.IncludePrimarySourceAttachments)
        {
            arguments.Add("--no-attachments");
        }
        else if (plan.PrimarySourceAttachmentIds is { Count: > 0 })
        {
            arguments.AddRange(["--attachments", string.Join(",", plan.PrimarySourceAttachmentIds)]);
        }
    }

    private static void AppendPrimaryVideo(List<string> arguments, SeriesEpisodeMuxPlan plan)
    {
        var primaryVideo = plan.VideoSources[0];
        var primaryVideoTrackId = primaryVideo.TrackId.ToString();
        var primaryAudioTrackId = plan.PrimaryAudioTrackId.ToString();

        arguments.AddRange(
        [
            "--video-tracks",
            primaryVideoTrackId,
            "--language",
            $"{primaryVideoTrackId}:{primaryVideo.LanguageCode}",
            "--track-name",
            $"{primaryVideoTrackId}:{primaryVideo.TrackName}",
            "--default-track-flag",
            $"{primaryVideoTrackId}:yes",
            "--stereo-mode",
            $"{primaryVideoTrackId}:mono",
            "--original-flag",
            $"{primaryVideoTrackId}:yes",

            "--language",
            $"{primaryAudioTrackId}:{plan.Metadata.AudioLanguageCode}",
            "--track-name",
            $"{primaryAudioTrackId}:{plan.Metadata.AudioTrackName}",
            "--default-track-flag",
            $"{primaryAudioTrackId}:yes",
            "--original-flag",
            $"{primaryAudioTrackId}:yes",

            plan.ResolveRuntimeFilePath(primaryVideo.FilePath)
        ]);
    }

    private static void AppendAdditionalVideos(List<string> arguments, SeriesEpisodeMuxPlan plan)
    {
        foreach (var videoSource in plan.VideoSources.Skip(1))
        {
            var trackId = videoSource.TrackId.ToString();
            arguments.AddRange(
            [
                "--no-audio",
                "--no-subtitles",
                "--no-attachments",
                "--video-tracks",
                trackId,
                "--language",
                $"{trackId}:{videoSource.LanguageCode}",
                "--track-name",
                $"{trackId}:{videoSource.TrackName}",
                "--default-track-flag",
                $"{trackId}:no",
                "--stereo-mode",
                $"{trackId}:mono",
                "--original-flag",
                $"{trackId}:yes",
                plan.ResolveRuntimeFilePath(videoSource.FilePath)
            ]);
        }
    }

    private static void AppendAudioDescription(List<string> arguments, SeriesEpisodeMuxPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.AudioDescriptionFilePath) || plan.AudioDescriptionTrackId is null)
        {
            return;
        }

        var adTrackId = plan.AudioDescriptionTrackId.Value.ToString();
        arguments.AddRange(
        [
            "--no-video",
            "--no-subtitles",
            "--no-attachments",
            "--audio-tracks",
            adTrackId,
            "--language",
            $"{adTrackId}:{plan.Metadata.AudioDescriptionLanguageCode}",
            "--track-name",
            $"{adTrackId}:{plan.Metadata.AudioDescriptionTrackName}",
            "--default-track-flag",
            $"{adTrackId}:no",
            "--visual-impaired-flag",
            $"{adTrackId}:yes",
            "--original-flag",
            $"{adTrackId}:yes",
            plan.ResolveRuntimeFilePath(plan.AudioDescriptionFilePath)
        ]);
    }

    private static void AppendSubtitles(List<string> arguments, SeriesEpisodeMuxPlan plan)
    {
        foreach (var subtitle in plan.SubtitleFiles)
        {
            if (subtitle.IsEmbedded && subtitle.EmbeddedTrackId is int embeddedTrackId)
            {
                AppendEmbeddedSubtitle(arguments, plan, subtitle, embeddedTrackId);
                continue;
            }

            AppendExternalSubtitle(arguments, subtitle);
        }
    }

    private static void AppendEmbeddedSubtitle(
        List<string> arguments,
        SeriesEpisodeMuxPlan plan,
        SubtitleFile subtitle,
        int embeddedTrackId)
    {
        var embeddedTrackIdText = embeddedTrackId.ToString();
        arguments.AddRange(
        [
            "--no-video",
            "--no-audio",
            "--no-attachments",
            "--subtitle-tracks",
            embeddedTrackIdText,
            "--language",
            $"{embeddedTrackIdText}:{subtitle.LanguageCode}",
            "--track-name",
            $"{embeddedTrackIdText}:{subtitle.TrackName}",
            "--default-track-flag",
            $"{embeddedTrackIdText}:no",
            "--hearing-impaired-flag",
            subtitle.IsHearingImpaired ? $"{embeddedTrackIdText}:yes" : $"{embeddedTrackIdText}:no",
            "--original-flag",
            $"{embeddedTrackIdText}:yes",
            plan.ResolveRuntimeFilePath(subtitle.FilePath)
        ]);
    }

    private static void AppendExternalSubtitle(List<string> arguments, SubtitleFile subtitle)
    {
        arguments.AddRange(
        [
            "--language",
            $"0:{subtitle.LanguageCode}",
            "--track-name",
            $"0:{subtitle.TrackName}",
            "--default-track-flag",
            "0:no",
            "--hearing-impaired-flag",
            subtitle.IsHearingImpaired ? "0:yes" : "0:no",
            "--original-flag",
            "0:yes",
            subtitle.FilePath
        ]);
    }

    private static void AppendAttachments(List<string> arguments, SeriesEpisodeMuxPlan plan)
    {
        if (!string.IsNullOrWhiteSpace(plan.AttachmentSourcePath))
        {
            // Separate Attachment-Quelle hält vorhandene TXT-Anhänge am Leben, auch wenn die Hauptquelle ausgetauscht wurde.
            arguments.AddRange(
            [
                "--no-video",
                "--no-audio",
                "--no-subtitles"
            ]);

            if (plan.AttachmentSourceAttachmentIds is { Count: > 0 })
            {
                arguments.AddRange(["--attachments", string.Join(",", plan.AttachmentSourceAttachmentIds)]);
            }
            else
            {
                arguments.Add("--no-attachments");
            }

            arguments.Add(plan.ResolveRuntimeFilePath(plan.AttachmentSourcePath));
        }

        foreach (var attachmentFilePath in plan.AttachmentFilePaths)
        {
            arguments.AddRange(
            [
                "--attachment-mime-type",
                "text/plain",
                "--attach-file",
                attachmentFilePath
            ]);
        }
    }
}
