using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Syuas.Core.ViewModels;
using Syuas.App;
using Syuas.App.Views;
using Syuas.Core.Models;

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

        // Instantiate and lay out every form without showing OS dialogs.
        foreach (var kind in new[] { AssistanceKind.Heading, AssistanceKind.Image, AssistanceKind.Include,
            AssistanceKind.Link, AssistanceKind.CrossReference, AssistanceKind.Anchor, AssistanceKind.SourceBlock, AssistanceKind.Admonition })
        {
            var form = new InputFormViewModel(kind, new(0, 0, "", "\n", @"C:\Docs\main.adoc"));
            var dialog = new InputDialog(form);
            var panel = (FrameworkElement)dialog.Content;
            panel.Measure(new Size(620, 650));
            panel.Arrange(new Rect(0, 0, 620, 650));
            dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.Equal(form.Title, dialog.Title);
            Assert.NotEmpty(Descendants<TextBox>(panel));
            foreach (var combo in Descendants<ComboBox>(panel).Where(c => c.Visibility == Visibility.Visible))
            {
                var field = Assert.IsType<InputField>(combo.DataContext);
                Assert.Equal(field.Value, combo.Text);
                if (!field.EditableChoice)
                {
                    combo.SelectedIndex = combo.Items.Count - 1;
                    dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    Assert.Equal(combo.SelectedItem, field.Value);
                }
            }
            if (kind == AssistanceKind.Image && Environment.GetEnvironmentVariable("SYUAS_DIALOG_SCREENSHOT") is { Length: > 0 } imagePath)
            {
                form["File"].Value = @"C:\Docs\images\system.png";
                form["Alt"].Value = "システム構成";
                dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                panel.UpdateLayout();
                var bitmap = new RenderTargetBitmap(620, 650, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(panel);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(imagePath);
                encoder.Save(stream);
            }
            dialog.Close();
        }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
