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
            var skipSummary = plan.SkipUsageSummary
                ?? EpisodeUsageSummary.CreatePending(
                    plan.SkipReason ?? "Die Zieldatei ist bereits vollständig.",
                    "keine weiteren Aktionen");
            return skipSummary with { Notes = BuildUsageSummaryNotes(plan) };
        }

        var (archiveAction, archiveDetails) = BuildArchiveStatus(plan);
        var highlightAdditions = ShouldHighlightAdditions(plan);

        return new EpisodeUsageSummary(
            archiveAction,
            archiveDetails,
            CreateUsageEntry(
                BuildVideoUsageItems(plan, [plan.VideoSources[0]], highlightAdditions),
                plan.UsageComparison.MainVideo),
            CreateUsageEntry(
                BuildVideoUsageItems(plan, plan.VideoSources.Skip(1).ToList(), highlightAdditions),
                plan.UsageComparison.AdditionalVideos),
            CreateUsageEntry(
                BuildAudioUsageItems(plan, highlightAdditions),
                plan.UsageComparison.Audio),
            CreateUsageEntry(
                BuildAudioDescriptionUsageItems(plan, highlightAdditions),
                plan.UsageComparison.AudioDescription),
            CreateUsageEntry(
                BuildSubtitleUsageItems(plan, highlightAdditions),
                plan.UsageComparison.Subtitles),
            CreateUsageEntry(
                BuildAttachmentUsageItems(plan, highlightAdditions),
                plan.UsageComparison.Attachments))
        {
            Notes = BuildUsageSummaryNotes(plan)
        };
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
            $"Anhänge: {summary.Attachments.CurrentText}",
            .. BuildCompactSummaryNotes(plan)
        ]);
    }

    private static IReadOnlyList<string> BuildCompactSummaryNotes(SeriesEpisodeMuxPlan plan)
    {
        var notes = BuildUsageSummaryNotes(plan);
        if (notes.Count == 0)
        {
            return [];
        }

        return ["", "Hinweise:", .. notes.Select(note => "- " + note)];
    }

    public static IReadOnlyList<string> GetReferencedInputFiles(SeriesEpisodeMuxPlan plan)
    {
        if (plan.SkipMux)
        {
            return [];
        }

        var filePaths = new List<string>();
        filePaths.AddRange(plan.VideoSources.Select(video => video.FilePath));
        filePaths.AddRange(plan.AudioSources.Select(audio => audio.FilePath));
        filePaths.AddRange(plan.AudioDescriptionSources.Select(audioDescription => audioDescription.FilePath));

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
        builder.AppendLine($"{plan.ExecutionToolFileName}: {plan.ExecutionToolPath}");
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

            builder.AppendLine("Audio:");
            foreach (var audioSource in plan.AudioSources)
            {
                var defaultText = audioSource.IsDefaultTrack ? " (Standard)" : string.Empty;
                builder.AppendLine($"- {Path.GetFileName(audioSource.FilePath)} -> {audioSource.TrackName}{defaultText}");
            }

            builder.AppendLine($"AD: {BuildAudioDescriptionPreview(plan)}");
            builder.AppendLine($"Untertitel: {(plan.SubtitleFiles.Count == 0 ? "keine" : string.Join(", ", plan.SubtitleFiles.Select(file => file.PreviewLabel)))}");
            builder.AppendLine($"Anhänge: {BuildAttachmentPreview(plan)}");

            if (plan.HasHeaderEdits)
            {
                builder.AppendLine("Direkte Header-Anpassungen:");
                if (plan.ContainerTitleEdit is not null)
                {
                    var currentTitle = string.IsNullOrWhiteSpace(plan.ContainerTitleEdit.CurrentTitle)
                        ? "(leer)"
                        : plan.ContainerTitleEdit.CurrentTitle;
                    builder.AppendLine($"- MKV-Titel: {currentTitle} -> {plan.ContainerTitleEdit.ExpectedTitle}");
                }

                foreach (var headerEdit in plan.TrackHeaderEdits)
                {
                    var currentName = string.IsNullOrWhiteSpace(headerEdit.CurrentTrackName)
                        ? "(leer)"
                        : headerEdit.CurrentTrackName;
                    builder.AppendLine($"- {headerEdit.DisplayLabel}: {currentName} -> {headerEdit.ExpectedTrackName}");
                }
            }
            else if (plan.WorkingCopy is not null)
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

    private static (string ArchiveAction, string ArchiveDetails) BuildArchiveStatus(SeriesEpisodeMuxPlan plan)
    {
        if (plan.HasHeaderEdits)
        {
            return (
                "Zieldatei bleibt inhaltlich unverändert",
                BuildHeaderEditArchiveDetails(plan));
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

        return (archiveAction, archiveDetails);
    }

    private static EpisodeUsageEntry CreateUsageEntry(IReadOnlyList<EpisodeUsageItem> currentItems, ArchiveUsageChange? removedChange)
    {
        var normalizedItems = currentItems.Count == 0
            ? [new EpisodeUsageItem("(keine)", EpisodeUsageItemKind.Neutral)]
            : currentItems;
        return new EpisodeUsageEntry(
            CurrentText: string.Join(Environment.NewLine, normalizedItems.Select(item => item.Text)),
            RemovedText: removedChange?.RemovedText,
            RemovedReason: removedChange?.Reason,
            currentItems: normalizedItems);
    }

    private static IReadOnlyList<EpisodeUsageItem> BuildVideoUsageItems(
        SeriesEpisodeMuxPlan plan,
        IReadOnlyList<VideoSourcePlan> videoSources,
        bool highlightAdditions)
    {
        return videoSources.Count == 0
            ? [new EpisodeUsageItem("(keine)", EpisodeUsageItemKind.Neutral)]
            : videoSources
                .Select(source => BuildFileBackedUsageItem(
                    plan,
                    source.FilePath,
                    source.TrackName,
                    highlightAdditions))
                .ToList();
    }

    private static IReadOnlyList<EpisodeUsageItem> BuildAudioUsageItems(SeriesEpisodeMuxPlan plan, bool highlightAdditions)
    {
        return plan.AudioSources.Count == 0
            ? [new EpisodeUsageItem("(keine)", EpisodeUsageItemKind.Neutral)]
            : plan.AudioSources
                .Select(audioSource => BuildFileBackedUsageItem(
                    plan,
                    audioSource.FilePath,
                    audioSource.TrackName,
                    highlightAdditions))
                .ToList();
    }

    private static IReadOnlyList<EpisodeUsageItem> BuildAudioDescriptionUsageItems(SeriesEpisodeMuxPlan plan, bool highlightAdditions)
    {
        if (plan.AudioDescriptionSources.Count == 0)
        {
            return [new EpisodeUsageItem("(keine)", EpisodeUsageItemKind.Neutral)];
        }

        return plan.AudioDescriptionSources
            .Select(audioDescriptionSource => BuildFileBackedUsageItem(
                plan,
                audioDescriptionSource.FilePath,
                audioDescriptionSource.TrackName,
                highlightAdditions))
            .ToList();
    }

    private static IReadOnlyList<EpisodeUsageItem> BuildSubtitleUsageItems(SeriesEpisodeMuxPlan plan, bool highlightAdditions)
    {
        return plan.SubtitleFiles.Count == 0
            ? [new EpisodeUsageItem("(keine)", EpisodeUsageItemKind.Neutral)]
            : plan.SubtitleFiles
                .Select(subtitle => subtitle.IsEmbedded
                    ? new EpisodeUsageItem(
                        BuildExistingTargetDisplayText(subtitle.TrackName),
                        EpisodeUsageItemKind.Existing)
                    : BuildNewOrNeutralItem(Path.GetFileName(subtitle.FilePath), highlightAdditions))
                .ToList();
    }

    private static IReadOnlyList<EpisodeUsageItem> BuildAttachmentUsageItems(SeriesEpisodeMuxPlan plan, bool highlightAdditions)
    {
        var items = new List<EpisodeUsageItem>();

        if ((plan.IncludePrimarySourceAttachments || !string.IsNullOrWhiteSpace(plan.AttachmentSourcePath))
            && plan.PreservedAttachmentNames.Count > 0)
        {
            // GUI-Vorschau soll alle wiederverwendeten Bestandteile einheitlich als Ziel-MKV-Inhalt kennzeichnen.
            items.AddRange(plan.PreservedAttachmentNames.Select(name =>
                new EpisodeUsageItem(BuildExistingTargetDisplayText(name), EpisodeUsageItemKind.Existing)));
        }

        items.AddRange(plan.AttachmentFilePaths
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Select(name => BuildNewOrNeutralItem(name, highlightAdditions)));

        return items.Count == 0
            ? [new EpisodeUsageItem("keine", EpisodeUsageItemKind.Neutral)]
            : items
                .DistinctBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    private static string BuildAttachmentPreview(SeriesEpisodeMuxPlan plan)
    {
        var items = BuildAttachmentUsageItems(plan, highlightAdditions: false);
        return items.Count == 1 && string.Equals(items[0].Text, "keine", StringComparison.OrdinalIgnoreCase)
            ? "keine"
            : string.Join(", ", items.Select(item => item.Text));
    }

    private static string BuildAudioDescriptionPreview(SeriesEpisodeMuxPlan plan)
    {
        return plan.AudioDescriptionSources.Count == 0
            ? "keine"
            : string.Join(", ", plan.AudioDescriptionSources.Select(source => Path.GetFileName(source.FilePath)));
    }

    private static EpisodeUsageItem BuildFileBackedUsageItem(
        SeriesEpisodeMuxPlan plan,
        string filePath,
        string existingTargetLabel,
        bool highlightAdditions)
    {
        return string.Equals(filePath, plan.OutputFilePath, StringComparison.OrdinalIgnoreCase)
            ? new EpisodeUsageItem(BuildExistingTargetDisplayText(existingTargetLabel), EpisodeUsageItemKind.Existing)
            : BuildNewOrNeutralItem(Path.GetFileName(filePath), highlightAdditions);
    }

    private static EpisodeUsageItem BuildNewOrNeutralItem(string? text, bool highlightAdditions)
    {
        return new EpisodeUsageItem(
            string.IsNullOrWhiteSpace(text) ? "(unbekannt)" : text,
            highlightAdditions ? EpisodeUsageItemKind.Added : EpisodeUsageItemKind.Neutral);
    }

    private static bool ShouldHighlightAdditions(SeriesEpisodeMuxPlan plan)
    {
        return plan.WorkingCopy is not null
            || plan.HasHeaderEdits
            || File.Exists(plan.OutputFilePath)
            || plan.UsageComparison.MainVideo is not null
            || plan.UsageComparison.AdditionalVideos is not null
            || plan.UsageComparison.Audio is not null
            || plan.UsageComparison.AudioDescription is not null
            || plan.UsageComparison.Subtitles is not null
            || plan.UsageComparison.Attachments is not null;
    }

    private static IReadOnlyList<string> BuildUsageSummaryNotes(SeriesEpisodeMuxPlan plan)
    {
        return plan.Notes
            .Where(IsUsageSummaryNoteRelevant)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsUsageSummaryNoteRelevant(string note)
    {
        return !note.StartsWith("Archiv-MKV bereits vorhanden.", StringComparison.OrdinalIgnoreCase)
            && !note.StartsWith("Die vorhandene Archivdatei liefert", StringComparison.OrdinalIgnoreCase)
            && !note.StartsWith("Vorhandene Videospur wird beibehalten:", StringComparison.OrdinalIgnoreCase)
            && !note.StartsWith("Es wird weder eine Arbeitskopie", StringComparison.OrdinalIgnoreCase)
            && !note.StartsWith("Alle Inhalte sind bereits vorhanden.", StringComparison.OrdinalIgnoreCase)
            && !note.StartsWith("Zieldatei bereits vollständig.", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildExistingTargetDisplayText(string value)
    {
        return $"Aus Zieldatei: {value}";
    }

    private static string BuildHeaderEditArchiveDetails(SeriesEpisodeMuxPlan plan)
    {
        var hasTitleEdit = plan.ContainerTitleEdit is not null;
        var hasTrackEdits = plan.HasTrackHeaderEdits;

        return (hasTitleEdit, hasTrackEdits) switch
        {
            (true, true) => "Es werden nur der MKV-Titel und die Benennungen der relevanten Spuren direkt im Header vereinheitlicht",
            (true, false) => "Es wird nur der MKV-Titel direkt im Header vereinheitlicht",
            _ => "Es werden nur die Benennungen der relevanten Spuren direkt im Header vereinheitlicht"
        };
    }
}
