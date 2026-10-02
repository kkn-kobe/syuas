using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Syuas.App.Views;
using Syuas.Core.Models;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

public sealed class TableDesignerInteractionTests
{
    private static TableInputFocus Focus(TableInputKind kind, int start = 0, int length = 0, int column = 0) =>
        new(new(kind, Column: column), start, length);

    [Theory]
    [InlineData(TableInputKind.Cell)]
    [InlineData(TableInputKind.Title)]
    [InlineData(TableInputKind.ColumnWidth)]
    public void ConsecutiveInputIsOneOperationWithBeforeAndAfterCaret(TableInputKind kind)
    {
        var model = new TableDesignerViewModel();
        void Set(string value)
        {
            if (kind == TableInputKind.Cell) model.Cells[0].Text = value;
            else if (kind == TableInputKind.Title) model.Title = value;
            else model.ColumnWidths[0].Value = value;
        }
        string Get() => kind == TableInputKind.Cell ? model.Cells[0].Text
            : kind == TableInputKind.Title ? model.Title : model.ColumnWidths[0].Value;
        Set("123");
        var before = Focus(kind, 1, 2);
        model.BeginTextEdit(before);
        Set("1"); Set("14"); Set("145");
        var after = Focus(kind, 3);
        model.UpdateInputFocus(after);
        Assert.Equal(2, model.UndoCount);
        TableInputFocus? restored = null;
        model.FocusRequested += (_, e) => restored = e.Focus;
        model.UndoCommand.Execute(null);
        Assert.Equal("123", Get()); Assert.Equal(before, restored);
        model.RedoCommand.Execute(null);
        Assert.Equal("145", Get()); Assert.Equal(after, restored);
        Assert.Equal(2, model.UndoCount);
    }

    [Fact]
    public void NavigationSeparatesInputWithoutConsumingRedo()
    {
        var model = new TableDesignerViewModel();
        model.BeginTextEdit(Focus(TableInputKind.Title));
        model.Title = "A"; model.Title = "AB";
        model.EndTextEdit();
        model.BeginTextEdit(Focus(TableInputKind.Title, 1));
        model.Title = "A!B";
        model.UndoCommand.Execute(null);
        Assert.Equal("AB", model.Title);
        model.BeginTextEdit(Focus(TableInputKind.Cell)); model.EndTextEdit();
        Assert.True(model.CanRedo);
        model.RedoCommand.Execute(null);
        Assert.Equal("A!B", model.Title);
    }

    [Fact]
    public void CancelledInputGroupPreservesRedoAndInvalidWidthCanBeRestored()
    {
        var model = new TableDesignerViewModel();
        model.Title = "later"; model.UndoCommand.Execute(null);
        model.BeginTextEdit(Focus(TableInputKind.ColumnWidth));
        model.ColumnWidths[0].Value = "bad";
        Assert.False(model.CanRedo); Assert.Null(model.Snippet);
        model.ColumnWidths[0].Value = "";
        model.EndTextEdit();
        Assert.True(model.CanRedo); Assert.False(model.CanUndo);
        model.RedoCommand.Execute(null);
        Assert.Equal("later", model.Title);
        model.BeginTextEdit(Focus(TableInputKind.ColumnWidth));
        model.ColumnWidths[0].Value = "b"; model.ColumnWidths[0].Value = "bad";
        model.UndoCommand.Execute(null);
        Assert.NotNull(model.Snippet);
        model.RedoCommand.Execute(null);
        Assert.Equal("bad", model.ColumnWidths[0].Value); Assert.Null(model.Snippet);
    }

    [Fact]
    public void InputAndStructuralOperationRemainSeparateAndBranchCorrectly()
    {
        var model = new TableDesignerViewModel();
        model.BeginTextEdit(Focus(TableInputKind.Cell));
        model.Cells[0].Text = "日"; model.Cells[0].Text = "日本語";
        model.DeleteRowCommand.Execute(null);
        Assert.Equal(2, model.UndoCount);
        model.UndoCommand.Execute(null);
        Assert.Equal("日本語", model.Cells[0].Text);
        model.UndoCommand.Execute(null);
        Assert.Equal("", model.Cells[0].Text);
        model.BeginTextEdit(Focus(TableInputKind.Title)); model.Title = "別の編集";
        Assert.False(model.CanRedo);
        model.EndTextEdit(); Assert.Equal(0, model.RedoCount);
    }

