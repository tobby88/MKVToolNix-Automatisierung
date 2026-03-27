namespace MkvToolnixAutomatisierung.Services;

internal static class MediaCodecPreferenceHelper
{
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
