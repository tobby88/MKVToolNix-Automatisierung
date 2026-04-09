using System.Diagnostics;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Kapselt die Wiederverwendung vorhandener Archiv-Anhänge inklusive heuristischer TXT-Zuordnung zu ersetzten Videospuren.
/// </summary>
public sealed partial class SeriesArchiveService
{
    private static readonly TimeSpan AttachmentExtractionTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Bestimmt, welche vorhandenen Attachments einer Archiv-MKV im finalen Mux-Plan erhalten bleiben dürfen.
    /// </summary>
    /// <param name="mkvMergePath">Pfad zur aktuell verwendeten <c>mkvmerge</c>-Executable, aus dem bei Bedarf <c>mkvextract</c> abgeleitet wird.</param>
    /// <param name="outputPath">Pfad der bereits vorhandenen Archivdatei.</param>
    /// <param name="existingAttachments">Alle bereits im Archivcontainer vorhandenen Attachments.</param>
    /// <param name="existingVideoTracks">Vorhandene Archivvideospuren zur Zuordnung eingebetteter TXT-Anhänge.</param>
    /// <param name="selectedVideoTracks">Final ausgewählte Videospuren des neuen Plans.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>
    /// Eine Wiederverwendungsentscheidung für Archiv-Attachments. Nicht-TXT-Anhänge bleiben grundsätzlich erhalten;
    /// TXT-Anhänge werden nur dann verworfen, wenn sie eindeutig einer entfallenden Videospur zugeordnet werden können.
    /// </returns>
    private async Task<AttachmentReusePlan> BuildAttachmentReusePlanAsync(
        string mkvMergePath,
        string outputPath,
        IReadOnlyList<ContainerAttachmentMetadata> existingAttachments,
        IReadOnlyList<ContainerTrackMetadata> existingVideoTracks,
        IReadOnlyList<VideoTrackSelection> selectedVideoTracks,
        CancellationToken cancellationToken)
    {
        if (existingAttachments.Count == 0)
        {
            return AttachmentReusePlan.None;
        }

        var preservedAttachmentIds = new HashSet<int>(
            existingAttachments
                .Where(attachment => !IsTextAttachment(attachment.FileName))
                .Select(attachment => attachment.Id));
        var textAttachments = existingAttachments
            .Where(attachment => IsTextAttachment(attachment.FileName))
            .ToList();
        var keptExistingTrackIds = selectedVideoTracks
            .Where(selection => string.Equals(selection.FilePath, outputPath, StringComparison.OrdinalIgnoreCase))
            .Select(selection => selection.TrackId)
            .ToHashSet();
        var removedExistingTrackIds = existingVideoTracks
            .Where(track => !keptExistingTrackIds.Contains(track.TrackId))
            .Select(track => track.TrackId)
            .ToHashSet();
        var singleExistingVideo = existingVideoTracks.Count == 1
            ? existingVideoTracks[0]
            : null;
        var keepsSingleExistingVideo = singleExistingVideo is not null
            && keptExistingTrackIds.Contains(singleExistingVideo.TrackId);

        if (textAttachments.Count > 0)
        {
            var textAttachmentMatches = await MatchTextAttachmentsToExistingTracksAsync(
                mkvMergePath,
                outputPath,
                textAttachments,
                existingVideoTracks,
                cancellationToken);
            var uniquelyMatchedRemovedAttachmentIds = textAttachmentMatches
                .Where(match => match.IsStrongMatch && match.MatchedTrackId is not null)
                .GroupBy(match => match.MatchedTrackId!.Value)
                .Where(group => group.Count() == 1)
                .Select(group => group.Single())
                .Where(match => removedExistingTrackIds.Contains(match.MatchedTrackId!.Value))
                .Select(match => match.AttachmentId)
                .ToHashSet();

            foreach (var textAttachment in textAttachments)
            {
                if (!uniquelyMatchedRemovedAttachmentIds.Contains(textAttachment.Id))
                {
                    preservedAttachmentIds.Add(textAttachment.Id);
                }
            }
        }

        if (singleExistingVideo is not null
            && !keepsSingleExistingVideo
            && textAttachments.Count == 1
            && preservedAttachmentIds.Contains(textAttachments[0].Id))
        {
            preservedAttachmentIds.Remove(textAttachments[0].Id);
        }

        if (preservedAttachmentIds.Count == 0)
        {
            return AttachmentReusePlan.None;
        }

        var preservedAttachments = existingAttachments
            .Where(attachment => preservedAttachmentIds.Contains(attachment.Id))
            .ToList();
        var preservedAttachmentNames = preservedAttachments
            .Select(attachment => attachment.FileName)
            .ToList();
        var primaryUsesArchive = selectedVideoTracks.Count > 0
            && string.Equals(selectedVideoTracks[0].FilePath, outputPath, StringComparison.OrdinalIgnoreCase);

        return primaryUsesArchive
            ? new AttachmentReusePlan(
                IncludePrimarySourceAttachments: true,
                PrimarySourceAttachmentIds: preservedAttachments.Select(attachment => attachment.Id).ToList(),
                AttachmentSourcePath: null,
                AttachmentSourceAttachmentIds: null,
                PreservedAttachmentNames: preservedAttachmentNames)
            : new AttachmentReusePlan(
                IncludePrimarySourceAttachments: false,
                PrimarySourceAttachmentIds: null,
                AttachmentSourcePath: outputPath,
                AttachmentSourceAttachmentIds: preservedAttachments.Select(attachment => attachment.Id).ToList(),
                PreservedAttachmentNames: preservedAttachmentNames);
    }

