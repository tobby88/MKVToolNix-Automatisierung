using System.ComponentModel;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MkvToolnixAutomatisierung.Services;
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

    private interface SelectionTestItemContract : INotifyPropertyChanged
    {
        bool IsSelected { get; }
    }
}
