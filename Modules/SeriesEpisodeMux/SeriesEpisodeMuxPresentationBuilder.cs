using System.Text;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

/// <summary>
/// Erzeugt lesbare Vorschauen und kompakte GUI-Zusammenfassungen aus einem Mux-Plan.
/// </summary>
internal static class SeriesEpisodeMuxPresentationBuilder
{
    public static string BuildCommandLinePreview(SeriesEpisodeMuxPlan plan)
    {
        return string.Join(Environment.NewLine, plan.BuildArguments().Select(EscapeArgument));
    }

    public static EpisodeUsageSummary BuildUsageSummary(SeriesEpisodeMuxPlan plan)
    {
        if (plan.SkipMux)
        {
            return EpisodeUsageSummary.CreatePending(
                plan.SkipReason ?? "Die Zieldatei ist bereits vollständig.",
                "keine weiteren Aktionen");
        }

        var archiveAction = plan.WorkingCopy is not null
            ? string.Equals(plan.VideoSources[0].FilePath, plan.WorkingCopy.SourceFilePath, StringComparison.OrdinalIgnoreCase)
                ? "Vorhandene Zieldatei bleibt Basis"
                : "Vorhandene Zieldatei wird mit neuer Hauptquelle aktualisiert"
            : File.Exists(plan.OutputFilePath)
                ? "MKV am Ziel bereits vorhanden"
                : "MKV am Ziel noch nicht vorhanden";

        var archiveDetails = plan.WorkingCopy is not null
            ? plan.WorkingCopy.IsReusable
                ? $"Arbeitskopie aktuell vorhanden: {Path.GetFileName(plan.WorkingCopy.DestinationFilePath)}"
                : $"Arbeitskopie wird erstellt: {Path.GetFileName(plan.WorkingCopy.DestinationFilePath)}"
            : File.Exists(plan.OutputFilePath)
                ? Path.GetFileName(plan.OutputFilePath)
                : "Neue MKV wird direkt erstellt";

        return new EpisodeUsageSummary(
            archiveAction,
            archiveDetails,
            CreateUsageEntry(Path.GetFileName(plan.VideoSources[0].FilePath), plan.UsageComparison.MainVideo),
            CreateUsageEntry(
                plan.VideoSources.Count > 1
                    ? string.Join(Environment.NewLine, plan.VideoSources.Skip(1).Select(source => Path.GetFileName(source.FilePath)))
                    : "(keine)",
                plan.UsageComparison.AdditionalVideos),
            CreateUsageEntry(Path.GetFileName(plan.PrimaryAudioFilePath), plan.UsageComparison.Audio),
            CreateUsageEntry(
                string.IsNullOrWhiteSpace(plan.AudioDescriptionFilePath) ? "(keine)" : Path.GetFileName(plan.AudioDescriptionFilePath),
                plan.UsageComparison.AudioDescription),
            CreateUsageEntry(
                plan.SubtitleFiles.Count == 0
                    ? "(keine)"
                    : string.Join(Environment.NewLine, plan.SubtitleFiles.Select(file => file.PreviewLabel)),
                plan.UsageComparison.Subtitles),
            CreateUsageEntry(BuildAttachmentPreview(plan), plan.UsageComparison.Attachments));
    }

    public static string BuildCompactSummaryText(SeriesEpisodeMuxPlan plan)
    {
        var summary = BuildUsageSummary(plan);
        return string.Join(Environment.NewLine,
        [
            $"{summary.ArchiveAction}: {summary.ArchiveDetails}",
            $"Hauptvideo: {summary.MainVideo.CurrentText}",
            $"Weitere Videos: {summary.AdditionalVideos.CurrentText}",
            $"Audio: {summary.Audio.CurrentText}",
            $"AD: {summary.AudioDescription.CurrentText}",
            $"Untertitel: {summary.Subtitles.CurrentText}",
            $"Anhänge: {summary.Attachments.CurrentText}"
        ]);
    }

