namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Zentrale Präferenzregeln für Codec-Vergleiche beim Archivabgleich.
/// </summary>
internal static class MediaCodecPreferenceHelper
{
    /// <summary>
    /// Liefert die bewusst H.264-bevorzugende Codec-Reihenfolge, nicht eine gemessene
    /// Bildqualitätsrangfolge. Auflösung und andere Qualitätsmerkmale werden vom Planer
    /// getrennt bewertet; ein neuerer Codec allein rechtfertigt keinen Austausch.
    /// </summary>
    public static int GetVideoCodecPreferenceRank(string codecLabel)
    {
        return codecLabel.ToUpperInvariant() switch
        {
            "H.264" => 0,
            "H.265" => 1,
            _ => 2
        };
    }
}
