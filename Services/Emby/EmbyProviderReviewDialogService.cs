using System.Windows;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using MkvToolnixAutomatisierung.Windows;

namespace MkvToolnixAutomatisierung.Services.Emby;

/// <summary>
/// Kapselt die WPF-Dialoge für den sequenziellen TVDB-/IMDb-Pflichtcheck im Emby-Abgleich.
/// </summary>
internal interface IEmbyProviderReviewDialogService
{
    EmbyTvdbReviewResult ReviewTvdb(
        EmbySyncItemViewModel item,
        EpisodeMetadataLookupService episodeMetadata,
        IAppSettingsDialogService settingsDialog);

    EmbyImdbReviewResult ReviewImdb(
        EmbySyncItemViewModel item,
        ImdbLookupService imdbLookup,
        ImdbLookupMode lookupMode);
}

/// <summary>
/// Standardimplementierung, die die bestehenden TVDB- und IMDb-Dialoge nutzt.
/// </summary>
internal sealed class EmbyProviderReviewDialogService : IEmbyProviderReviewDialogService
{
    public EmbyTvdbReviewResult ReviewTvdb(
        EmbySyncItemViewModel item,
        EpisodeMetadataLookupService episodeMetadata,
        IAppSettingsDialogService settingsDialog)
    {
        if (!item.TryBuildMetadataGuess(out var guess))
        {
            if (!string.IsNullOrWhiteSpace(item.TvdbId))
            {
                var result = MessageBox.Show(
                    ResolveOwner(),
                    $"Für diese Datei kann die TVDB-Suche nicht automatisch vorbefüllt werden:\n\n{item.MediaFileName}\n\nAktuelle TVDB-ID beibehalten und als geprüft markieren?\n\nTVDB-ID: {item.TvdbId}",
                    "TVDB-ID bestätigen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                return result == MessageBoxResult.Yes
                    ? EmbyTvdbReviewResult.KeepCurrent
                    : EmbyTvdbReviewResult.Cancelled;
            }

            return EmbyTvdbReviewResult.Unavailable;
        }

        var dialog = new TvdbLookupWindow(episodeMetadata, guess!, settingsDialog)
        {
            Owner = ResolveOwner()
        };
        if (dialog.ShowDialog() != true)
        {
            return EmbyTvdbReviewResult.Cancelled;
        }

        if (dialog.KeepLocalDetection)
        {
            return EmbyTvdbReviewResult.KeepCurrent;
        }

        return dialog.SelectedEpisodeSelection is null
            ? EmbyTvdbReviewResult.Cancelled
            : EmbyTvdbReviewResult.Apply(dialog.SelectedEpisodeSelection);
    }

    public EmbyImdbReviewResult ReviewImdb(
        EmbySyncItemViewModel item,
        ImdbLookupService imdbLookup,
        ImdbLookupMode lookupMode)
    {
        item.TryBuildMetadataGuess(out var guess);
        var dialog = new ImdbLookupWindow(imdbLookup, lookupMode, guess, item.ImdbId)
        {
            Owner = ResolveOwner()
        };
        if (dialog.ShowDialog() != true)
        {
            return EmbyImdbReviewResult.Cancelled;
        }

        if (dialog.ImdbExplicitlyUnavailable)
        {
            return EmbyImdbReviewResult.NoImdbId;
        }

        return string.IsNullOrWhiteSpace(dialog.SelectedImdbId)
            ? EmbyImdbReviewResult.Cancelled
            : EmbyImdbReviewResult.Apply(dialog.SelectedImdbId!);
    }

    private static Window? ResolveOwner()
    {
        return Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
               ?? Application.Current?.MainWindow;
    }
}

internal enum EmbyProviderReviewResultKind
{
    Cancelled,
    Unavailable,
    KeepCurrent,
    Applied,
    NoImdbId
}

internal sealed record EmbyTvdbReviewResult(EmbyProviderReviewResultKind Kind, TvdbEpisodeSelection? Selection)
{
    public static EmbyTvdbReviewResult Cancelled { get; } = new(EmbyProviderReviewResultKind.Cancelled, null);

    public static EmbyTvdbReviewResult Unavailable { get; } = new(EmbyProviderReviewResultKind.Unavailable, null);

    public static EmbyTvdbReviewResult KeepCurrent { get; } = new(EmbyProviderReviewResultKind.KeepCurrent, null);

    public static EmbyTvdbReviewResult Apply(TvdbEpisodeSelection selection) => new(EmbyProviderReviewResultKind.Applied, selection);
}

internal sealed record EmbyImdbReviewResult(EmbyProviderReviewResultKind Kind, string? ImdbId)
{
    public static EmbyImdbReviewResult Cancelled { get; } = new(EmbyProviderReviewResultKind.Cancelled, null);

    public static EmbyImdbReviewResult NoImdbId { get; } = new(EmbyProviderReviewResultKind.NoImdbId, null);

    public static EmbyImdbReviewResult Apply(string imdbId) => new(EmbyProviderReviewResultKind.Applied, imdbId);
}
