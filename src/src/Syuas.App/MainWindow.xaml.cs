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
    private readonly Dictionary<DocumentTabViewModel, DocumentEditorView> editorViews = [];
    public static readonly DependencyProperty ActiveEditorProperty = DependencyProperty.Register(
        nameof(ActiveEditor), typeof(ICSharpCode.AvalonEdit.TextEditor), typeof(MainWindow));
    public ICSharpCode.AvalonEdit.TextEditor ActiveEditor
    {
        get => (ICSharpCode.AvalonEdit.TextEditor)GetValue(ActiveEditorProperty);
        private set => SetValue(ActiveEditorProperty, value);
    }
    public ICSharpCode.AvalonEdit.TextEditor Editor => ActiveEditor;
    private readonly MainViewModel viewModel;
    private readonly DispatcherTimer documentUpdate = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer recoveryUpdate = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer externalUpdate = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool recoveryInitialized, recoveryDialogOpen, externalDialogOpen, closingApproved, closed;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".adoc", ".asciidoc", ".ad", ".asc", ".txt" };

    public MainWindow()
    {
        InitializeComponent();
        var files = new Utf8FileService();
        var dialogs = new WindowsDialogs(this);
        var recent = new RecentFilesStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SYUAS", "recent-files.json"));
        viewModel = new(() =>
        {
            var view = new DocumentEditorView { Visibility = Visibility.Collapsed };
            var document = new DocumentTabViewModel(view.Adapter, files, dialogs, recent,
                new InputAssistanceDialogs(this), new TableEditingDialogs(this));
            view.DataContext = document;
            editorViews.Add(document, view);
            EditorHost.Children.Add(view);
            view.Editor.TextChanged += OnSourceChanged;
            document.EditorFocusRequested += OnEditorFocusRequested;
            document.Assistance!.FocusRequested += OnEditorFocusRequested;
            document.Structure.FocusRequested += OnEditorFocusRequested;
            document.Disposed += OnDocumentDisposed;
            document.EnableExternalMonitoring(new ExternalChangeService(new FileSystemChangeMonitor(), files));
            return document;
        }, dialogs, recent);
        viewModel.ActiveDocumentChanged += OnActiveDocumentChanged;
        OnActiveDocumentChanged(this, EventArgs.Empty);
        viewModel.PropertyChanged += OnViewModelChanged;
        documentUpdate.Tick += OnDocumentUpdate;
        recoveryUpdate.Tick += OnRecoveryTick;
        DataContext = viewModel;

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
        viewModel.PropertyChanged -= OnViewModelChanged;
        viewModel.ActiveDocumentChanged -= OnActiveDocumentChanged;
        HtmlPreview.Dispose(); viewModel.Dispose();
    }
    private void OnDocumentDisposed(object? sender, EventArgs e)
    {
        if (sender is not DocumentTabViewModel document || !editorViews.Remove(document, out var view)) return;
        view.Editor.TextChanged -= OnSourceChanged;
        document.EditorFocusRequested -= OnEditorFocusRequested;
        document.Assistance!.FocusRequested -= OnEditorFocusRequested;
        document.Structure.FocusRequested -= OnEditorFocusRequested;
        document.Disposed -= OnDocumentDisposed;
        EditorHost.Children.Remove(view);
        view.Dispose();
    }
    private void OnActiveDocumentChanged(object? sender, EventArgs e)
    {
        foreach (var (document, view) in editorViews)
            if (document != viewModel.ActiveDocument && view.Visibility == Visibility.Visible) view.HideEditor();
        var selectedView = editorViews[viewModel.ActiveDocument];
        selectedView.Visibility = Visibility.Visible;
        ActiveEditor = selectedView.Editor;
        HtmlPreview.InvalidateDocument();
        viewModel.Structure.Refresh();
        OnSourceChanged(this, EventArgs.Empty);
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!closed && ActiveEditor == selectedView.Editor) selectedView.RestoreViewport();
        }), DispatcherPriority.Loaded);
        _ = viewModel.ActiveDocument.CheckExternalChangesAsync(force: true);
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
    private void OnSourceChanged(object? sender, EventArgs e)
    {
        if (sender is ICSharpCode.AvalonEdit.TextEditor source && source != ActiveEditor) return;
        documentUpdate.Stop(); documentUpdate.Start();
    }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsRecoveryBusy))
            foreach (var view in editorViews.Values) view.Editor.IsReadOnly = viewModel.IsRecoveryBusy;
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
        if (e.Key == Key.Tab && (Keyboard.Modifiers == ModifierKeys.Control || Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
        {
            viewModel.SelectRelative(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.F or Key.H)
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

    private static string[] DroppedPaths(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.Where(p => File.Exists(p) && Extensions.Contains(Path.GetExtension(p))).ToArray() : [];
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DroppedPaths(e.Data).Length == 0 ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        viewModel.OpenMany(DroppedPaths(e.Data));
    }

    private void OnAbout(object sender, RoutedEventArgs e) => new Views.AboutDialog { Owner = this }.ShowDialog();
    private void OnRecoveryHelp(object sender, RoutedEventArgs e) => new RecoveryHelpDialog { Owner = this }.ShowDialog();
    private void OnTableEditingHelp(object sender, RoutedEventArgs e) => new TableEditingHelpDialog { Owner = this }.ShowDialog();
}

