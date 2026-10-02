using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Syuas.Core.Models;
using Syuas.Core.ViewModels;

namespace Syuas.App.Views;

public partial class TableDesignerDialog : Window
{
    private readonly TableDesignerViewModel model;
    private bool restoringFocus;
    private bool closed;
    private int focusRequest;
    private TextBox? composingBox;
    public TableDesignerDialog(TableDesignerViewModel model)
    {
        InitializeComponent();
        this.model = model;
        DataContext = model;
        model.StructureChanged += OnStructureChanged;
        model.CloseRequested += OnInsert;
        model.FocusRequested += OnFocusRequested;
        model.PropertyChanged += OnModelChanged;
        AddHandler(CommandManager.PreviewCanExecuteEvent, new CanExecuteRoutedEventHandler(OnPreviewCanExecute), true);
        AddHandler(CommandManager.PreviewExecutedEvent, new ExecutedRoutedEventHandler(OnPreviewExecuted), true);
        AddHandler(CommandManager.ExecutedEvent, new ExecutedRoutedEventHandler(OnExecuted), true);
        PreviewKeyDown += OnHistoryKeyDown;
        ConfigureInput(RowsBox, new(TableInputKind.Rows));
        ConfigureInput(ColumnsBox, new(TableInputKind.Columns));
        ConfigureInput(TitleBox, new(TableInputKind.Title));
        PreviewBox.ContextMenu = CreateContextMenu(PreviewBox);
        Closed += (_, _) =>
        {
            closed = true;
            model.StructureChanged -= OnStructureChanged; model.CloseRequested -= OnInsert;
            model.FocusRequested -= OnFocusRequested; model.PropertyChanged -= OnModelChanged;
        };
        BuildGrid();
    }
    private void OnInsert(object? sender, EventArgs e) => DialogResult = true;
    private void OnHelp(object sender, RoutedEventArgs e) => new TableEditingHelpDialog { Owner = this }.ShowDialog();
    private void OnStructureChanged(object? sender, EventArgs e) => BuildGrid();

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(model.CanUndo) or nameof(model.CanRedo)) CommandManager.InvalidateRequerySuggested();
    }

    private void OnPreviewCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Undo && e.Command != ApplicationCommands.Redo) return;
        e.CanExecute = e.Command == ApplicationCommands.Undo ? model.CanUndo : model.CanRedo;
        e.ContinueRouting = false;
        e.Handled = true;
    }

    private void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command == ApplicationCommands.Undo || e.Command == ApplicationCommands.Redo)
        {
            RunHistory(e.Command == ApplicationCommands.Redo);
            e.Handled = true;
        }
        else if (e.Command == ApplicationCommands.Paste || e.Command == ApplicationCommands.Cut)
        {
            if (model.IsTextComposing) { e.Handled = true; return; }
            model.EndTextEdit();
            if (e.OriginalSource is TextBox box && box.Tag is TableInputTarget) BeginInput(box);
        }
        else if (e.Command == ApplicationCommands.SelectAll) model.EndTextEdit();
    }

    private void OnExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste && e.Command != ApplicationCommands.Cut) return;
        if (e.OriginalSource is TextBox box && box.Tag is TableInputTarget) SyncInput(box);
        model.EndTextEdit();
    }

    private void OnHistoryKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        var undo = modifiers == ModifierKeys.Control && key == Key.Z;
        var redo = (modifiers == ModifierKeys.Control && key == Key.Y)
            || (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.Z);
        if (!undo && !redo) return;
        RunHistory(redo);
        // Consume even when unavailable: never fall through to a TextBox's private Undo stack.
        e.Handled = true;
    }

    private void RunHistory(bool redo)
    {
        if (model.IsTextComposing) return;
        if (Keyboard.FocusedElement is TextBox box && box.Tag is TableInputTarget) SyncInput(box);
        var command = redo ? model.RedoCommand : model.UndoCommand;
        if (command.CanExecute(null)) command.Execute(null);
    }

    private static ContextMenu CreateContextMenu(TextBox box)
    {
        var menu = new ContextMenu();
        void Add(string label, RoutedCommand command, string gesture = "") => menu.Items.Add(new MenuItem
        {
            Header = label, Command = command, CommandTarget = box, InputGestureText = gesture
        });
        Add("元に戻す", ApplicationCommands.Undo, "Ctrl+Z");
        Add("やり直し", ApplicationCommands.Redo, "Ctrl+Y");
        menu.Items.Add(new Separator());
        Add("切り取り", ApplicationCommands.Cut, "Ctrl+X");
        Add("コピー", ApplicationCommands.Copy, "Ctrl+C");
        Add("貼り付け", ApplicationCommands.Paste, "Ctrl+V");
        menu.Items.Add(new Separator());
        Add("すべて選択", ApplicationCommands.SelectAll, "Ctrl+A");
        return menu;
    }

    private void WidthBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && box.Tag is null && box.DataContext is InputField field
            && int.TryParse(field.Key, out var column)) ConfigureInput(box, new(TableInputKind.ColumnWidth, Column: column));
    }

    private TableInputFocus InputFocus(TextBox box) => new((TableInputTarget)box.Tag, box.SelectionStart, box.SelectionLength);

    private void BeginInput(TextBox box)
    {
        if (!restoringFocus) model.BeginTextEdit(InputFocus(box));
    }

    private void SyncInput(TextBox box)
    {
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        model.UpdateInputFocus(InputFocus(box));
    }

    private void ConfigureInput(TextBox box, TableInputTarget target)
    {
        box.Tag = target;
        // Retain WPF's internal text undo support for IME. All user Undo/Redo routes
        // are intercepted at the window, including the explicit context menu targets.
        box.ContextMenu = CreateContextMenu(box);
        box.GotKeyboardFocus += (_, _) =>
        {
            if (restoringFocus) return;
            ++focusRequest;
            if (target.Kind == TableInputKind.Cell) model.SelectCell(target.Row, target.Column);
            BeginInput(box);
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            if (restoringFocus) return;
            SyncInput(box);
            model.EndTextEdit();
            if (composingBox == box)
            {
                // WPF normally completes before focus leaves. Also release the guard
                // when an input method cancels without delivering a final TextInput.
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (!closed && composingBox == box && !box.IsKeyboardFocusWithin)
                    {
                        FinishComposition(box);
                        if (Keyboard.FocusedElement is TextBox next && next.Tag is TableInputTarget nextTarget)
                        {
                            if (nextTarget.Kind == TableInputKind.Cell) model.SelectCell(nextTarget.Row, nextTarget.Column);
                            BeginInput(next);
                        }
                    }
                }));
            }
        };
        box.PreviewMouseDown += (_, _) => { if (!restoringFocus) model.EndTextEdit(); };
        box.PreviewKeyDown += (_, e) =>
        {
            if (restoringFocus || model.IsTextComposing || e.Handled) return;
            ++focusRequest;
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Tab
                || (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) model.EndTextEdit();
            else BeginInput(box);
        };
        box.SelectionChanged += (_, _) =>
        {
            if (!restoringFocus && box.IsKeyboardFocusWithin) model.UpdateInputFocus(InputFocus(box));
        };
        box.AddHandler(TextCompositionManager.PreviewTextInputStartEvent, new TextCompositionEventHandler((_, e) =>
        {
            if (e.TextComposition.AutoComplete != TextCompositionAutoComplete.Off) return;
            model.EndTextEdit();
            BeginInput(box);
            composingBox = box;
            model.SetTextComposition(true);
        }), true);
        box.AddHandler(TextCompositionManager.PreviewTextInputUpdateEvent, new TextCompositionEventHandler((_, _) =>
        {
            if (composingBox == box) model.SetTextComposition(true);
        }), true);
        box.AddHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler((_, _) => BeginInput(box)), true);
        box.AddHandler(TextCompositionManager.TextInputEvent, new TextCompositionEventHandler((_, _) =>
        {
            if (composingBox == box) FinishComposition(box);
        }), true);
    }

    private void FinishComposition(TextBox box)
    {
        SyncInput(box);
        composingBox = null;
        model.SetTextComposition(false);
        model.EndTextEdit();
    }

    private void OnFocusRequested(object? sender, TableFocusEventArgs e)
    {
        var request = ++focusRequest;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (closed || request != focusRequest || !IsVisible) return;
            var focus = e.Focus;
            var target = focus?.Target ?? new(TableInputKind.Cell, model.Selection.Row, model.Selection.Column);
            TextBox? box;
            if (target.Kind == TableInputKind.ColumnWidth)
            {
                WidthsExpander.IsExpanded = true;
                UpdateLayout();
                var column = Math.Clamp(target.Column, 0, model.Definition.ColumnCount - 1);
                box = Descendants(WidthsList).OfType<TextBox>().FirstOrDefault(b => b.DataContext is InputField f && f.Key == column.ToString());
            }
            else if (target.Kind == TableInputKind.Cell)
            {
                var cell = model.Definition.CellAt(Math.Clamp(target.Row, 0, model.Definition.RowCount - 1),
                    Math.Clamp(target.Column, 0, model.Definition.ColumnCount - 1));
                box = Descendants(CellGrid).OfType<TextBox>().FirstOrDefault(b => b.Tag is TableInputTarget t && t.Row == cell.Row && t.Column == cell.Column);
            }
            else box = target.Kind switch { TableInputKind.Rows => RowsBox, TableInputKind.Columns => ColumnsBox, _ => TitleBox };
            if (box is null) return;
            restoringFocus = true;
            try
            {
                box.Focus();
                var start = Math.Clamp(focus?.SelectionStart ?? 0, 0, box.Text.Length);
                box.Select(start, Math.Clamp(focus?.SelectionLength ?? 0, 0, box.Text.Length - start));
                box.BringIntoView();
                model.UpdateInputFocus(InputFocus(box));
            }
            finally { restoringFocus = false; }
        }));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private void BuildGrid()
    {
        CellGrid.Children.Clear(); CellGrid.RowDefinitions.Clear(); CellGrid.ColumnDefinitions.Clear();
        CellGrid.RowDefinitions.Add(new() { Height = new(28) });
        CellGrid.ColumnDefinitions.Add(new() { Width = new(38) });
        for (var row = 0; row < model.Definition.RowCount; row++)
        {
            CellGrid.RowDefinitions.Add(new() { Height = new(92) });
            AddHeader((row + 1).ToString(), row + 1, 0);
        }
        for (var col = 0; col < model.Definition.ColumnCount; col++)
        {
            CellGrid.ColumnDefinitions.Add(new() { Width = new(160) });
            AddHeader((col + 1).ToString(), 0, col + 1);
        }
        foreach (var cell in model.Cells)
        {
            var border = new Border { DataContext = cell, Style = (Style)FindResource("CellBorder"), Padding = new(4) };
            Grid.SetRow(border, cell.Row + 1); Grid.SetColumn(border, cell.Column + 1);
            Grid.SetRowSpan(border, cell.RowSpan); Grid.SetColumnSpan(border, cell.ColumnSpan);
            var content = new DockPanel();
            var label = new TextBlock { Text = cell.Label, Foreground = Brushes.SlateGray, FontSize = 10, Margin = new(2, 0, 0, 3) };
            DockPanel.SetDock(label, Dock.Top); content.Children.Add(label);
            var input = new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(2), FontSize = 14
            };
            input.SetBinding(TextBox.TextProperty, new Binding(nameof(TableCellViewModel.Text)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            System.Windows.Automation.AutomationProperties.SetName(input, cell.Label);
            border.PreviewMouseLeftButtonDown += (_, e) =>
            {
                var extend = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                model.SelectCell(cell.Row, cell.Column, extend);
                if (extend) e.Handled = true;
            };
            ConfigureInput(input, new(TableInputKind.Cell, cell.Row, cell.Column));
            content.Children.Add(input); border.Child = content; CellGrid.Children.Add(border);
        }
    }

    private void AddHeader(string text, int row, int column)
    {
        var header = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.SlateGray };
        Grid.SetRow(header, row); Grid.SetColumn(header, column); CellGrid.Children.Add(header);
    }
}
