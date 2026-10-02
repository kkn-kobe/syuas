using Syuas.App;
using Syuas.Core.Models;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

// Runs on WindowTests' WPF thread, with isolated dialogs/history and without touching user recovery data.
internal static class StartupFileChecks
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "SYUAS.StartupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "日本語 手順.adoc");
            var second = Path.Combine(root, "second document.txt");
            var missing = Path.Combine(root, "missing.adoc");
            var invalid = Path.Combine(root, "invalid-utf8.adoc");
            File.WriteAllText(first, "= 操作手順\n\n本文");
            File.WriteAllText(second, "second document");
            File.WriteAllBytes(invalid, [0xff]);
            var dialogs = new Dialogs();
            var history = new History();
            var arguments = new[] { first, missing, Path.GetRelativePath(Environment.CurrentDirectory, second), invalid, first.ToUpperInvariant() };
            var window = new MainWindow(arguments, dialogs, history);
            // Startup arguments are captured when the window is created.
            arguments[0] = missing;
            try
            {
                window.OpenStartupFiles();
                var model = Assert.IsType<MainViewModel>(window.DataContext);
                Assert.Equal(3, model.Documents.Count); // Empty starting tab plus two successful opens.
                Assert.Equal(first, model.FilePath);
                Assert.Equal("= 操作手順\n\n本文", window.ActiveEditor.Text);
                Assert.False(model.Session.IsModified);
                Assert.Equal(new[] { first.ToUpperInvariant(), second }, history.Paths);
                var error = Assert.Single(dialogs.Errors);
                Assert.Contains(missing, error);
                Assert.Contains(invalid, error);
                model.ActiveDocument = model.Documents.Single(d => d.FilePath == second);
                Assert.Equal("second document", window.ActiveEditor.Text);
                window.ActiveEditor.Document.Insert(0, "edit ");
                var session = model.Session;
                window.OpenStartupFiles();
                Assert.Equal(session, model.Session);
                Assert.Equal("edit second document", window.ActiveEditor.Text);
                Assert.Single(dialogs.Errors);
                Assert.Equal(3, model.Documents.Count);
                model.UndoCommand.Execute(null);
                Assert.Equal("second document", window.ActiveEditor.Text);
                Assert.False(model.Session.IsModified);
            }
            finally { window.Close(); }

            var empty = new MainWindow([], dialogs, new History());
            try
            {
                empty.OpenStartupFiles();
                var model = Assert.IsType<MainViewModel>(empty.DataContext);
                Assert.Single(model.Documents);
                Assert.Null(model.FilePath);
                Assert.Equal("", empty.ActiveEditor.Text);
            }
            finally { empty.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class History : IRecentFilesStore
    {
        public IReadOnlyList<string> Paths { get; private set; } = [];
        public IReadOnlyList<string> Load() => Paths;
        public void Save(IReadOnlyList<string> paths) => Paths = paths;
    }

    private sealed class Dialogs : IUserDialogs
    {
        public List<string> Errors { get; } = [];
        public string? ChooseOpenFile() => throw new InvalidOperationException("Startup should use the supplied paths.");
        public string? ChooseSaveFile(string? currentPath) => throw new InvalidOperationException("Startup must not save.");
        public SaveDecision ConfirmSave(string documentName) => SaveDecision.Discard;
        public SaveConflictDecision ResolveSaveConflict(string path, FileObservation observation, bool isCurrentFile) => SaveConflictDecision.Cancel;
        public void ShowError(string message) => Errors.Add(message);
        public void ShowInformation(string message) { }
    }
}