    [Fact]
    public void CompositionPreventsHistoryStructureAndConfirmButAcceptsInput()
    {
        var model = new TableDesignerViewModel();
        model.SelectCell(0, 0); model.SelectCell(1, 1, true);
        var selection = model.Selection;
        model.BeginTextEdit(Focus(TableInputKind.Cell));
        model.SetTextComposition(true);
        model.Cells[0].Text = "に"; model.Cells[0].Text = "日本";
        RelayCommand[] commands = [model.UndoCommand, model.RedoCommand, model.MergeCommand, model.UnmergeCommand,
            model.ResizeCommand, model.AddRowCommand, model.AddColumnCommand, model.DeleteRowCommand,
            model.DeleteColumnCommand, model.ConfirmCommand];
        foreach (var command in commands) { Assert.False(command.CanExecute(null)); command.Execute(null); }
        model.SelectCell(2, 2); model.HasHeader = true;
        Assert.False(model.HasHeader); Assert.Equal(selection, model.Selection);
        Assert.False(model.CanCancel);
        Assert.Equal(3, model.Definition.RowCount);
        model.EndTextEdit(); // Focus/selection events must not split an ongoing composition.
        model.Cells[0].Text = "日本語";
        model.SetTextComposition(false); model.EndTextEdit();
        Assert.True(model.CanCancel); Assert.Equal(1, model.UndoCount);
        model.UndoCommand.Execute(null); Assert.Equal("", model.Cells[0].Text);
        model.RedoCommand.Execute(null); Assert.Equal("日本語", model.Cells[0].Text);
    }

    [Fact]
    public void SnapshotFocusIsImmutableNavigationMetadata()
    {
        var table = new TableDefinition();
        var focus = Focus(TableInputKind.Cell, 1, 2);
        var first = TableDesignerSnapshot.Capture(table, new(0, 0, 1, 1), 0, 0, inputFocus: focus);
        var second = TableDesignerSnapshot.Capture(table, new(0, 0, 1, 1), 0, 0, inputFocus: focus with { SelectionStart = 8 });
        Assert.True(first.ContentEquals(second)); Assert.Equal(1, first.InputFocus!.SelectionStart);
        Assert.Throws<ArgumentException>(() => TableDesignerSnapshot.Capture(table, new(0, 0, 1, 1), 0, 0,
            inputFocus: focus with { SelectionLength = -1 }));
    }

