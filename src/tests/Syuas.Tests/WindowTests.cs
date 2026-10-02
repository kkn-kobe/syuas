using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Syuas.Core.ViewModels;
using Syuas.App;
using Syuas.App.Views;
using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Tests;

public sealed class WindowTests
{
    [Fact]
    public void MainWindowLoadsXamlResourcesAndViewModel() => Sta.Run(() =>
    {
        // Use a window-test host so dispatching never runs the production Startup with test-runner arguments.
        var app = new Application();
        app.Resources.Add("BooleanToVisibility", new BooleanToVisibilityConverter());
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        VerifySettings();
        var window = new MainWindow();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1120, 700));
        content.Arrange(new Rect(0, 0, 1120, 700));
        Assert.NotNull(window.DataContext);
        Assert.Contains("SYUAS", window.Title);
        Assert.NotNull(window.ActiveEditor);
        var model = Assert.IsType<MainViewModel>(window.DataContext);
        var sourceEditor = window.ActiveEditor;
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
        var editorLayout = Assert.IsType<Grid>(window.FindName("DocumentLayout"));
        editorLayout.ColumnDefinitions[0].Width = new GridLength(250);
        model.Structure.IsVisible = false;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        Assert.Equal(0, editorLayout.ColumnDefinitions[0].ActualWidth);
        model.Structure.IsVisible = true;
        var externalBanner = Assert.IsType<Border>(window.FindName("ExternalChangeBanner"));
        Assert.Equal(Visibility.Collapsed, externalBanner.Visibility);
        externalBanner.DataContext = new
        {
            IsExternalChangeVisible = true, CanReadExternalFile = true,
            ExternalChangeMessage = "このファイルは外部で変更されています。編集中の内容は保持しています。",
            ExternalChangeDetail = @"C:\Documents\日本語のフォルダー\manual.adoc",
            DismissExternalChangeCommand = new RelayCommand(_ => { }), SaveAsCommand = new RelayCommand(_ => { })
        };
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.Measure(new Size(1120, 700));
        content.Arrange(new Rect(0, 0, 1120, 700));
        content.UpdateLayout();
        Assert.Equal(Visibility.Visible, externalBanner.Visibility);
        Assert.True(externalBanner.ActualHeight > 60);
        Assert.Equal(5, Descendants<Button>(externalBanner).Count());
        if (Environment.GetEnvironmentVariable("SYUAS_EXTERNAL_BANNER_SCREENSHOT") is { Length: > 0 } bannerImage)
            RenderScreenshot(content, window.Background, 1120, 700, bannerImage);
        var editTableMenu = Assert.IsType<MenuItem>(window.FindName("EditTableMenu"));
        Assert.Same(model.EditTableCommand, editTableMenu.Command);
        sourceEditor.ContextMenu.PlacementTarget = sourceEditor;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var contextEditTable = Assert.Single(sourceEditor.ContextMenu.Items.OfType<MenuItem>(), item => item.Name == "EditTableContextMenu");
        Assert.Same(model.EditTableCommand, contextEditTable.Command);
        Assert.True(contextEditTable.Command.CanExecute(null));
        var firstTab = model.ActiveDocument;
        var tabs = Assert.IsType<TabControl>(window.FindName("DocumentTabs"));
        model.New();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        Assert.Equal(2, tabs.Items.Count);
        Assert.Same(model.ActiveDocument, tabs.SelectedItem);
        Assert.NotSame(sourceEditor, window.ActiveEditor);
        tabs.SelectedItem = firstTab;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        Assert.Same(firstTab, model.ActiveDocument);
        Assert.Same(sourceEditor, window.ActiveEditor);
        Assert.True(sourceEditor.ActualWidth > 300);
        if (Environment.GetEnvironmentVariable("SYUAS_TABS_SCREENSHOT") is { Length: > 0 } tabsImage)
            RenderScreenshot(content, window.Background, 1120, 700, tabsImage);
        sourceEditor.Text = string.Join("\n", Enumerable.Range(1, 300).Select(i => $"line {i}"));
        sourceEditor.Document.UndoStack.MarkAsOriginalFile();
        content.UpdateLayout();
        sourceEditor.ScrollToVerticalOffset(900);
        content.UpdateLayout();
        var verticalOffset = sourceEditor.VerticalOffset;
        Assert.True(verticalOffset > 0);
        model.SelectRelative(1);
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        model.ActiveDocument = firstTab;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        Assert.Equal(verticalOffset, sourceEditor.VerticalOffset, precision: 1);
        VerifyTabReordering(window, model, tabs, content);
        window.Close();
        StartupFileChecks.Run();
        TabCloseChecks.Run();

