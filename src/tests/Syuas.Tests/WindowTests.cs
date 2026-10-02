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
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new MainWindow();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1120, 700));
        content.Arrange(new Rect(0, 0, 1120, 700));
        Assert.NotNull(window.DataContext);
        Assert.Contains("SYUAS", window.Title);
        Assert.NotNull(window.FindName("Editor"));
        var model = Assert.IsType<MainViewModel>(window.DataContext);
        var sourceEditor = Assert.IsType<ICSharpCode.AvalonEdit.TextEditor>(window.FindName("Editor"));
        sourceEditor.Text = "== Overview\n\n=== Details\n\n== Configuration";
        sourceEditor.Document.UndoStack.MarkAsOriginalFile();
        model.Structure.Refresh();
        model.IsSearchVisible = true;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.Measure(new Size(1120, 700));
        content.Arrange(new Rect(0, 0, 1120, 700));
        content.UpdateLayout();
        var outline = Assert.IsType<TreeView>(window.FindName("OutlineTree"));
        Assert.Equal(2, outline.Items.Count);
        var secondHeading = Assert.IsType<TreeViewItem>(outline.ItemContainerGenerator.ContainerFromIndex(1));
        secondHeading.IsSelected = true;
        Assert.Equal(sourceEditor.Text.IndexOf("== Configuration", StringComparison.Ordinal), sourceEditor.CaretOffset);
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
        var editorLayout = Assert.IsType<Grid>(sourceEditor.Parent);
        editorLayout.ColumnDefinitions[0].Width = new GridLength(250);
        model.Structure.IsVisible = false;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        Assert.Equal(0, editorLayout.ColumnDefinitions[0].ActualWidth);
        model.Structure.IsVisible = true;
        window.Close();

        foreach (var status in Enum.GetValues<FileObservationStatus>())
        {
            var conflict = new SaveConflictViewModel(@"C:\Documents\日本語のフォルダー\manual.adoc",
                new(status, Error: "ファイルは別のプロセスによって使用されています。"), true);
            var conflictDialog = new SaveConflictDialog(conflict);
            var conflictPanel = (FrameworkElement)conflictDialog.Content;
            conflictPanel.Measure(new Size(640, 300));
            conflictPanel.Arrange(new Rect(0, 0, 640, 300));
            conflictDialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            conflictPanel.UpdateLayout();
            var primary = Assert.IsType<Button>(conflictDialog.FindName("PrimaryButton"));
            var cancel = Assert.IsType<Button>(conflictDialog.FindName("CancelButton"));
            Assert.Equal(conflict.PrimaryLabel, primary.Content);
            Assert.Equal(status switch
            {
                FileObservationStatus.Present => SaveConflictDecision.OverwriteWithBackup,
                FileObservationStatus.Missing => SaveConflictDecision.Recreate,
                _ => SaveConflictDecision.Retry
            }, conflict.PrimaryDecision);
            Assert.False(primary.IsDefault);
            Assert.True(cancel.IsDefault);
            Assert.True(cancel.IsCancel);
            Assert.Same(cancel, System.Windows.Input.FocusManager.GetFocusedElement(conflictDialog));
            Assert.Equal(SaveConflictDecision.Cancel, conflictDialog.Decision);
            Assert.Equal(conflict.FilePath, Assert.Single(Descendants<TextBox>(conflictPanel)).Text);
            if (Environment.GetEnvironmentVariable("SYUAS_SAVE_CONFLICT_SCREENSHOTS") is { Length: > 0 } outputFolder)
            {
                Directory.CreateDirectory(outputFolder);
                var bitmap = new RenderTargetBitmap(640, 300, 96, 96, PixelFormats.Pbgra32);
                var background = new DrawingVisual();
                using (var drawing = background.RenderOpen())
                    drawing.DrawRectangle(conflictDialog.Background, null, new Rect(0, 0, 640, 300));
                bitmap.Render(background);
                bitmap.Render(conflictPanel);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(outputFolder, $"{status}.png"));
                encoder.Save(output);
            }
            conflictDialog.Close();
        }

        var recoverySnapshot = new RecoverySnapshot(Guid.NewGuid(), 12, DateTimeOffset.Now, "復元する本文",
            new FileBaseline(@"C:\Documents\日本語のフォルダー\manual.adoc", FileFingerprint.FromBytes("old"u8)), 0, 0, 0);
        var recoveryModel = new RecoveryListViewModel([
            new(new(Guid.NewGuid(), recoverySnapshot.DocumentId), recoverySnapshot)
                { OriginalFile = new(FileComparisonStatus.Modified) },
            new(new(Guid.NewGuid(), Guid.NewGuid()), recoverySnapshot with { Baseline = null }, true, "直前の世代から復元します。"),
            new(new(Guid.NewGuid(), Guid.NewGuid()), null, Error: "復元データを読み取れません。元データは保持しています。")
        ]);
        var recoveryDialog = new RecoveryDialog(recoveryModel);
        var recoveryPanel = (FrameworkElement)recoveryDialog.Content;
        recoveryPanel.Measure(new Size(810, 460));
        recoveryPanel.Arrange(new Rect(0, 0, 810, 460));
        recoveryDialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var restoreButton = Assert.IsType<Button>(recoveryDialog.FindName("RestoreButton"));
        var laterButton = Assert.IsType<Button>(recoveryDialog.FindName("LaterButton"));
        Assert.True(restoreButton.IsEnabled);
        Assert.True(laterButton.IsDefault);
        Assert.True(laterButton.IsCancel);
        Assert.Same(laterButton, System.Windows.Input.FocusManager.GetFocusedElement(recoveryDialog));
        var recoveryList = Assert.IsType<ListView>(recoveryDialog.FindName("RecoveryList"));
        Assert.Equal(3, recoveryList.Items.Count);
        recoveryList.SelectedIndex = 2;
        recoveryDialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Assert.False(restoreButton.IsEnabled);
        Assert.True(Assert.IsType<Button>(recoveryDialog.FindName("DiscardButton")).IsEnabled);
        recoveryList.SelectedIndex = 0;
        recoveryDialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        recoveryPanel.UpdateLayout();
        if (Environment.GetEnvironmentVariable("SYUAS_RECOVERY_SCREENSHOT") is { Length: > 0 } recoveryImage)
        {
            var bitmap = new RenderTargetBitmap(810, 460, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen())
                drawing.DrawRectangle(recoveryDialog.Background, null, new Rect(0, 0, 810, 460));
            bitmap.Render(background);
            bitmap.Render(recoveryPanel);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(recoveryImage);
            encoder.Save(output);
        }
        recoveryDialog.Close();

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
            foreach (var combo in Descendants<ComboBox>(panel).Where(c => c.Visibility == Visibility.Visible && c.DataContext is InputField))
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

        var tableModel = new TableDesignerViewModel();
        var tableDialog = new TableDesignerDialog(tableModel);
        var tablePanel = (FrameworkElement)tableDialog.Content;
        tablePanel.Measure(new Size(1010, 700));
        tablePanel.Arrange(new Rect(0, 0, 1010, 700));
        tableDialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var cellGrid = Assert.IsType<Grid>(tableDialog.FindName("CellGrid"));
        Assert.Equal(9, cellGrid.Children.OfType<Border>().Count());
        var firstCell = Descendants<TextBox>(cellGrid).First();
        firstCell.Text = "セル入力";
        Assert.Equal("セル入力", tableModel.Definition.CellAt(0, 0).Text);
        Assert.Contains("|セル入力", tableModel.Preview);
        tableModel.RowsInput = "4";
        tableModel.ResizeCommand.Execute(null);
        tableModel.Title = "システム構成";
        tableModel.HasHeader = true;
        tableModel.Cells[0].Text = "項目"; tableModel.Cells[1].Text = "説明"; tableModel.Cells[2].Text = "備考";
        tableModel.SelectCell(1, 1); tableModel.SelectCell(2, 2, true);
        tableModel.MergeCommand.Execute(null);
        var mergedBorder = Assert.Single(cellGrid.Children.OfType<Border>(), b => Grid.GetRowSpan(b) == 2);
        Assert.Equal(2, Grid.GetColumnSpan(mergedBorder));
        var mergedModel = Assert.IsType<TableCellViewModel>(mergedBorder.DataContext);
        mergedModel.Text = "結合セル\n2行 × 2列";
        tableModel.Cells.First(c => c.Row == 1 && c.Column == 0).Text = "エディタ";
        tableModel.Cells.First(c => c.Row == 2 && c.Column == 0).Text = "入力補助";
        tableModel.Cells.First(c => c.Row == 3 && c.Column == 0).Text = "保存";
        tablePanel.Measure(new Size(1010, 700));
        tablePanel.Arrange(new Rect(0, 0, 1010, 700));
        tableDialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        tablePanel.UpdateLayout();
        Assert.Contains("2.2+|結合セル", tableModel.Preview);
        Assert.True(tableModel.InsertCommand.CanExecute(null));
        if (Environment.GetEnvironmentVariable("SYUAS_TABLE_SCREENSHOT") is { Length: > 0 } tablePath)
        {
            var bitmap = new RenderTargetBitmap(1010, 700, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(tablePanel);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(tablePath);
            encoder.Save(stream);
        }
        tableModel.UnmergeCommand.Execute(null);
        Assert.Equal(12, cellGrid.Children.OfType<Border>().Count());
        Assert.All(cellGrid.Children.OfType<Border>(), b => Assert.Equal(1, Grid.GetRowSpan(b)));
        tableModel.HasHeader = false;
        tableModel.SelectCell(0, 0); tableModel.SelectCell(1, 0, true); tableModel.MergeCommand.Execute(null);
        var headerCheckBox = Assert.Single(Descendants<CheckBox>(tablePanel));
        headerCheckBox.IsChecked = true;
        tableDialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Assert.False(tableModel.HasHeader);
        Assert.False(headerCheckBox.IsChecked);
        tableDialog.Close();
        if (Environment.GetEnvironmentVariable("SYUAS_WEBVIEW_SMOKE") is { Length: > 0 } smokeFolder) VerifyWebPreview(smokeFolder);
    });

    private static void VerifyWebPreview(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "included.adoc"), "// tag::shown[]\n== Included\n\nIncluded content\n// end::shown[]\n// tag::hidden[]\nHidden content\n// end::hidden[]");
        var imageEncoder = new PngBitmapEncoder();
        imageEncoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 220, 130, 50, 255, 220, 130, 50, 255, 220, 130, 50, 255, 220, 130, 50, 255 }, 8)));
        using (var imageStream = File.Create(Path.Combine(directory, "pixel.png"))) imageEncoder.Save(imageStream);
        var preview = new HtmlPreviewControl();
        var host = new Window { Content = preview, Width = 700, Height = 550, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        host.Show();
        try
        {
            var source = "= Preview\n\n== Unsaved edit\n\ninclude::included.adoc[tags=\"shown\",leveloffset=+1,encoding=UTF-8]\n\ninclude::missing.adoc[opts=optional]\n\nimage::pixel.png[Test image]\n\n++++\n<script>window.evil = true</script>\n++++";
            var task = host.Dispatcher.Invoke(() => preview.RenderAsync(source, Path.Combine(directory, "main.adoc")));
            PumpUntil(host, () => task.IsCompleted && (preview.RenderedVersion > 0 || preview.StatusText.Contains("できません")));
            task.GetAwaiter().GetResult();
            Assert.True(preview.RenderedVersion > 0, preview.StatusText);
            string html = "";
            PumpUntil(host, () =>
            {
                var inspection = host.Dispatcher.Invoke(() => InspectPreview(preview, "document.getElementById('preview').contentDocument.body.innerHTML"));
                PumpUntil(host, () => inspection.IsCompleted);
                html = System.Text.Json.JsonSerializer.Deserialize<string>(inspection.GetAwaiter().GetResult()) ?? "";
                return html.Contains("Unsaved edit");
            });
            Assert.Contains("Included content", html);
            Assert.DoesNotContain("Hidden content", html);
            Assert.Contains("pixel.png", html);
            PumpUntil(host, () =>
            {
                var loaded = host.Dispatcher.Invoke(() => InspectPreview(preview, "Array.from(document.getElementById('preview').contentDocument.images).every(i=>i.complete)"));
                PumpUntil(host, () => loaded.IsCompleted);
                return loaded.Result == "true";
            });
            var state = host.Dispatcher.Invoke(() => InspectPreview(preview, "JSON.stringify({evil:!!document.getElementById('preview').contentWindow.evil,images:Array.from(document.getElementById('preview').contentDocument.images).map(i=>({src:i.src,width:i.naturalWidth}))})"));
            PumpUntil(host, () => state.IsCompleted);
            using var stateJson = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Deserialize<string>(state.Result)!);
            Assert.False(stateJson.RootElement.GetProperty("evil").GetBoolean());
            Assert.True(stateJson.RootElement.GetProperty("images")[0].GetProperty("width").GetInt32() > 0, state.Result);
            File.WriteAllText(Path.Combine(directory, "rendered.html"), html);
            using (var stream = File.Create(Path.Combine(directory, "preview.png")))
            {
                var browser = (Microsoft.Web.WebView2.Wpf.WebView2)preview.FindName("Browser");
                var capture = host.Dispatcher.Invoke(() => browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, stream));
                PumpUntil(host, () => capture.IsCompleted);
                capture.GetAwaiter().GetResult();
            }
            var old = host.Dispatcher.Invoke(() => preview.RenderAsync("== Old", null));
            var latest = host.Dispatcher.Invoke(() => preview.RenderAsync("== Latest", null));
            PumpUntil(host, () => old.IsCompleted && latest.IsCompleted && preview.RenderedVersion == 3);
            PumpUntil(host, () =>
            {
                var inspection = host.Dispatcher.Invoke(() => InspectPreview(preview, "document.getElementById('preview').contentDocument.body.innerHTML"));
                PumpUntil(host, () => inspection.IsCompleted);
                return inspection.Result.Contains("Latest") && !inspection.Result.Contains("Old");
            });
        }
        finally { preview.Dispose(); host.Close(); }
    }

    private static Task<string> InspectPreview(HtmlPreviewControl preview, string script)
        => ((Microsoft.Web.WebView2.Wpf.WebView2)preview.FindName("Browser")).ExecuteScriptAsync(script);

    private static void PumpUntil(Window window, Func<bool> done)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), "WebView2 integration check timed out.");
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(10);
        }
    }

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
