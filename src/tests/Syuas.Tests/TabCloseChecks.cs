using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Syuas.App;
using Syuas.Core.Models;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

// Shares WindowTests' Application/STA; real keyboard focus is essential to reproduce this crash.
internal static class TabCloseChecks
{
    internal static void Run()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            foreach (var reordered in new[] { false, true })
            foreach (var recovery in new[] { false, true })
            foreach (var closeBackground in new[] { false, true })
                Verify(reordered, recovery, closeBackground);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void Verify(bool reordered, bool recovery, bool closeBackground)
    {
        var root = Path.Combine(Path.GetTempPath(), "SYUAS.TabCloseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var window = new MainWindow([], new Dialogs(), new History())
        {
            Opacity = 0, ShowInTaskbar = false, ShowActivated = false
        };
        // Suppress production startup so this HWND never opens the user's recovery store or prompts.
        var loaded = typeof(MainWindow).GetMethod("OnLoaded", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
        window.Loaded -= (RoutedEventHandler)loaded.CreateDelegate(typeof(RoutedEventHandler), window);
        var model = (MainViewModel)window.DataContext;
        var tabs = (TabControl)window.FindName("DocumentTabs");
        try
        {
            window.Show();
            if (recovery) model.EnableRecovery(new(new RecoveryStore(root)));
            var first = model.ActiveDocument;
            model.New();
            var second = model.ActiveDocument;
            if (reordered)
            {
                Layout();
                var front = (TabItem)tabs.ItemContainerGenerator.ContainerFromIndex(0);
                var point = front.TranslatePoint(new Point(2, front.ActualHeight / 2), tabs);
                Assert.True(window.TabReorder.CompleteDrop(window.TabReorder.CreateData(second), point));
                Assert.Same(second, model.Documents[0]);
            }
            var target = closeBackground ? first : model.Documents[1];
            var survivor = model.Documents.Single(d => d != target);
            model.ActiveDocument = survivor;
            var survivorEditor = window.ActiveEditor;
            survivorEditor.Text = "surviving document";
            survivorEditor.Document.UndoStack.MarkAsOriginalFile();
            survivorEditor.AppendText(" edit");
            model.ActiveDocument = closeBackground ? survivor : target;
            Layout();
            FocusHeader(model.ActiveDocument);
            Close(target);
            Assert.Same(survivor, Assert.Single(model.Documents));
            Assert.Same(survivor, model.ActiveDocument);
            Assert.Same(survivor, tabs.SelectedItem);
            Assert.Equal(0, tabs.SelectedIndex);
            Assert.Same(survivorEditor, window.ActiveEditor);
            Assert.Equal("surviving document edit", window.ActiveEditor.Text);
            model.UndoCommand.Execute(null);
            Assert.Equal("surviving document", window.ActiveEditor.Text);

            Layout();
            FocusHeader(survivor);
            Close(survivor);
            var replacement = Assert.Single(model.Documents);
            Assert.NotSame(survivor, replacement);
            Assert.Same(replacement, model.ActiveDocument);
            Assert.Same(replacement, tabs.SelectedItem);
            Assert.Equal(0, tabs.SelectedIndex);
            Assert.Equal("", window.ActiveEditor.Text);
            Assert.False(model.Session.IsModified);
            Assert.False(model.IsBusy);
        }
        finally
        {
            window.Close();
            Layout();
            model.Dispose();
            Directory.Delete(root, true);
        }

        void Layout()
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.UpdateLayout();
        }
        void FocusHeader(DocumentTabViewModel document)
        {
            window.Activate();
            var header = (TabItem)tabs.ItemContainerGenerator.ContainerFromItem(document);
            Assert.True(header.Focus());
            Assert.True(tabs.IsKeyboardFocusWithin);
        }
        void Close(DocumentTabViewModel document)
        {
            var task = model.CloseDocumentAsync(document);
            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < timeout)
            {
                Layout();
                Thread.Sleep(1);
            }
            Assert.True(task.IsCompleted, $"Close timed out: reordered={reordered}, recovery={recovery}, background={closeBackground}");
            Assert.True(task.GetAwaiter().GetResult());
            Layout();
        }
    }

    private sealed class History : IRecentFilesStore
    {
        public IReadOnlyList<string> Load() => [];
        public void Save(IReadOnlyList<string> paths) { }
    }
    private sealed class Dialogs : IUserDialogs
    {
        public string? ChooseOpenFile() => throw new InvalidOperationException();
        public string? ChooseSaveFile(string? currentPath) => throw new InvalidOperationException();
        public SaveDecision ConfirmSave(string documentName) => SaveDecision.Discard;
        public SaveConflictDecision ResolveSaveConflict(string path, FileObservation observation, bool isCurrentFile) => SaveConflictDecision.Cancel;
        public void ShowError(string message) => Assert.Fail(message);
        public void ShowInformation(string message) => Assert.Fail(message);
    }
}
