using System.ComponentModel;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels.Commands;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using MkvToolnixAutomatisierung.Views;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Views;

public sealed class SelectionGridInteractionTests
{
    [Fact]
    public async Task BatchSelectionGrid_SpaceToggle_KeepsFocusAndDoesNotTriggerPlanRefresh()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            using var controller = new BatchEpisodeCollectionController();
            var item = BatchEpisodeItemViewModel.CreateErrorItem(@"C:\Temp\episode.mp4", "boom");
            controller.Reset([item]);
            controller.SelectedItem = item;

            var selectedItemPlanInputsChangedCount = 0;
            controller.SelectedItemPlanInputsChanged += () => selectedItemPlanInputsChangedCount++;

            var toggleCommand = new RelayCommand(() => item.IsSelected = !item.IsSelected);
            var grid = CreateSelectionGrid(
                controller.View,
                nameof(BatchEpisodeItemViewModel.Title),
                toggleCommand,
                isGridReadOnly: true);

            var window = CreateHostWindow(grid);
            try
            {
                window.Show();
                await WpfTestHost.WaitForIdleAsync();

                FocusSelectionCell(grid, item);
                await WpfTestHost.WaitForIdleAsync();

                PressSpaceOnFocusedElement();
                await WpfTestHost.WaitForIdleAsync();

                Assert.True(item.IsSelected);
                Assert.True(grid.IsKeyboardFocusWithin);
                Assert.Equal(0, selectedItemPlanInputsChangedCount);

                // Der ursprüngliche Batch-Bug trat mit einem leichten zeitlichen Versatz auf.
                // Nach kurzer Dispatcher- und Zeitverzögerung muss der Fokus weiterhin im Grid bleiben.
                await Task.Delay(300);
                await WpfTestHost.WaitForIdleAsync();

                Assert.True(grid.IsKeyboardFocusWithin);
                Assert.Equal(0, selectedItemPlanInputsChangedCount);

                PressSpaceOnFocusedElement();
                await WpfTestHost.WaitForIdleAsync();

                Assert.False(item.IsSelected);
                Assert.True(grid.IsKeyboardFocusWithin);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task DownloadSelectionGrid_MouseToggle_TogglesOnFirstClick()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var item = new DownloadSortItemViewModel(new DownloadSortCandidate(
                DisplayName: "Episode 1",
                FilePaths: [@"C:\Temp\episode.mp4"],
                DetectedSeriesName: "Beispielserie",
                SuggestedFolderName: "Beispielserie",
                State: DownloadSortItemState.Ready,
                Note: "Bereit"));

            var toggleCommand = new RelayCommand(() => item.IsSelected = !item.IsSelected);
            var grid = CreateSelectionGrid(
                new[] { item },
                nameof(DownloadSortItemViewModel.DisplayName),
                toggleCommand,
                isGridReadOnly: false);

            var window = CreateHostWindow(grid);
            try
            {
                window.Show();
                await WpfTestHost.WaitForIdleAsync();

                var selectionCheckBox = GetSelectionCheckBox(grid, item);
                Assert.NotNull(selectionCheckBox);

                grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                    Source = selectionCheckBox
                });

                await WpfTestHost.WaitForIdleAsync();

                Assert.False(item.IsSelected);
                Assert.Same(item, grid.SelectedItem);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task EmbySelectionGrid_SpaceToggle_UsesSharedKeyboardSelectionHandling()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var viewModel = CreateEmbySyncViewModel();
            var item = new EmbySyncItemViewModel(@"C:\Videos\Serie - S01E01 - Pilot.mkv", EmbyProviderIds.Empty);
            viewModel.Items.Add(item);

            var view = new EmbySyncView
            {
                DataContext = viewModel
            };
            var window = new Window
            {
                Content = view,
                Width = 840,
                Height = 520,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000
            };

            try
            {
                window.Show();
                await WpfTestHost.WaitForIdleAsync();

                var grid = Assert.IsType<DataGrid>(FindVisualChild<DataGrid>(view));
                FocusSelectionCell(grid, item);
                await WpfTestHost.WaitForIdleAsync();

                PressSpaceOnFocusedElement();
                await WpfTestHost.WaitForIdleAsync();

                Assert.False(item.IsSelected);
                Assert.True(grid.IsKeyboardFocusWithin);
                Assert.Same(item, viewModel.SelectedItem);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static DataGrid CreateSelectionGrid(
        IEnumerable itemsSource,
        string displayMemberPath,
        ICommand toggleCommand,
        bool isGridReadOnly)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = isGridReadOnly,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = itemsSource,
            Width = 520,
            Height = 220
        };

        KeyboardNavigation.SetDirectionalNavigation(grid, KeyboardNavigationMode.Contained);
        grid.PreviewKeyDown += (_, e) => DataGridSelectionInput.TryHandleSpaceToggle(grid, e, toggleCommand);
        grid.PreviewMouseLeftButtonDown += (_, e) => DataGridSelectionInput.TryHandleMouseToggle(grid, e, toggleCommand);

