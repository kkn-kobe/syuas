using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Syuas.Core.ViewModels;

namespace Syuas.App.Views;

public partial class TableDesignerDialog : Window
{
    private readonly TableDesignerViewModel model;
    public TableDesignerDialog(TableDesignerViewModel model)
    {
        InitializeComponent();
        this.model = model;
        DataContext = model;
        model.StructureChanged += OnStructureChanged;
        model.CloseRequested += OnInsert;
        Closed += (_, _) => { model.StructureChanged -= OnStructureChanged; model.CloseRequested -= OnInsert; };
        BuildGrid();
    }
    private void OnInsert(object? sender, EventArgs e) => DialogResult = true;
    private void OnStructureChanged(object? sender, EventArgs e) => BuildGrid();

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
            input.GotKeyboardFocus += (_, _) => model.SelectCell(cell.Row, cell.Column);
            content.Children.Add(input); border.Child = content; CellGrid.Children.Add(border);
        }
    }

    private void AddHeader(string text, int row, int column)
    {
        var header = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.SlateGray };
        Grid.SetRow(header, row); Grid.SetColumn(header, column); CellGrid.Children.Add(header);
    }
}
