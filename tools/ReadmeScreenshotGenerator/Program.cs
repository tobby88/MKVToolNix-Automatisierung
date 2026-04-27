using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MkvToolnixAutomatisierung.Views;

namespace ReadmeScreenshotGenerator;

/// <summary>
/// Erzeugt reproduzierbare README-Screenshots aus echten WPF-Views mit statischen Demodaten.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var outputDirectory = Path.Combine(GetRepositoryRoot(), "docs", "images", "readme");
        Directory.CreateDirectory(outputDirectory);

        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };

        try
        {
            RenderScreenshot(
                outputDirectory,
                "download.png",
                "Download",
                new DownloadView
                {
                    DataContext = DemoData.CreateDownloadViewModel()
                },
                1320,
                820);

            RenderScreenshot(
                outputDirectory,
                "download-sort.png",
                "Einsortieren",
                new DownloadSortView
                {
                    DataContext = DemoData.CreateDownloadSortViewModel()
                },
                1380,
                860);

            RenderScreenshot(
                outputDirectory,
                "mux-single.png",
                "Muxen",
                new MuxModuleView
                {
                    DataContext = DemoData.CreateMuxModuleViewModel(selectedTabIndex: 0)
                },
                1480,
                940);

            RenderScreenshot(
                outputDirectory,
                "mux-batch.png",
                "Muxen",
                new MuxModuleView
                {
                    DataContext = DemoData.CreateMuxModuleViewModel(selectedTabIndex: 1)
                },
                1480,
                980);

            RenderScreenshot(
                outputDirectory,
                "emby-sync.png",
                "Emby-Abgleich",
                new EmbySyncView
                {
                    DataContext = DemoData.CreateEmbySyncViewModel()
                },
                1440,
                860);

            RenderScreenshot(
                outputDirectory,
                "archive-maintenance.png",
                "Archivpflege",
                new ArchiveMaintenanceView
                {
                    DataContext = DemoData.CreateArchiveMaintenanceViewModel()
                },
                1460,
                900);

            return 0;
        }
        finally
        {
            app.Shutdown();
        }
    }

    /// <summary>
    /// Rendert eine gegebene Demo-Ansicht in eine PNG-Datei.
    /// </summary>
    private static void RenderScreenshot(
        string outputDirectory,
        string fileName,
        string selectedModuleTitle,
        FrameworkElement view,
        int width,
        int height)
    {
        var host = CreateScreenshotHost(selectedModuleTitle, view);
        var window = new Window
        {
            Content = host,
            Width = width,
            Height = height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Background = Brushes.Transparent,
            AllowsTransparency = false,
            Left = -20000,
            Top = -20000
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            DrainDispatcher();

            host.Measure(new Size(width, height));
            host.Arrange(new Rect(0, 0, width, height));
            host.UpdateLayout();
            DrainDispatcher();

            var renderBitmap = new RenderTargetBitmap(
                width,
                height,
                96,
                96,
                PixelFormats.Pbgra32);
            renderBitmap.Render(host);

            var outputPath = Path.Combine(outputDirectory, fileName);
            using var stream = File.Create(outputPath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
            encoder.Save(stream);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Baut eine neutrale Host-Hülle, damit die Modul-Screenshots ohne MainWindow-Chrome trotzdem lesbar bleiben.
    /// </summary>
    private static FrameworkElement CreateScreenshotHost(string selectedModuleTitle, FrameworkElement view)
    {
        var shell = new Border
        {
            Background = DemoData.Brush("#F3F4F6"),
            Padding = new Thickness(12),
            BorderBrush = DemoData.Brush("#D8DEE6"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };

        var rootGrid = new Grid();
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        shell.Child = rootGrid;

        var sideBar = new Border
        {
            Background = DemoData.Brush("#F5F5F5"),
            BorderBrush = DemoData.Brush("#D0D0D0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12)
        };
        Grid.SetColumn(sideBar, 0);
        rootGrid.Children.Add(sideBar);

        var sideBarGrid = new Grid();
        sideBarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sideBarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sideBarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sideBar.Child = sideBarGrid;

        sideBarGrid.Children.Add(new TextBlock
        {
            Text = "Module",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var moduleList = new StackPanel();
        Grid.SetRow(moduleList, 1);
        sideBarGrid.Children.Add(moduleList);

        foreach (var module in DemoData.GetModuleCards())
        {
            moduleList.Children.Add(CreateModuleCard(
                module.Title,
                module.Description,
                string.Equals(module.Title, selectedModuleTitle, StringComparison.Ordinal)));
        }

        var sideBarFooter = new StackPanel
        {
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetRow(sideBarFooter, 2);
        sideBarGrid.Children.Add(sideBarFooter);

        sideBarFooter.Children.Add(new Button
        {
            Content = "Einstellungen",
            Padding = new Thickness(10, 8, 10, 8)
        });

        sideBarFooter.Children.Add(CreateFooterCard(
            "Systemstatus",
            [
                "Archiv: bereit",
                "MKVToolNix: bereit",
                "Laufzeiten: ffprobe",
                "Daten: portable"
            ],
            "#F8FAFC",
            "#D5DEE8",
            new Thickness(0, 8, 0, 0)));

        var contentHost = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = view
        };
        Grid.SetColumn(contentHost, 2);
        rootGrid.Children.Add(contentHost);

        return shell;
    }

    private static Border CreateModuleCard(string title, string description, bool isSelected)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 62,
            Padding = new Thickness(10),
            Background = isSelected ? DemoData.Brush("#EAF1FF") : Brushes.White,
            BorderBrush = isSelected ? DemoData.Brush("#7AA2E3") : DemoData.Brush("#D9D9D9"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };

        var stack = new StackPanel();
        border.Child = stack;
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(new TextBlock
        {
            Text = description,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = DemoData.Brush("#555555"),
            TextWrapping = TextWrapping.Wrap
        });
        return border;
    }

    private static Border CreateFooterCard(
        string title,
        IReadOnlyList<string> lines,
        string background,
        string borderBrush,
        Thickness margin)
    {
        var border = new Border
        {
            Margin = margin,
            Padding = new Thickness(10, 8, 10, 8),
            Background = DemoData.Brush(background),
            BorderBrush = DemoData.Brush(borderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };

        var stack = new StackPanel();
        border.Child = stack;
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        foreach (var line in lines)
        {
            stack.Children.Add(new TextBlock
            {
                Text = line,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        return border;
    }

    private static void DrainDispatcher()
    {
        Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
