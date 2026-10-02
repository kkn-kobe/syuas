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
