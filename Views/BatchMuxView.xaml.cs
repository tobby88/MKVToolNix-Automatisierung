using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung.Views;

public partial class BatchMuxView : UserControl
{
    private bool _columnLayoutPending;
    private BatchMuxViewModel? _viewModel;

    public BatchMuxView()
    {
        InitializeComponent();
        DataContextChanged += BatchMuxView_OnDataContextChanged;
    }

    private void EpisodeItemsGrid_OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        ScheduleColumnLayout();
    }

    private void EpisodeItemsGrid_OnSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            ScheduleColumnLayout();
        }
    }

    private void EpisodeItemsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not BatchMuxViewModel viewModel || viewModel.SelectedEpisodeItem is null)
        {
            return;
        }

        if (viewModel.SelectedEpisodeItem.RequiresManualCheck
            && !viewModel.SelectedEpisodeItem.IsManualCheckApproved
            && viewModel.OpenSelectedSourcesCommand.CanExecute(null))
        {
            viewModel.OpenSelectedSourcesCommand.Execute(null);
            return;
        }

        if (viewModel.SelectedEpisodeItem.RequiresMetadataReview
            && !viewModel.SelectedEpisodeItem.IsMetadataReviewApproved
            && viewModel.ReviewSelectedMetadataCommand.CanExecute(null))
        {
            viewModel.ReviewSelectedMetadataCommand.Execute(null);
            return;
        }

        DetailsExpander.IsExpanded = true;
        DetailsExpander.BringIntoView();
    }

    private void ScheduleColumnLayout()
    {
        if (_columnLayoutPending)
        {
            return;
        }

        _columnLayoutPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _columnLayoutPending = false;
            ApplyEpisodeGridColumnWidths();
        }));
    }

    private void BatchMuxView_OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.EpisodeItems.CollectionChanged -= EpisodeItems_OnCollectionChanged;
        }

        _viewModel = e.NewValue as BatchMuxViewModel;
        if (_viewModel is not null)
        {
            _viewModel.EpisodeItems.CollectionChanged += EpisodeItems_OnCollectionChanged;
            ScheduleColumnLayout();
        }
    }

    private void EpisodeItems_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScheduleColumnLayout();
    }

    private void ApplyEpisodeGridColumnWidths()
    {
        if (EpisodeItemsGrid.Columns.Count < 7 || EpisodeItemsGrid.ActualWidth <= 0)
        {
            return;
        }

        var columns = EpisodeItemsGrid.Columns;
        var items = _viewModel?.EpisodeItems ?? [];
        var checkWidth = Math.Max(MeasureTextWidth("Auswahl"), 68d);
        var seasonWidth = Math.Max(MeasureTextWidth("S/E"), MeasureTextWidth(items.Select(item => item.EpisodeCodeDisplayText))) + 24;
        var libraryWidth = Math.Max(MeasureTextWidth("In Bibliothek"), MeasureTextWidth(items.Select(item => item.ArchiveStateText))) + 28;
        var reviewWidth = Math.Max(MeasureTextWidth("Prüfung"), MeasureTextWidth(items.Select(item => item.ReviewHint))) + 30;
        var statusWidth = Math.Max(MeasureTextWidth("Status"), MeasureTextWidth(items.Select(item => item.Status))) + 30;

        columns[0].Width = new DataGridLength(checkWidth, DataGridLengthUnitType.Pixel);
        columns[2].Width = new DataGridLength(seasonWidth, DataGridLengthUnitType.Pixel);
        columns[3].Width = new DataGridLength(libraryWidth, DataGridLengthUnitType.Pixel);
        columns[4].Width = new DataGridLength(reviewWidth, DataGridLengthUnitType.Pixel);
        columns[5].Width = new DataGridLength(statusWidth, DataGridLengthUnitType.Pixel);

        var fixedWidth = checkWidth
            + seasonWidth
            + libraryWidth
            + reviewWidth
            + statusWidth;

        var availableWidth = EpisodeItemsGrid.ActualWidth - fixedWidth - 42;
        if (availableWidth <= 0)
        {
            return;
        }

        var titleDesiredWidth = Math.Max(180d, Math.Max(MeasureTextWidth("Titel"), MeasureTextWidth(items.Select(item => item.Title)) + 22));
        var sourceDesiredWidth = Math.Max(220d, Math.Max(MeasureTextWidth("Quelle"), MeasureTextWidth(items.Select(item => item.MainVideoFileName)) + 22));

        var titleWidth = titleDesiredWidth <= availableWidth - 220d
            ? titleDesiredWidth
            : Math.Max(180d, availableWidth - 220d);

        var sourceWidth = sourceDesiredWidth;

        columns[1].Width = new DataGridLength(titleWidth, DataGridLengthUnitType.Pixel);
        columns[6].Width = new DataGridLength(sourceWidth, DataGridLengthUnitType.Pixel);
    }

    private double MeasureTextWidth(IEnumerable<string> values)
    {
        var width = 0d;
        foreach (var value in values)
        {
            width = Math.Max(width, MeasureTextWidth(value));
        }

        return width;
    }

    private double MeasureTextWidth(string? text)
    {
        text ??= string.Empty;

        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(
                EpisodeItemsGrid.FontFamily,
                EpisodeItemsGrid.FontStyle,
                EpisodeItemsGrid.FontWeight,
                EpisodeItemsGrid.FontStretch),
            EpisodeItemsGrid.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(EpisodeItemsGrid).PixelsPerDip);

        return Math.Ceiling(formattedText.Width);
    }
}