        grid.Columns.Add(CreateSelectionColumn());
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Titel",
            Binding = new Binding(displayMemberPath),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        return grid;
    }

    private static DataGridTemplateColumn CreateSelectionColumn()
    {
        var checkBoxFactory = new FrameworkElementFactory(typeof(CheckBox));
        checkBoxFactory.SetValue(UIElement.FocusableProperty, false);
        checkBoxFactory.SetValue(UIElement.IsHitTestVisibleProperty, false);
        checkBoxFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBoxFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkBoxFactory.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(SelectionTestItemContract.IsSelected))
        {
            Mode = BindingMode.OneWay
        });

        return new DataGridTemplateColumn
        {
            Header = "Auswahl",
            Width = DataGridLength.SizeToHeader,
            IsReadOnly = true,
            CellTemplate = new DataTemplate
            {
                VisualTree = checkBoxFactory
            }
        };
    }

    private static Window CreateHostWindow(DataGrid grid)
    {
        return new Window
        {
            Content = grid,
            Width = 640,
            Height = 360,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000
        };
    }

    private static void FocusSelectionCell(DataGrid grid, object item)
    {
        grid.SelectedItem = item;
        grid.CurrentCell = new DataGridCellInfo(item, grid.Columns[0]);
        grid.ScrollIntoView(item, grid.Columns[0]);
        grid.UpdateLayout();
        grid.Focus();
        Keyboard.Focus(grid);
    }

    private static void PressSpaceOnFocusedElement()
    {
        var focusedElement = Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement);
        var source = PresentationSource.FromVisual(focusedElement);
        Assert.NotNull(source);

        focusedElement.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        });
    }

    private static CheckBox GetSelectionCheckBox(DataGrid grid, object item)
    {
        grid.SelectedItem = item;
        grid.CurrentCell = new DataGridCellInfo(item, grid.Columns[0]);
        grid.ScrollIntoView(item, grid.Columns[0]);
        grid.UpdateLayout();

        var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(item));
        row.ApplyTemplate();
        var presenter = FindVisualChild<DataGridCellsPresenter>(row);
        Assert.NotNull(presenter);
        presenter.ApplyTemplate();
        var cell = Assert.IsType<DataGridCell>(presenter.ItemContainerGenerator.ContainerFromIndex(0));
        return Assert.IsType<CheckBox>(FindVisualChild<CheckBox>(cell));
    }

    private static T? FindVisualChild<T>(DependencyObject? parent)
        where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            if (FindVisualChild<T>(child) is T descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static EmbySyncViewModel CreateEmbySyncViewModel()
    {
        var settingsStore = new AppSettingsStore();
        var services = new EmbyModuleServices(
            new AppEmbySettingsStore(settingsStore),
            new AppArchiveSettingsStore(settingsStore),
            new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService()),
            new EpisodeMetadataLookupService(new AppMetadataStore(settingsStore), new ThrowingTvdbClient()),
            new NullSettingsDialogService());
        return new EmbySyncViewModel(services, new NullDialogService());
    }

    private sealed class ThrowingEmbyClient : IEmbyClient
    {
        public Task<IReadOnlyList<EmbyLibraryFolder>> GetLibrariesAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EmbyServerInfo> GetSystemInfoAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerLibraryScanAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerItemFileScanAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EmbyItem?> FindItemByPathAsync(AppEmbySettings settings, string mediaFilePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RefreshItemMetadataAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingTvdbClient : ITvdbClient
    {
        public Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(string apiKey, string? pin, string query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TvdbEpisodeRecord>> GetSeriesEpisodesAsync(string apiKey, string? pin, int seriesId, string? language = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class NullSettingsDialogService : IAppSettingsDialogService
    {
        public bool ShowDialog(Window? owner = null, AppSettingsPage initialPage = AppSettingsPage.Archive)
        {
            return false;
        }
    }

    private sealed class NullDialogService : IUserDialogService
    {
        public string? SelectMainVideo(string initialDirectory) => null;
        public string? SelectAudioDescription(string initialDirectory) => null;
        public string[]? SelectSubtitles(string initialDirectory) => null;
        public string[]? SelectAttachments(string initialDirectory) => null;
        public string? SelectOutput(string initialDirectory, string fileName) => null;
        public string? SelectFolder(string title, string initialDirectory) => null;
        public string? SelectExecutable(string title, string filter, string initialDirectory) => null;
        public string? SelectFile(string title, string filter, string initialDirectory) => null;
        public string[]? SelectFiles(string title, string filter, string initialDirectory) => null;
        public MessageBoxResult AskAudioDescriptionChoice() => MessageBoxResult.Cancel;
        public MessageBoxResult AskSubtitlesChoice() => MessageBoxResult.Cancel;
        public MessageBoxResult AskAttachmentChoice() => MessageBoxResult.Cancel;
        public bool ConfirmMuxStart() => false;
        public bool ConfirmBatchExecution(int itemCount, int archiveFileCount, long archiveTotalBytes) => false;
        public bool ConfirmApplyBatchSelectionToAllItems(bool selectItems) => false;
        public bool ConfirmArchiveCopy(MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux.FileCopyPlan copyPlan) => false;
        public bool ConfirmSingleEpisodeCleanup(IReadOnlyList<string> usedFiles, IReadOnlyList<string> unusedFiles) => false;
        public bool ConfirmBatchRecycleDoneFiles(int fileCount, string doneDirectory) => false;
        public bool AskOpenDoneDirectory(string doneDirectory) => false;
        public bool ConfirmPlanReview(string episodeTitle, string reviewText) => false;
        public bool TryOpenFilesWithDefaultApp(IEnumerable<string> filePaths) => false;
        public void OpenPathWithDefaultApp(string path) { }
        public MessageBoxResult AskSourceReviewResult(string fileName, bool canTryAlternative) => MessageBoxResult.Cancel;
        public void ShowInfo(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string message) { }
    }

    private interface SelectionTestItemContract : INotifyPropertyChanged
    {
        bool IsSelected { get; }
    }
}