        var comparisonText = new DocumentComparison(@"C:\Documents\manual.adoc",
            "== 操作手順\n\nSYUASで追記した説明です。\n\n* ローカルの変更\n",
            new("== 操作手順\n\n別のエディタで更新した説明です。\n\n* 外部の変更\n", new(@"C:\Documents\manual.adoc", FileFingerprint.FromBytes("external"u8))), DateTimeOffset.Now);
        var comparisonDialog = new DocumentComparisonDialog(comparisonText);
        var comparisonPanel = (FrameworkElement)comparisonDialog.Content;
        comparisonPanel.Measure(new Size(1080, 660));
        comparisonPanel.Arrange(new Rect(0, 0, 1080, 660));
        comparisonDialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        comparisonPanel.UpdateLayout();
        var localPane = Assert.IsType<ICSharpCode.AvalonEdit.TextEditor>(comparisonDialog.FindName("LocalEditor"));
        var diskPane = Assert.IsType<ICSharpCode.AvalonEdit.TextEditor>(comparisonDialog.FindName("DiskEditor"));
        Assert.True(localPane.IsReadOnly);
        Assert.True(diskPane.IsReadOnly);
        Assert.Equal(comparisonText.EditorText, localPane.Text);
        Assert.Equal(comparisonText.DiskSnapshot.Text, diskPane.Text);
        Assert.True(localPane.ActualWidth > 300);
        Assert.True(diskPane.ActualWidth > 300);
        if (Environment.GetEnvironmentVariable("SYUAS_COMPARISON_SCREENSHOT") is { Length: > 0 } comparisonImage)
            RenderScreenshot(comparisonPanel, comparisonDialog.Background, 1080, 660, comparisonImage);
        comparisonDialog.Close();

        var helpDialog = new RecoveryHelpDialog();
        var guide = Assert.IsType<TextBox>(helpDialog.FindName("GuideText"));
        Assert.True(guide.IsReadOnly);
        Assert.Contains("%LOCALAPPDATA%", guide.Text);
        Assert.Contains("再表示", guide.Text);
        var helpPanel = (FrameworkElement)helpDialog.Content;
        helpPanel.Measure(new Size(780, 640));
        helpPanel.Arrange(new Rect(0, 0, 780, 640));
        helpPanel.UpdateLayout();
        Assert.True(guide.ActualWidth > 600);
        Assert.True(guide.ActualHeight > 400);
        if (Environment.GetEnvironmentVariable("SYUAS_RECOVERY_HELP_SCREENSHOT") is { Length: > 0 } helpImage)
            RenderScreenshot(helpPanel, helpDialog.Background, 780, 640, helpImage);
        helpDialog.Close();

