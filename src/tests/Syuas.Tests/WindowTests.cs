using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Syuas.Core.ViewModels;
using Syuas.App;

namespace Syuas.Tests;

public sealed class WindowTests
{
    [Fact]
    public void MainWindowLoadsXamlResourcesAndViewModel() => Sta.Run(() =>
    {
        var app = new Syuas.App.App();
        app.InitializeComponent();
        var window = new MainWindow();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1120, 700));
        content.Arrange(new Rect(0, 0, 1120, 700));
        Assert.NotNull(window.DataContext);
        Assert.Contains("SYUAS", window.Title);
        Assert.NotNull(window.FindName("Editor"));
        var model = Assert.IsType<MainViewModel>(window.DataContext);
        model.IsSearchVisible = true;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.Measure(new Size(1120, 700));
        content.Arrange(new Rect(0, 0, 1120, 700));
        content.UpdateLayout();
        var searchBox = (FrameworkElement)window.FindName("SearchBox");
        Assert.Equal(Visibility.Visible, ((FrameworkElement)searchBox.Parent).Visibility);
        Assert.True(searchBox.ActualWidth > 0);
        if (Environment.GetEnvironmentVariable("SYUAS_SMOKE_SCREENSHOT") is { Length: > 0 } path)
        {
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
        window.Close();
    });
}
