using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Syuas.App.Adapters;
using Syuas.App.Highlighting;
using Syuas.App.Services;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.App;

public partial class MainWindow : Window
{
    private readonly AvalonEditAdapter adapter;
    private readonly MainViewModel viewModel;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".adoc", ".asciidoc", ".ad", ".asc", ".txt" };

    public MainWindow()
    {
        InitializeComponent();
        Editor.SyntaxHighlighting = AsciiDocHighlighting.Load();
        Editor.Options.IndentationSize = 4;
        Editor.Options.ConvertTabsToSpaces = false;
        adapter = new(Editor);
        viewModel = new(adapter, new Utf8FileService(), new WindowsDialogs(this),
            new RecentFilesStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SYUAS", "recent-files.json")), new InputAssistanceDialogs(this));
        viewModel.Assistance!.FocusRequested += (_, _) => Editor.Focus();
        DataContext = viewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Editor.Focus();
    private void OnClosing(object? sender, CancelEventArgs e) => e.Cancel = !viewModel.CanClose();
    private void OnClosed(object? sender, EventArgs e) { viewModel.Dispose(); adapter.Dispose(); }
    private void OnExit(object sender, RoutedEventArgs e) => Close();
    private void OnFind(object sender, RoutedEventArgs e) => ShowSearch(false);
    private void OnReplace(object sender, RoutedEventArgs e) => ShowSearch(true);
    private void OnCloseSearch(object sender, RoutedEventArgs e) { viewModel.IsSearchVisible = false; Editor.Focus(); }

    private void ShowSearch(bool replace)
    {
        if (Editor.SelectionLength > 0 && !Editor.SelectedText.Contains('\n')) viewModel.SearchText = Editor.SelectedText;
        viewModel.IsSearchVisible = true;
        var target = replace ? ReplacementBox : SearchBox;
        target.Focus();
        target.SelectAll();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.F or Key.H)
        {
            ShowSearch(e.Key == Key.H);
            e.Handled = true;
        }
        else if (e.Key == Key.F3)
        {
            if (viewModel.SearchText.Length == 0) ShowSearch(false); else viewModel.FindNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && viewModel.IsSearchVisible)
        {
            viewModel.IsSearchVisible = false;
            Editor.Focus();
            e.Handled = true;
        }
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { viewModel.FindNext(); e.Handled = true; }
    }

    private static string? DroppedPath(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop)) return null;
        return data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths
            && File.Exists(paths[0]) && Extensions.Contains(Path.GetExtension(paths[0])) ? paths[0] : null;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DroppedPath(e.Data) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        if (DroppedPath(e.Data) is { } path) viewModel.Open(path);
    }

    private void OnAbout(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "SYUAS\nAsciiDoc ソースエディタ\n\nPhase 2 — 基本入力補助\n.NET 8 / WPF / AvalonEdit", "SYUASについて", MessageBoxButton.OK, MessageBoxImage.Information);
}
