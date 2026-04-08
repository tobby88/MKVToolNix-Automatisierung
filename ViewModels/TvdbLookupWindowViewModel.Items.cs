using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels;

internal sealed partial class TvdbLookupWindowViewModel
{
    /// <summary>
    /// UI-taugliche Serienzeile für die linke Trefferliste.
    /// </summary>
    public sealed class SelectableSeriesItem
    {
        /// <summary>
        /// Verpackt einen TVDB-Serientreffer für die GUI-Liste.
        /// </summary>
        /// <param name="series">Fachlicher TVDB-Serientreffer.</param>
        public SelectableSeriesItem(TvdbSeriesSearchResult series)
        {
            Series = series;
        }

        /// <summary>
        /// Fachlicher TVDB-Serientreffer hinter der sichtbaren Zeile.
        /// </summary>
        public TvdbSeriesSearchResult Series { get; }

        /// <summary>
        /// Lesbare Serienbeschreibung für die Trefferliste.
        /// </summary>
        public string DisplayText => TvdbLookupWindowTextFormatter.FormatSeriesDisplayText(Series);
    }

    /// <summary>
    /// UI-taugliche Episodenzeile für die rechte Ergebnisliste.
    /// </summary>
    public sealed class SelectableEpisodeItem
    {
        /// <summary>
        /// Verpackt einen TVDB-Episodentreffer für die GUI-Liste.
        /// </summary>
        /// <param name="episode">Fachlicher TVDB-Episodentreffer.</param>
        public SelectableEpisodeItem(TvdbEpisodeRecord episode)
        {
            Episode = episode;
        }

        /// <summary>
        /// Fachlicher TVDB-Episodentreffer hinter der sichtbaren Zeile.
        /// </summary>
        public TvdbEpisodeRecord Episode { get; }

        /// <summary>
        /// Lesbare Episodenbeschreibung für die Trefferliste.
        /// </summary>
        public string DisplayText => TvdbLookupWindowTextFormatter.FormatEpisodeDisplayText(Episode);
    }
}