    /// <summary>
    /// Liest für Doppelfolgen-Heuristiken konservativ nur dann eine eingebettete TXT-Laufzeit aus dem Archiv,
    /// wenn genau ein TXT-Anhang vorhanden ist. Mehrdeutige Anhangssituationen werden bewusst ignoriert.
    /// </summary>
    /// <param name="mkvMergePath">Pfad zur verwendeten <c>mkvmerge.exe</c>.</param>
    /// <param name="containerPath">Bereits vorhandene Archiv-MKV.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Die gefundene Laufzeit oder <see langword="null"/> bei fehlendem oder mehrdeutigem TXT-Anhang.</returns>
    internal async Task<TimeSpan?> TryReadUniqueEmbeddedTextDurationAsync(
        string mkvMergePath,
        string containerPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerPath) || !File.Exists(containerPath))
        {
            return null;
        }

        var container = await _probeService.ReadContainerMetadataAsync(mkvMergePath, containerPath, cancellationToken);
        var textAttachments = container.Attachments
            .Where(attachment => IsTextAttachment(attachment.FileName))
            .ToList();
        if (textAttachments.Count != 1)
        {
            return null;
        }

        var details = await TryReadEmbeddedTextAttachmentDetailsAsync(
            mkvMergePath,
            containerPath,
            textAttachments[0],
            cancellationToken);
        return details.Duration;
    }

    private async Task<IReadOnlyList<AttachmentTrackMatch>> MatchTextAttachmentsToExistingTracksAsync(
        string mkvMergePath,
        string outputPath,
        IReadOnlyList<ContainerAttachmentMetadata> textAttachments,
        IReadOnlyList<ContainerTrackMetadata> existingVideoTracks,
        CancellationToken cancellationToken)
    {
        var matches = new List<AttachmentTrackMatch>(textAttachments.Count);

        foreach (var textAttachment in textAttachments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var details = await TryReadEmbeddedTextAttachmentDetailsAsync(
                mkvMergePath,
                outputPath,
                textAttachment,
                cancellationToken);
            var profile = BuildAttachmentTextHeuristicProfile(textAttachment.FileName, details);
            matches.Add(MatchTextAttachmentToTrack(textAttachment, profile, existingVideoTracks));
        }

        return matches;
    }

    private static AttachmentTrackMatch MatchTextAttachmentToTrack(
        ContainerAttachmentMetadata attachment,
        AttachmentTextHeuristicProfile profile,
        IReadOnlyList<ContainerTrackMetadata> existingVideoTracks)
    {
        if (!profile.HasUsableSignals)
        {
            return new AttachmentTrackMatch(attachment.Id, attachment.FileName, null, false);
        }

        var candidateTracks = existingVideoTracks
            .Where(track => MatchesAttachmentProfile(track, profile))
            .ToList();
        if (candidateTracks.Count != 1)
        {
            return new AttachmentTrackMatch(attachment.Id, attachment.FileName, null, false);
        }

        var matchedTrack = candidateTracks[0];
        var strongMatch = profile.HasCodecAndResolution
            || (profile.LanguageCode is not null && (profile.CodecLabel is not null || profile.ResolutionLabel is not null))
            || (profile.HasExplicitLanguageMarker
                && existingVideoTracks.Count(track => string.Equals(
                    MediaLanguageHelper.NormalizeMuxLanguageCode(track.Language),
                    profile.LanguageCode,
                    StringComparison.OrdinalIgnoreCase)) == 1);

        return new AttachmentTrackMatch(
            attachment.Id,
            attachment.FileName,
            strongMatch ? matchedTrack.TrackId : null,
            strongMatch);
    }

    private static bool MatchesAttachmentProfile(ContainerTrackMetadata track, AttachmentTextHeuristicProfile profile)
    {
        if (profile.LanguageCode is not null
            && !string.Equals(
                MediaLanguageHelper.NormalizeMuxLanguageCode(track.Language),
                profile.LanguageCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (profile.CodecLabel is not null
            && !string.Equals(track.CodecLabel, profile.CodecLabel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (profile.ResolutionLabel is not null
            && !string.Equals(
                ResolutionLabel.FromWidth(track.VideoWidth).Value,
                profile.ResolutionLabel,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static AttachmentTextHeuristicProfile BuildAttachmentTextHeuristicProfile(
        string fileName,
        CompanionTextDetails details)
    {
        var languageSignals = string.Join(
            " ",
            new[] { fileName, details.Title, details.Topic, details.Sender }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        var hasExplicitLanguageMarker = TryInferExplicitLanguageCode(languageSignals, out var languageCode);
        if (languageCode is null && (!string.IsNullOrWhiteSpace(details.Title) || !string.IsNullOrWhiteSpace(fileName)))
        {
            languageCode = "de";
        }

        var codecLabel = TryInferCodecLabel(details.MediaUrl);
        var resolutionLabel = TryInferResolutionLabel(details.MediaUrl);

        return new AttachmentTextHeuristicProfile(
            LanguageCode: languageCode,
            HasExplicitLanguageMarker: hasExplicitLanguageMarker,
            CodecLabel: codecLabel,
            ResolutionLabel: resolutionLabel);
    }

    private static bool TryInferExplicitLanguageCode(string? value, out string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            languageCode = null;
            return false;
        }

        if (ContainsAny(value, "op platt", "plattd", "plattdu", "plattdü"))
        {
            languageCode = "nds";
            return true;
        }

        if (ContainsAny(value, "englisch", "english", "originalversion"))
        {
            languageCode = "en";
            return true;
        }

        if (ContainsAny(value, "deutsch", "german"))
        {
            languageCode = "de";
            return true;
        }

        languageCode = null;
        return false;
    }

    private static string? TryInferCodecLabel(string? mediaUrl)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl))
        {
            return null;
        }

        if (ContainsAny(mediaUrl, "hevc", "h265", "h.265"))
        {
            return "H.265";
        }

        return mediaUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            || ContainsAny(mediaUrl, "h264", "h.264", "avc")
            ? "H.264"
            : null;
    }

    private static string? TryInferResolutionLabel(string? mediaUrl)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl))
        {
            return null;
        }

        if (ContainsAny(mediaUrl, "2160", "uhd", "4k"))
        {
            return "UHD";
        }

        if (ContainsAny(mediaUrl, "1080"))
        {
            return "FHD";
        }

        if (ContainsAny(mediaUrl, "720") || ContainsAny(mediaUrl, "hd.mp4", "_hd.", "-hd.", "/hd."))
        {
            return "HD";
        }

        if (ContainsAny(mediaUrl, "540", "sd.mp4", "_sd.", "-sd.", "/sd."))
        {
            return "SD";
        }

        return null;
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<CompanionTextDetails> TryReadEmbeddedTextAttachmentDetailsAsync(
        string mkvMergePath,
        string containerPath,
        ContainerAttachmentMetadata attachment,
        CancellationToken cancellationToken)
    {
        var mkvExtractPath = ResolveMkvExtractPath(mkvMergePath);
        if (!File.Exists(mkvExtractPath))
        {
            return CompanionTextDetails.Empty;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-embedded-attachments", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var tempFilePath = Path.Combine(
            tempDirectory,
            $"{attachment.Id}{Path.GetExtension(attachment.FileName)}");

        try
        {
            var exitCode = await RunMkvExtractAttachmentAsync(
                mkvExtractPath,
                containerPath,
                attachment.Id,
                tempFilePath,
                cancellationToken);
            if (exitCode != 0 || !File.Exists(tempFilePath))
            {
                return CompanionTextDetails.Empty;
            }

            return CompanionTextMetadataReader.ReadDetailed(tempFilePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return CompanionTextDetails.Empty;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
            catch
            {
                // Die TXT-Heuristik ist nur ein optionaler Präzisionsgewinn.
                // Ein liegen gebliebener Temp-Ordner darf die eigentliche Planung nicht scheitern lassen.
            }
        }
    }

    private static string ResolveMkvExtractPath(string mkvMergePath)
    {
        var toolDirectory = Path.GetDirectoryName(mkvMergePath);
        return string.IsNullOrWhiteSpace(toolDirectory)
            ? "mkvextract.exe"
            : Path.Combine(toolDirectory, "mkvextract.exe");
    }

    private static async Task<int> RunMkvExtractAttachmentAsync(
        string mkvExtractPath,
        string containerPath,
        int attachmentId,
        string outputPath,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(AttachmentExtractionTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = mkvExtractPath,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("attachments");
        process.StartInfo.ArgumentList.Add(containerPath);
        process.StartInfo.ArgumentList.Add($"{attachmentId}:{outputPath}");

        process.Start();
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort für Cancel. Die eigentliche Abbruchsemantik kommt über das geworfene Token.
            }
        });

        using var timeoutRegistration = linkedCancellation.Token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        try
        {
            await process.WaitForExitAsync(linkedCancellation.Token);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return -1;
        }

    }

    private static bool IsTextAttachment(string fileName)
    {
        return fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildAttachmentPathsForUsedVideos(IEnumerable<string> usedVideoPaths)
    {
        return usedVideoPaths
            .Select(path => Path.ChangeExtension(path, ".txt"))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record AttachmentReusePlan(
        bool IncludePrimarySourceAttachments,
        IReadOnlyList<int>? PrimarySourceAttachmentIds,
        string? AttachmentSourcePath,
        IReadOnlyList<int>? AttachmentSourceAttachmentIds,
        IReadOnlyList<string> PreservedAttachmentNames)
    {
        public static AttachmentReusePlan None { get; } = new(
            IncludePrimarySourceAttachments: false,
            PrimarySourceAttachmentIds: null,
            AttachmentSourcePath: null,
            AttachmentSourceAttachmentIds: null,
            PreservedAttachmentNames: []);
    }

    private sealed record AttachmentTextHeuristicProfile(
        string? LanguageCode,
        bool HasExplicitLanguageMarker,
        string? CodecLabel,
        string? ResolutionLabel)
    {
        public bool HasCodecAndResolution => CodecLabel is not null && ResolutionLabel is not null;

        public bool HasUsableSignals => LanguageCode is not null || CodecLabel is not null || ResolutionLabel is not null;
    }

    private sealed record AttachmentTrackMatch(
        int AttachmentId,
        string FileName,
        int? MatchedTrackId,
        bool IsStrongMatch);
}