    [Fact]
    public void RoutedCommandsAndContextMenusUseTableHistoryEvenInsideTextBoxes() => Sta.Run(() =>
    {
        var model = new TableDesignerViewModel();
        var dialog = new TableDesignerDialog(model);
        try
        {
            Layout(dialog);
            var title = (TextBox)dialog.FindName("TitleBox");
            Enter(title); title.Text = "a"; title.Text = "ab";
            Assert.Equal(1, model.UndoCount);
            Assert.True(ApplicationCommands.Undo.CanExecute(null, title));
            var menu = Assert.IsType<MenuItem>(title.ContextMenu.Items[0]);
            Assert.Same(title, menu.CommandTarget);
            ((RoutedCommand)menu.Command).Execute(null, menu.CommandTarget);
            Assert.Empty(model.Title);
            Assert.False(ApplicationCommands.Undo.CanExecute(null, title));
            // Native TextBox history must not bypass the table's now-empty history.
            ApplicationCommands.Undo.Execute(null, title);
            Assert.Empty(model.Title);
            ApplicationCommands.Redo.Execute(null, title);
            Assert.Equal("ab", model.Title);
            model.AddRowCommand.Execute(null);
            var preview = (TextBox)dialog.FindName("PreviewBox");
            Assert.True(preview.IsReadOnly);
            ApplicationCommands.Undo.Execute(null, preview);
            Assert.Equal(3, model.Definition.RowCount);
            var undo = (Button)dialog.FindName("UndoButton");
            Assert.Same(dialog, undo.CommandTarget);
            ((RoutedCommand)undo.Command).Execute(null, undo.CommandTarget);
            Assert.Empty(model.Title);
            var shortcuts = dialog.InputBindings.OfType<KeyBinding>().ToArray();
            Assert.Contains(shortcuts, b => b.Key == Key.Z && b.Modifiers == ModifierKeys.Control && b.Command == ApplicationCommands.Undo);
            Assert.Contains(shortcuts, b => b.Key == Key.Y && b.Modifiers == ModifierKeys.Control && b.Command == ApplicationCommands.Redo);
            Assert.Contains(shortcuts, b => b.Key == Key.Z && b.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && b.Command == ApplicationCommands.Redo);
        }
        finally { dialog.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NavigationAndClipboardCommandsCreateDistinctInputOperations(bool paste) => Sta.Run(() =>
    {
        var model = new TableDesignerViewModel();
        var dialog = new TableDesignerDialog(model);
        try
        {
            Layout(dialog);
            var title = (TextBox)dialog.FindName("TitleBox");
            Enter(title); title.Text = "a"; title.Text = "abc";
            title.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new TestSource(), 0, Key.Left)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            title.Select(1, 0);
            // PreviewTextInput starts a new edit after navigation without synthesizing OS keys.
            CompositionEvent(title, TextCompositionManager.PreviewTextInputEvent);
            title.Text = "aXbc";
            Assert.Equal(2, model.UndoCount);
            // Exercise the command event boundaries without replacing the user's clipboard.
            var clipboardCommand = paste ? ApplicationCommands.Paste : ApplicationCommands.Cut;
            title.CommandBindings.Add(new CommandBinding(clipboardCommand,
                (_, e) => { title.Text = "aX"; e.Handled = true; }, (_, e) => { e.CanExecute = true; e.Handled = true; }));
            clipboardCommand.Execute(null, title);
            CompositionEvent(title, TextCompositionManager.PreviewTextInputEvent);
            title.Text = "aXY";
            Assert.Equal(4, model.UndoCount);
            model.UndoCommand.Execute(null); Assert.Equal("aX", model.Title);
            model.UndoCommand.Execute(null); Assert.Equal("aXbc", model.Title);
            model.UndoCommand.Execute(null); Assert.Equal("abc", model.Title);
        }
        finally { dialog.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompositionEventsGuardCommandsAndGroupCommitOrCancellation(bool cancel) => Sta.Run(() =>
    {
        var model = new TableDesignerViewModel();
        var dialog = new TableDesignerDialog(model);
        try
        {
            Layout(dialog);
            var cell = Descendants<TextBox>((Grid)dialog.FindName("CellGrid")).First();
            Enter(cell);
            CompositionEvent(cell, TextCompositionManager.PreviewTextInputStartEvent, TextCompositionAutoComplete.Off);
            cell.Text = "に";
            CompositionEvent(cell, TextCompositionManager.PreviewTextInputUpdateEvent, TextCompositionAutoComplete.Off);
            cell.Text = "日本";
            Assert.True(model.IsTextComposing);
            Assert.False(ApplicationCommands.Undo.CanExecute(null, cell));
            ApplicationCommands.Undo.Execute(null, cell);
            Assert.Equal("日本", model.Cells[0].Text);
            Assert.False(((Button)dialog.FindName("CancelButton")).IsCancel);
            Assert.False(((Button)dialog.FindName("ConfirmButton")).IsEnabled);
            cell.Text = cancel ? "" : "日本語";
            CompositionEvent(cell, TextCompositionManager.TextInputEvent, TextCompositionAutoComplete.Off);
            Assert.False(model.IsTextComposing);
            Assert.True(((Button)dialog.FindName("CancelButton")).IsCancel);
            Assert.Equal(cancel ? 0 : 1, model.UndoCount);
            if (!cancel)
            {
                ApplicationCommands.Undo.Execute(null, cell); Assert.Empty(model.Cells[0].Text);
                var restoredCell = Descendants<TextBox>((Grid)dialog.FindName("CellGrid")).First();
                ApplicationCommands.Redo.Execute(null, restoredCell); Assert.Equal("日本語", model.Cells[0].Text);
            }
        }
        finally { dialog.Close(); }
    });

    [Fact]
    public void RebuiltInputsRestoreFocusAndSelectionWithoutCollapsingCellRange() => Sta.Run(() =>
    {
        var model = new TableDesignerViewModel();
        var dialog = new TableDesignerDialog(model) { Opacity = 0, ShowActivated = false };
        try
        {
            dialog.Show(); Pump(dialog);
            model.Cells[0].Text = "abcdef";
            model.SelectCell(0, 0); model.SelectCell(1, 1, true);
            model.UpdateInputFocus(Focus(TableInputKind.Cell, 2, 3));
            model.MergeCommand.Execute(null); Pump(dialog);
            model.UndoCommand.Execute(null); Pump(dialog);
            var cell = Descendants<TextBox>((Grid)dialog.FindName("CellGrid")).First();
            Assert.Equal(2, cell.SelectionStart); Assert.Equal(3, cell.SelectionLength);
            Assert.Same(cell, FocusManager.GetFocusedElement(dialog));
            Assert.Equal(new TableSelection(0, 0, 2, 2), model.Selection);
            model.BeginTextEdit(Focus(TableInputKind.ColumnWidth, column: 1));
            model.ColumnWidths[1].Value = "123";
            model.UpdateInputFocus(Focus(TableInputKind.ColumnWidth, 2, 1, 1)); model.EndTextEdit();
            model.UndoCommand.Execute(null); Pump(dialog);
            model.RedoCommand.Execute(null); Pump(dialog);
            Assert.True(((Expander)dialog.FindName("WidthsExpander")).IsExpanded);
            var width = Descendants<TextBox>((ItemsControl)dialog.FindName("WidthsList"))
                .Single(b => b.DataContext is InputField f && f.Key == "1");
            Assert.Equal("123", width.Text); Assert.Equal(2, width.SelectionStart); Assert.Equal(1, width.SelectionLength);
            Assert.Same(width, FocusManager.GetFocusedElement(dialog));
        }
        finally { dialog.Close(); }
    });

    [Fact]
    public void NativeTextReplacementRestoresOriginalSelectionAndFinalCaret() => Sta.Run(() =>
    {
        var model = new TableDesignerViewModel { Title = "abcdef" };
        var dialog = new TableDesignerDialog(model) { Opacity = 0 };
        try
        {
            dialog.Show(); Pump(dialog);
            var title = (TextBox)dialog.FindName("TitleBox");
            Assert.True(title.Focus());
            title.Select(2, 3);
            CompositionEvent(title, TextCompositionManager.PreviewTextInputEvent);
            title.SelectedText = "X";
            Assert.Equal("abXf", model.Title);
            title.Select(3, 0);
            ApplicationCommands.Undo.Execute(null, title); Pump(dialog);
            Assert.Equal("abcdef", title.Text);
            Assert.Equal(2, title.SelectionStart); Assert.Equal(3, title.SelectionLength);
            ApplicationCommands.Redo.Execute(null, title); Pump(dialog);
            Assert.Equal("abXf", title.Text);
            Assert.Equal(3, title.SelectionStart); Assert.Equal(0, title.SelectionLength);
        }
        finally { dialog.Close(); }
    });

    [Fact]
    public void DesignerRendersHistoryControlsAtMinimumSize() => Sta.Run(() =>
    {
        var model = new TableDesignerViewModel();
        var dialog = new TableDesignerDialog(model);
        try
        {
            model.Title = "履歴の確認";
            var panel = (FrameworkElement)dialog.Content;
            panel.Measure(new Size(784, 551)); panel.Arrange(new Rect(0, 0, 784, 551)); Pump(dialog);
            var undo = (Button)dialog.FindName("UndoButton");
            Assert.True(undo.IsEnabled); Assert.Contains("表タイトル", undo.ToolTip.ToString());
            Assert.True(((Grid)dialog.FindName("CellGrid")).ActualHeight > 0);
            var path = Environment.GetEnvironmentVariable("SYUAS_TABLE_HISTORY_SCREENSHOT");
            if (!string.IsNullOrEmpty(path))
            {
                var bitmap = new RenderTargetBitmap(784, 551, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(panel);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = System.IO.File.Create(path); encoder.Save(file);
            }
        }
        finally { dialog.Close(); }
    });

    private static void CompositionEvent(TextBox box, RoutedEvent routedEvent,
        TextCompositionAutoComplete autoComplete = TextCompositionAutoComplete.On) =>
        box.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,
            new TextComposition(InputManager.Current, box, "", autoComplete)) { RoutedEvent = routedEvent, Handled = true });

    private static void Enter(TextBox box) => box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, box)
        { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
    private static void Pump(Window dialog) => dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static void Layout(Window dialog)
    {
        var panel = (FrameworkElement)dialog.Content;
        panel.Measure(new Size(1080, 790)); panel.Arrange(new Rect(0, 0, 1080, 790)); Pump(dialog);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private sealed class TestSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