        var tableHelp = new TableEditingHelpDialog();
        var tableGuide = Assert.IsType<TextBox>(tableHelp.FindName("GuideText"));
        Assert.True(tableGuide.IsReadOnly);
        Assert.Contains("未適用", tableGuide.Text);
        Assert.Contains("noheader", tableGuide.Text);
        var tableHelpPanel = (FrameworkElement)tableHelp.Content;
        tableHelpPanel.Measure(new Size(780, 640));
        tableHelpPanel.Arrange(new Rect(0, 0, 780, 640));
        tableHelpPanel.UpdateLayout();
        Assert.True(tableGuide.ActualHeight > 400);
        if (Environment.GetEnvironmentVariable("SYUAS_TABLE_HELP_SCREENSHOT") is { Length: > 0 } tableHelpImage)
            RenderScreenshot(tableHelpPanel, tableHelp.Background, 780, 640, tableHelpImage);
        tableHelp.Close();

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
        tableDialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Assert.Equal("表デザイナー", tableDialog.Title);
        Assert.Equal("挿入", Assert.IsType<Button>(tableDialog.FindName("ConfirmButton")).Content);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<TextBlock>(tableDialog.FindName("EditHint")).Visibility);
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
        VerifyTableEditingDialog();
        if (Environment.GetEnvironmentVariable("SYUAS_WEBVIEW_SMOKE") is { Length: > 0 } smokeFolder) VerifyWebPreview(smokeFolder);
        if (Environment.GetEnvironmentVariable("SYUAS_TABLE_COMPATIBILITY_SMOKE") is { Length: > 0 } tablesFolder) TableRenderingChecks.Run(tablesFolder);
    }, timeoutSeconds: Environment.GetEnvironmentVariable("SYUAS_WEBVIEW_SMOKE") is { Length: > 0 } ? 60 : 20);

    private static void VerifyTableEditingDialog()
    {
        var definition = new TableDefinition(4, 3) { Title = "操作一覧", HasHeader = true };
        definition.SetColumnWidth(1, "2");
        definition.CellAt(0, 0).Text = "機能";
        definition.CellAt(0, 1).Text = "説明";
        definition.CellAt(0, 2).Text = "備考";
        definition.CellAt(1, 0).Text = "再編集";
        definition.CellAt(2, 0).Text = "セル結合";
        definition.Merge(new(1, 1, 2, 2));
        definition.CellAt(1, 1).Text = "既存の結合セルも読み戻せます。\n文章を修正して適用してください。";
        var parsed = AsciiDocTableParser.Parse(AsciiDocTableGenerator.Generate(definition));
        Assert.True(parsed.Succeeded);
        var viewModel = new TableDesignerViewModel(definition: parsed.Definition, sourceLine: 12,
            applyEdit: () => new(TableEditApplyStatus.Rejected, new(TableEditDiagnosticCode.DocumentChanged,
                "表を開いた後に文書が変更されています。適用せずに表を開き直してください。")));
        var dialog = new TableDesignerDialog(viewModel);
        var panel = (FrameworkElement)dialog.Content;
        panel.Measure(new Size(1010, 720)); panel.Arrange(new Rect(0, 0, 1010, 720));
        dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        panel.UpdateLayout();
        Assert.Equal("表を再編集 — SYUAS", dialog.Title);
        var confirm = Assert.IsType<Button>(dialog.FindName("ConfirmButton"));
        Assert.Equal("適用", confirm.Content);
        Assert.Same(viewModel.ConfirmCommand, confirm.Command);
        Assert.True(Assert.IsType<Button>(dialog.FindName("CancelButton")).IsCancel);
        var hint = Assert.IsType<TextBlock>(dialog.FindName("EditHint"));
        Assert.Equal(Visibility.Visible, hint.Visibility);
        Assert.Contains("12行目", hint.Text);
        var grid = Assert.IsType<Grid>(dialog.FindName("CellGrid"));
        Assert.Single(grid.Children.OfType<Border>(), border => Grid.GetRowSpan(border) == 2 && Grid.GetColumnSpan(border) == 2);
        Assert.Equal("機能", Descendants<TextBox>(grid).First().Text);
        if (Environment.GetEnvironmentVariable("SYUAS_TABLE_EDIT_SCREENSHOT") is { Length: > 0 } screenshot)
            RenderScreenshot(panel, dialog.Background, 1010, 720, screenshot);
        viewModel.ConfirmCommand.Execute(null); // Rejection must not request DialogResult/close.
        dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        panel.UpdateLayout();
        var error = Assert.IsType<TextBlock>(dialog.FindName("ErrorText"));
        Assert.Contains("開き直して", error.Text);
        Assert.True(error.ActualHeight > 0);
        Assert.Equal("機能", Descendants<TextBox>(grid).First().Text);
        panel.Measure(new Size(768, 520)); panel.Arrange(new Rect(0, 0, 768, 520)); panel.UpdateLayout();
        Assert.True(grid.Parent is ScrollViewer scroll && scroll.ActualHeight > 80);
        Assert.True(confirm.ActualWidth > 0);
        if (Environment.GetEnvironmentVariable("SYUAS_TABLE_EDIT_ERROR_SCREENSHOT") is { Length: > 0 } errorScreenshot)
            RenderScreenshot(panel, dialog.Background, 768, 520, errorScreenshot);
        dialog.Close();

        // Exercise the real WPF modal close bridge invisibly, including rejection then retry.
        var attempts = 0;
        var modalModel = new TableDesignerViewModel(applyEdit: () => ++attempts == 1
            ? new(TableEditApplyStatus.Rejected, new(TableEditDiagnosticCode.InvalidTable, "入力を確認してください。"))
            : new(TableEditApplyStatus.Applied));
        var modal = new TableDesignerDialog(modalModel) { Opacity = 0, ShowActivated = false };
        Exception? failure = null;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => { failure ??= new TimeoutException("Table modal did not close"); modal.Close(); };
        _ = modal.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                modalModel.ConfirmCommand.Execute(null);
                Assert.True(modal.IsVisible);
                Assert.Contains("入力を確認", modalModel.Error);
                modalModel.Cells[0].Text = "修正";
                modalModel.ConfirmCommand.Execute(null);
            }
            catch (Exception error) { failure = error; modal.Close(); }
        }));
        timeout.Start();
        bool? accepted;
        try { accepted = modal.ShowDialog(); }
        finally { timeout.Stop(); }
        if (failure is not null) throw new InvalidOperationException("Table editing dialog failed", failure);
        Assert.True(accepted);
        Assert.Equal(2, attempts);
    }

    private static void VerifyTabReordering(MainWindow window, MainViewModel model, TabControl tabs, FrameworkElement content)
    {
        var selected = model.ActiveDocument;
        var editor = window.ActiveEditor;
        var offset = editor.VerticalOffset;
        model.New();
        model.ActiveDocument = selected;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        var before = model.Documents.ToArray();
        var drag = window.TabReorder;
        var firstItem = (TabItem)tabs.ItemContainerGenerator.ContainerFromIndex(0);
        var closeButton = Descendants<Button>(firstItem).Single();
        Assert.False(drag.CanStart(closeButton));
        Assert.True(drag.CanStart(Descendants<TextBlock>(firstItem).First()));
        var point = firstItem.TranslatePoint(new Point(2, firstItem.ActualHeight / 2), tabs);
        var data = drag.CreateData(before[^1]);
        Assert.True(drag.UpdateDrop(data, point));
        var indicator = (Border)tabs.Template.FindName("DropIndicator", tabs);
        Assert.Equal(Visibility.Visible, indicator.Visibility);
        Assert.Equal(before, model.Documents); // Dragging shows placement but does not commit.
        if (Environment.GetEnvironmentVariable("SYUAS_TAB_REORDER_SCREENSHOT") is { Length: > 0 } screenshot)
        {
            content.UpdateLayout();
            RenderScreenshot(content, window.Background, 1120, 700, screenshot);
        }
        drag.ClearDrop();
        Assert.Equal(Visibility.Collapsed, indicator.Visibility);
        Assert.False(drag.CompleteDrop(data, new Point(-10, -10)));
        Assert.False(drag.CompleteDrop(new DataObject(DataFormats.FileDrop, new[] { "other.adoc" }), point));
        Assert.Equal(before, model.Documents);
        Assert.True(drag.CompleteDrop(data, point));
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        Assert.Same(before[^1], model.Documents[0]);
        Assert.Same(selected, model.ActiveDocument);
        Assert.Same(selected, tabs.SelectedItem);
        Assert.Same(editor, window.ActiveEditor);
        Assert.Equal(offset, editor.VerticalOffset, precision: 1);

        var lastItem = (TabItem)tabs.ItemContainerGenerator.ContainerFromIndex(tabs.Items.Count - 1);
        point = lastItem.TranslatePoint(new Point(lastItem.ActualWidth - 2, lastItem.ActualHeight / 2), tabs);
        Assert.True(drag.CompleteDrop(data, point));
        content.UpdateLayout();
        Assert.Equal(before, model.Documents);
        Assert.Same(selected, tabs.SelectedItem);

        // Overflow headers scroll while the pointer remains at the edge, then accept an end drop.
        for (var i = 0; i < 20; i++) model.New();
        model.ActiveDocument = selected;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        var scroll = (ScrollViewer)tabs.Template.FindName("HeaderScroll", tabs);
        Assert.True(scroll.ScrollableWidth > 0);
        scroll.ScrollToLeftEnd();
        content.UpdateLayout();
        var viewport = Descendants<ScrollContentPresenter>(scroll).First();
        point = viewport.TranslatePoint(new Point(viewport.ActualWidth - 2, 10), tabs);
        data = drag.CreateData(selected);
        Assert.True(drag.UpdateDrop(data, point));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (scroll.HorizontalOffset < scroll.ScrollableWidth && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(10);
        }
        Assert.Equal(scroll.ScrollableWidth, scroll.HorizontalOffset, precision: 1);
        Assert.True(drag.CompleteDrop(data, point));
        content.UpdateLayout();
        Assert.Same(selected, model.Documents[^1]);
        Assert.Same(selected, tabs.SelectedItem);
        Assert.Equal(Visibility.Collapsed, indicator.Visibility);
    }

    private static void VerifyWebPreview(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "included.adoc"), "// tag::shown[]\n== Included\n\nIncluded content\n// end::shown[]\n// tag::hidden[]\nHidden content\n// end::hidden[]");
        var imageEncoder = new PngBitmapEncoder();
        imageEncoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 220, 130, 50, 255, 220, 130, 50, 255, 220, 130, 50, 255, 220, 130, 50, 255 }, 8)));
        using (var imageStream = File.Create(Path.Combine(directory, "pixel.png"))) imageEncoder.Save(imageStream);
        var previewData = Path.Combine(directory, "preview-data-" + Guid.NewGuid().ToString("N"));
        var preview = new HtmlPreviewControl(PreviewDataMode.DeleteOnExit, previewData);
        var host = new Window { Content = preview, Width = 700, Height = 550, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        host.Show();
        var rendered = false;
        try
        {
            var source = "= Preview\n\n== Unsaved edit\n\ninclude::included.adoc[tags=\"shown\",leveloffset=+1,encoding=UTF-8]\n\ninclude::missing.adoc[opts=optional]\n\nimage::pixel.png[Test image]\n\n++++\n<script>window.evil = true</script>\n++++";
            var task = host.Dispatcher.Invoke(() => preview.RenderAsync(source, Path.Combine(directory, "main.adoc")));
            PumpUntil(host, () => task.IsCompleted && (preview.RenderedVersion > 0 || preview.StatusText.Contains("できません") || preview.StatusText.Contains("失敗")), timeoutSeconds: 30);
            task.GetAwaiter().GetResult();
            Assert.True(preview.RenderedVersion > 0, preview.StatusText);
            rendered = true;
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
            var otherDirectory = Path.Combine(directory, "other-tab");
            Directory.CreateDirectory(otherDirectory);
            File.WriteAllText(Path.Combine(otherDirectory, "included.adoc"), "Other tab content");
            File.Copy(Path.Combine(directory, "pixel.png"), Path.Combine(otherDirectory, "pixel.png"), true);
            host.Dispatcher.Invoke(preview.InvalidateDocument);
            Assert.Equal(Visibility.Hidden, ((Microsoft.Web.WebView2.Wpf.WebView2)preview.FindName("Browser")).Visibility);
            var switched = host.Dispatcher.Invoke(() => preview.RenderAsync(
                "== Other tab\n\ninclude::included.adoc[]\n\nimage::pixel.png[]", Path.Combine(otherDirectory, "main.adoc")));
            PumpUntil(host, () => switched.IsCompleted && preview.RenderedVersion == 5);
            PumpUntil(host, () =>
            {
                var inspection = host.Dispatcher.Invoke(() => InspectPreview(preview,
                    "document.getElementById('preview').contentDocument.body.innerHTML"));
                PumpUntil(host, () => inspection.IsCompleted);
                return inspection.Result.Contains("Other tab content") && !inspection.Result.Contains("Included content");
            });
            var staleResource = host.Dispatcher.Invoke(() => InspectPreview(preview,
                "(() => { const x = new XMLHttpRequest(); x.open('GET', 'https://1.document.syuas.local/included.adoc', false); try { x.send(); return x.status; } catch { return 0; } })()"));
            PumpUntil(host, () => staleResource.IsCompleted);
            Assert.NotEqual("200", staleResource.Result);
        }
        finally
        {
            var shutdown = host.Dispatcher.Invoke(preview.ShutdownAsync);
            PumpUntil(host, () => shutdown.IsCompleted);
            var cleanupError = shutdown.GetAwaiter().GetResult();
            if (rendered)
            {
                Assert.Null(cleanupError);
                Assert.DoesNotContain(Directory.EnumerateDirectories(Path.Combine(previewData, "PreviewSessions")),
                    path => Directory.Exists(Path.Combine(path, "Data")));
            }
            host.Close();
        }
    }

    private static void VerifySettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "SYUAS-settings-ui-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(Path.Combine(root, "settings.json"));
        store.Save(AppSettings.Restricted);
        var window = new MainWindow([], null, null, root);
        var model = (MainViewModel)window.DataContext;
        try
        {
            Assert.False(model.PreviewAllowed);
            model.IsPreviewVisible = true;
            Assert.False(model.IsPreviewVisible);
            Assert.False(model.HasRecovery);
            var preview = (HtmlPreviewControl)((Border)window.FindName("PreviewHost")).Child;
            var render = preview.RenderAsync("secret", null);
            PumpUntil(window, () => render.IsCompleted);
            Assert.Null(((Microsoft.Web.WebView2.Wpf.WebView2)preview.FindName("Browser")).CoreWebView2);
            Assert.False(Directory.Exists(Path.Combine(root, "Recovery")));
            Assert.False(Directory.Exists(Path.Combine(root, "WebView2")));
            Assert.False(Directory.Exists(Path.Combine(root, "PreviewSessions")));
            var enable = window.ApplySettingsAsync(new(true, PreviewDataMode.DeleteOnExit));
            PumpUntil(window, () => enable.IsCompleted);
            enable.GetAwaiter().GetResult();
            Assert.True(model.HasRecovery);
            Assert.True(model.PreviewAllowed);
            var disable = window.ApplySettingsAsync(new(false, PreviewDataMode.DeleteOnExit));
            PumpUntil(window, () => disable.IsCompleted);
            disable.GetAwaiter().GetResult();
            Assert.False(model.HasRecovery);
            Assert.True(model.PreviewAllowed);
            Assert.Contains("無効", model.RecoveryStatus);
            var again = window.ApplySettingsAsync(new(true, PreviewDataMode.Keep));
            PumpUntil(window, () => again.IsCompleted);
            again.GetAwaiter().GetResult();
            Assert.True(model.HasRecovery);
            var stop = window.ApplySettingsAsync(AppSettings.Restricted);
            PumpUntil(window, () => stop.IsCompleted);
            stop.GetAwaiter().GetResult();

            var settingsModel = new SettingsViewModel(new(false, PreviewDataMode.DeleteOnExit));
            var dialog = new SettingsDialog(settingsModel);
            var content = (FrameworkElement)dialog.Content;
            content.Measure(new Size(590, 600));
            content.Arrange(new Rect(0, 0, 590, content.DesiredSize.Height));
            content.UpdateLayout();
            var combo = Descendants<ComboBox>(content).Single();
            Assert.Equal(PreviewDataMode.DeleteOnExit, combo.SelectedValue);
            combo.SelectedValue = PreviewDataMode.Disabled;
            Assert.Equal(PreviewDataMode.Disabled, settingsModel.ToSettings().PreviewData);
            if (Environment.GetEnvironmentVariable("SYUAS_SETTINGS_SCREENSHOT") is { Length: > 0 } image)
                RenderScreenshot(content, dialog.Background, 590, (int)content.ActualHeight + 48, image);
            dialog.Close();
        }
        finally
        {
            window.Close();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            model.Dispose();
            Directory.Delete(root, true);
        }
    }

    private static void RenderScreenshot(FrameworkElement panel, Brush backgroundBrush, int width, int height, string path)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(backgroundBrush, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(panel);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static Task<string> InspectPreview(HtmlPreviewControl preview, string script)
        => ((Microsoft.Web.WebView2.Wpf.WebView2)preview.FindName("Browser")).ExecuteScriptAsync(script);

    private static void PumpUntil(Window window, Func<bool> done, int timeoutSeconds = 15)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(timeoutSeconds), "WebView2 integration check timed out.");
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

