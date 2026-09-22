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

        return IsAudioDescriptionTrack(track.TrackName, track.IsVisualImpaired);
    }

    /// <summary>
    /// Prüft dieselbe AD-Heuristik ohne Container-Modell und eignet sich damit auch für rohe
    /// <c>mkvmerge --identify</c>-Antworten oder andere Vorstufen der Trackmodellierung.
    /// </summary>
    /// <param name="trackName">Bereits normalisierter oder roher Trackname.</param>
    /// <param name="isVisualImpaired">Accessibility-Flag der Audiospur.</param>
    /// <returns>
    /// <see langword="true"/>, wenn Name oder Accessibility-Flag auf Audiodeskription hindeuten;
    /// andernfalls <see langword="false"/>.
    /// </returns>
    public static bool IsAudioDescriptionTrack(string? trackName, bool isVisualImpaired)
    {
        return isVisualImpaired
            || EpisodeFileNameHelper.ContainsAudioDescriptionMarker(trackName);
    }

    /// <summary>
    /// Selects a marked AD track from an explicitly selected AD source. An unmarked
    /// single audio track is accepted; ambiguous multi-audio sources are never guessed.
    /// </summary>
    public static ContainerTrackMetadata SelectAudioDescriptionTrack(IEnumerable<ContainerTrackMetadata> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        var audioTracks = tracks.Where(IsAudioTrack).ToList();
        var markedTrack = audioTracks.FirstOrDefault(IsAudioDescriptionTrack);
        if (markedTrack is not null)
        {
            return markedTrack;
        }

        return audioTracks.Count == 1
            ? audioTracks[0]
            : throw new InvalidOperationException(audioTracks.Count == 0
                ? "In der AD-Datei wurde keine Audiospur gefunden."
                : "Die AD-Datei enthaelt mehrere Audiospuren, aber keine eindeutig markierte Audiodeskription.");
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
