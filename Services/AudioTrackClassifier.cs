using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Bündelt die projektweiten Heuristiken zur Unterscheidung zwischen normalen Audiospuren
/// und Audiodeskriptionsspuren.
/// </summary>
internal static class AudioTrackClassifier
{
    /// <summary>
    /// Prüft, ob eine Container-Audiospur fachlich wie eine Audiodeskriptionsspur behandelt werden soll.
    /// </summary>
    /// <param name="track">Zu bewertende Container-Audiospur.</param>
    /// <returns>
    /// <see langword="true"/>, wenn Name oder Accessibility-Flags auf Audiodeskription hindeuten;
    /// andernfalls <see langword="false"/>.
    /// </returns>
    public static bool IsAudioDescriptionTrack(ContainerTrackMetadata track)
    {
        ArgumentNullException.ThrowIfNull(track);

        return track.IsVisualImpaired
            || track.TrackName.Contains("sehbehinder", StringComparison.OrdinalIgnoreCase)
            || track.TrackName.Contains("audiodeskrip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Filtert aus einer Trackliste nur normale Audiospuren heraus und entfernt dabei erkannte AD-Spuren.
    /// </summary>
    /// <param name="tracks">Beliebige Container-Tracks oder bereits vorgefilterte Audiospuren.</param>
    /// <returns>Normale Audiospuren in der ursprünglichen Reihenfolge.</returns>
    public static IReadOnlyList<ContainerTrackMetadata> GetNormalAudioTracks(IEnumerable<ContainerTrackMetadata> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        return tracks
            .Where(IsAudioTrack)
            .Where(track => !IsAudioDescriptionTrack(track))
            .ToList();
    }

    /// <summary>
    /// Liefert die für frische Quellen bevorzugten normalen Audiospuren.
    /// </summary>
    /// <param name="tracks">Beliebige Container-Tracks oder bereits vorgefilterte Audiospuren.</param>
    /// <returns>
    /// Zuerst alle normalen Audiospuren ohne AD-Marker. Wenn die Heuristik ausnahmsweise jede
    /// vorhandene Audiospur als AD markieren würde, fallen die Ergebnisse konservativ auf alle
    /// Audiospuren zurück, damit eine Einspur-Quelle nicht stumm geplant wird.
    /// </returns>
    public static IReadOnlyList<ContainerTrackMetadata> GetPreferredNormalAudioTracks(IEnumerable<ContainerTrackMetadata> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var audioTracks = tracks
            .Where(IsAudioTrack)
            .ToList();
        var normalTracks = audioTracks
            .Where(track => !IsAudioDescriptionTrack(track))
            .ToList();

        return normalTracks.Count > 0
            ? normalTracks
            : audioTracks;
    }

    private static bool IsAudioTrack(ContainerTrackMetadata track)
    {
        return string.Equals(track.Type, "audio", StringComparison.OrdinalIgnoreCase);
    }
}