    public static IReadOnlyList<string> GetReferencedInputFiles(SeriesEpisodeMuxPlan plan)
    {
        if (plan.SkipMux)
        {
            return [];
        }

        var filePaths = new List<string>();
        filePaths.AddRange(plan.VideoSources.Select(video => video.FilePath));

        if (!string.IsNullOrWhiteSpace(plan.AudioDescriptionFilePath))
        {
            filePaths.Add(plan.AudioDescriptionFilePath);
        }

        if (!string.IsNullOrWhiteSpace(plan.AttachmentSourcePath)
            && !string.Equals(plan.AttachmentSourcePath, plan.OutputFilePath, StringComparison.OrdinalIgnoreCase))
        {
            filePaths.Add(plan.AttachmentSourcePath);
        }

        filePaths.AddRange(plan.SubtitleFiles
            .Where(subtitle => !subtitle.IsEmbedded)
            .Select(subtitle => subtitle.FilePath));
        filePaths.AddRange(plan.AttachmentFilePaths);

        return filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string BuildPreviewText(SeriesEpisodeMuxPlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"mkvmerge.exe: {plan.MkvMergePath}");
        builder.AppendLine();
        builder.AppendLine($"Titel: {plan.Title}");
        builder.AppendLine($"Ausgabe: {plan.OutputFilePath}");

        if (plan.SkipMux)
        {
            builder.AppendLine();
            builder.AppendLine($"Kein Mux nötig: {plan.SkipReason}");
        }
        else
        {
            builder.AppendLine("Videos:");

            foreach (var videoSource in plan.VideoSources)
            {
                var defaultText = videoSource.IsDefaultTrack ? " (Standard)" : string.Empty;
                builder.AppendLine($"- {Path.GetFileName(videoSource.FilePath)} -> {videoSource.TrackName}{defaultText}");
            }

            builder.AppendLine($"Audio: {Path.GetFileName(plan.PrimaryAudioFilePath)} -> {plan.Metadata.AudioTrackName}");
            builder.AppendLine($"AD: {(plan.AudioDescriptionFilePath is null ? "keine" : Path.GetFileName(plan.AudioDescriptionFilePath))}");
            builder.AppendLine($"Untertitel: {(plan.SubtitleFiles.Count == 0 ? "keine" : string.Join(", ", plan.SubtitleFiles.Select(file => file.PreviewLabel)))}");
            builder.AppendLine($"Anhänge: {BuildAttachmentPreview(plan)}");

            if (plan.WorkingCopy is not null)
            {
                builder.AppendLine($"Arbeitskopie vorab: {plan.WorkingCopy.SourceFilePath} -> {plan.WorkingCopy.DestinationFilePath}");
            }

            builder.AppendLine();
            builder.AppendLine("Argumente:");
            builder.AppendLine(BuildCommandLinePreview(plan));
        }

        if (plan.Notes.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Hinweise:");
            foreach (var note in plan.Notes)
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

    private static EpisodeUsageEntry CreateUsageEntry(string currentText, ArchiveUsageChange? removedChange)
    {
        return new EpisodeUsageEntry(
            CurrentText: currentText,
            RemovedText: removedChange?.RemovedText,
            RemovedReason: removedChange?.Reason);
    }

    private static string BuildAttachmentPreview(SeriesEpisodeMuxPlan plan)
    {
        var parts = new List<string>();

        if ((plan.IncludePrimarySourceAttachments || !string.IsNullOrWhiteSpace(plan.AttachmentSourcePath))
            && plan.PreservedAttachmentNames.Count > 0)
        {
            // GUI-Vorschau soll sichtbar machen, dass diese Anhänge nicht aus neuen Dateien, sondern aus der Ziel-MKV stammen.
            parts.AddRange(plan.PreservedAttachmentNames.Select(name => $"{name} (aus Zieldatei)"));
        }

        parts.AddRange(plan.AttachmentFilePaths
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>());

        return parts.Count == 0 ? "keine" : string.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
