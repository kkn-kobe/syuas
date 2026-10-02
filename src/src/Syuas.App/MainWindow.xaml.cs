using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Syuas.Core.Models;
using Syuas.App.Adapters;
using Syuas.App.Highlighting;
using Syuas.App.Services;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;
using Syuas.App.Views;

namespace Syuas.App;

public partial class MainWindow : Window
{
    private readonly AvalonEditAdapter adapter;
    private readonly MainViewModel viewModel;
    private readonly DispatcherTimer documentUpdate = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer recoveryUpdate = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer externalUpdate = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool recoveryInitialized, recoveryDialogOpen, externalDialogOpen, closingApproved, closed;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".adoc", ".asciidoc", ".ad", ".asc", ".txt" };

    public MainWindow()
    {
        InitializeComponent();
        Editor.SyntaxHighlighting = AsciiDocHighlighting.Load();
        Editor.Options.IndentationSize = 4;
        Editor.Options.ConvertTabsToSpaces = false;
        adapter = new(Editor);
        var files = new Utf8FileService();
        viewModel = new(adapter, files, new WindowsDialogs(this),
            new RecentFilesStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SYUAS", "recent-files.json")), new InputAssistanceDialogs(this), new TableEditingDialogs(this));
        viewModel.EditorFocusRequested += OnEditorFocusRequested;
        viewModel.Assistance!.FocusRequested += (_, _) => Editor.Focus();
        viewModel.Structure.FocusRequested += (_, _) => Editor.Focus();
        Editor.TextChanged += OnSourceChanged;
        viewModel.PropertyChanged += OnViewModelChanged;
        documentUpdate.Tick += OnDocumentUpdate;
        recoveryUpdate.Tick += OnRecoveryTick;
        DataContext = viewModel;
        viewModel.EnableExternalMonitoring(new ExternalChangeService(new FileSystemChangeMonitor(), files));
        externalUpdate.Tick += OnExternalTick;
        Activated += OnActivated;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Editor.Focus();
        externalUpdate.Start();
        if (recoveryInitialized) return;
        recoveryInitialized = true;
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SYUAS", "Recovery");
            viewModel.EnableRecovery(new RecoveryService(new RecoveryStore(root)));
            recoveryUpdate.Start();
            await ShowRecoveryCandidates(true);
        }
        catch (Exception error) when (IsRecoveryError(error)) { viewModel.ReportRecoveryError(error.Message); }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closingApproved) return;
        if (recoveryDialogOpen || externalDialogOpen || !viewModel.CanClose()) { e.Cancel = true; return; }
        if (!viewModel.HasRecovery) return;
        e.Cancel = true;
        IsEnabled = false;
        recoveryUpdate.Stop();
        externalUpdate.Stop();
        await viewModel.CloseRecoveryAsync();
        closingApproved = true;
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
    private void OnClosed(object? sender, EventArgs e)
    {
        closed = true;
        externalUpdate.Stop(); externalUpdate.Tick -= OnExternalTick;
        Activated -= OnActivated;
        recoveryUpdate.Stop(); recoveryUpdate.Tick -= OnRecoveryTick;
        documentUpdate.Stop(); documentUpdate.Tick -= OnDocumentUpdate;
        Editor.TextChanged -= OnSourceChanged; viewModel.PropertyChanged -= OnViewModelChanged;
        viewModel.EditorFocusRequested -= OnEditorFocusRequested;
        HtmlPreview.Dispose(); viewModel.Dispose(); adapter.Dispose();
    }
    private async void OnRecoveryTick(object? sender, EventArgs e) => await viewModel.TickRecoveryAsync();
    private void OnEditorFocusRequested(object? sender, EventArgs e) => Editor.Focus();
    private async void OnExternalTick(object? sender, EventArgs e) => await viewModel.CheckExternalChangesAsync();
    private async void OnActivated(object? sender, EventArgs e) => await viewModel.CheckExternalChangesAsync(force: true);
    private async void OnCheckExternal(object sender, RoutedEventArgs e) => await viewModel.CheckExternalChangesAsync(force: true);
    private void OnReloadExternal(object sender, RoutedEventArgs e) { viewModel.ReloadFromDisk(); Editor.Focus(); }
    private async void OnCompareExternal(object sender, RoutedEventArgs e)
    {
        if (externalDialogOpen || recoveryDialogOpen || closed) return;
        externalDialogOpen = true;
        IsEnabled = false;
        try
        {
            var comparison = await viewModel.ReadComparisonAsync();
            IsEnabled = true;
            if (comparison is not null && !closed) new DocumentComparisonDialog(comparison) { Owner = this }.ShowDialog();
        }
        finally { externalDialogOpen = false; if (!closed) { IsEnabled = true; Editor.Focus(); } }
    }
    private async void OnRecoveryDocuments(object sender, RoutedEventArgs e) => await ShowRecoveryCandidates(false);
    private async Task ShowRecoveryCandidates(bool startup)
    {
        if (recoveryDialogOpen || externalDialogOpen || closed) return;
        recoveryDialogOpen = true;
        try
        {
            if (!viewModel.HasRecovery)
            {
                if (!startup) MessageBox.Show(this, "自動復元を開始できていません。ステータスバーを確認してください。", "SYUAS");
                return;
            }
            while (!closed)
            {
                IsEnabled = false;
                var candidates = await viewModel.ListRecoveryAsync();
                IsEnabled = true;
                if (closed) return;
                if (candidates.Count == 0)
                {
                    if (!startup) MessageBox.Show(this, "復元可能な文書はありません。", "SYUAS");
                    return;
                }
                var dialog = new RecoveryDialog(new(candidates)) { Owner = this };
                if (dialog.ShowDialog() != true || dialog.SelectedKey is not { } key) return;
                IsEnabled = false;
                if (dialog.DiscardRequested)
                {
                    await viewModel.DiscardRecoveryAsync(key);
                    continue;
                }
                if (await viewModel.RestoreRecoveryAsync(key)) return;
            }
        }
        catch (Exception error) when (IsRecoveryError(error))
        {
            viewModel.ReportRecoveryError(error.Message);
            MessageBox.Show(this, $"復元データの操作を完了できませんでした。\n{error.Message}", "SYUAS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { recoveryDialogOpen = false; IsEnabled = true; if (!closed) Editor.Focus(); }
    }
    private static bool IsRecoveryError(Exception e) => e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
    private void OnSourceChanged(object? sender, EventArgs e) { documentUpdate.Stop(); documentUpdate.Start(); }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.FilePath) or nameof(MainViewModel.IsPreviewVisible)) OnSourceChanged(sender, EventArgs.Empty);
    }
    private async void OnDocumentUpdate(object? sender, EventArgs e)
    {
        documentUpdate.Stop();
        viewModel.Structure.Refresh();
        if (viewModel.IsPreviewVisible) await HtmlPreview.RenderAsync(Editor.Text, viewModel.FilePath);
    }
    private void OnRefreshPreview(object sender, RoutedEventArgs e)
    {
        viewModel.IsPreviewVisible = true;
        OnDocumentUpdate(sender, EventArgs.Empty);
    }
    private void OnOutlineSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is OutlineEntry entry) viewModel.Structure.NavigateCommand.Execute(entry);
    }
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

    private void OnAbout(object sender, RoutedEventArgs e) => new Views.AboutDialog { Owner = this }.ShowDialog();
    private void OnRecoveryHelp(object sender, RoutedEventArgs e) => new RecoveryHelpDialog { Owner = this }.ShowDialog();
    private void OnTableEditingHelp(object sender, RoutedEventArgs e) => new TableEditingHelpDialog { Owner = this }.ShowDialog();
}
