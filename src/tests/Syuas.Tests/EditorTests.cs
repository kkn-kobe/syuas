using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using Syuas.App.Adapters;
using Syuas.App.Highlighting;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

public sealed class EditorTests
{
    [Fact]
    public void SavePointIsRestoredByUndoAndRedo() => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load("original");
        editor.Replace(8, 0, " modified");
        Assert.True(editor.IsModified);
        editor.MarkSaved();
        Assert.False(editor.IsModified);
        editor.Undo();
        Assert.Equal("original", editor.Text);
        Assert.True(editor.IsModified);
        editor.Redo();
        Assert.False(editor.IsModified);
    });

    [Fact]
    public void LoadClearsPreviousUndoHistory() => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Replace(0, 0, "old");
        editor.Load("new");
        Assert.False(editor.CanUndo);
        Assert.False(editor.IsModified);
        Assert.Equal(1, editor.Line);
        Assert.Equal(1, editor.Column);
    });

    [Theory]
    [InlineData(SaveDecision.Cancel, false)]
    [InlineData(SaveDecision.Discard, true)]
    [InlineData(SaveDecision.Save, true)]
    public void NewHonorsUnsavedDecision(SaveDecision decision, bool expected) => Sta.Run(() =>
    {
        using var fixture = new Fixture();
        fixture.Editor.Replace(0, 0, "draft");
        fixture.Dialogs.Decision = decision;
        Assert.Equal(expected, fixture.Model.New());
        Assert.Equal(expected ? "" : "draft", fixture.Editor.Text);
        if (decision == SaveDecision.Save) Assert.Equal("draft", fixture.Files.SavedText);
    });

    [Fact]
    public void CancelSaveAsPreventsCloseAndKeepsDraft() => Sta.Run(() =>
    {
        using var fixture = new Fixture();
        fixture.Editor.Replace(0, 0, "draft");
        fixture.Dialogs.SavePath = null;
        Assert.False(fixture.Model.CanClose());
        Assert.True(fixture.Editor.IsModified);
        Assert.Null(fixture.Model.FilePath);
    });

    [Fact]
    public void FailedSavePreventsOpenAndKeepsDraft() => Sta.Run(() =>
    {
        using var fixture = new Fixture();
        fixture.Editor.Replace(0, 0, "draft");
        fixture.Files.FailWrite = true;
        Assert.False(fixture.Model.Open("next.adoc"));
        Assert.Equal("draft", fixture.Editor.Text);
        Assert.True(fixture.Editor.IsModified);
        Assert.Null(fixture.Model.FilePath);
        Assert.Single(fixture.Dialogs.Errors);
    });

    [Fact]
    public void FailedReadKeepsCurrentFileAndUndoHistory() => Sta.Run(() =>
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Model.Open("first.adoc"));
        fixture.Editor.Replace(0, 0, "draft");
        fixture.Dialogs.Decision = SaveDecision.Discard;
        fixture.Files.FailRead = true;
        Assert.False(fixture.Model.Open("missing.adoc"));
        Assert.Equal("draftloaded", fixture.Editor.Text);
        Assert.Equal(Path.GetFullPath("first.adoc"), fixture.Model.FilePath);
        Assert.True(fixture.Editor.CanUndo);
        Assert.True(fixture.Editor.IsModified);
    });

    [Fact]
    public void FailedSaveAsKeepsOriginalFilePath() => Sta.Run(() =>
    {
        using var fixture = new Fixture();
        fixture.Model.Open("first.adoc");
        fixture.Editor.Replace(0, 0, "draft");
        fixture.Files.FailWrite = true;
        Assert.False(fixture.Model.Save(true));
        Assert.Equal(Path.GetFullPath("first.adoc"), fixture.Model.FilePath);
        Assert.True(fixture.Editor.IsModified);
    });

    [Fact]
    public void HistoryFailureDoesNotTurnSuccessfulSaveIntoFailure() => Sta.Run(() =>
    {
        using var fixture = new Fixture();
        fixture.Editor.Replace(0, 0, "draft");
        fixture.History.FailSave = true;
        Assert.True(fixture.Model.Save());
        Assert.False(fixture.Editor.IsModified);
        Assert.Empty(fixture.Dialogs.Errors);
    });

    [Fact]
    public void RecentFilesAreUniqueAndLimitedToTen() => Sta.Run(() =>
    {
        using var fixture = new Fixture();
        for (var i = 0; i < 12; i++) Assert.True(fixture.Model.Open($"file{i}.adoc"));
        fixture.Model.Open("FILE11.adoc");
        Assert.Equal(10, fixture.Model.RecentFiles.Count);
        Assert.Equal(Path.GetFullPath("FILE11.adoc"), fixture.Model.RecentFiles[0]);
        Assert.Equal(10, fixture.History.Paths.Count);
    });

    [Fact]
    public void ReplaceAllUsesOneUndoAndDoesNotRecursivelyReplace() => Sta.Run(() =>
    {
        using var fixture = new Fixture();
        fixture.Editor.Load("cat Cat cat");
        fixture.Model.SearchText = "cat";
        fixture.Model.ReplacementText = "catcat";
        fixture.Model.ReplaceAllCommand.Execute(null);
        Assert.Equal("catcat catcat catcat", fixture.Editor.Text);
        Assert.Equal("3 件を置換しました", fixture.Model.SearchStatus);
        fixture.Editor.Undo();
        Assert.Equal("cat Cat cat", fixture.Editor.Text);
        Assert.False(fixture.Editor.IsModified);
        Assert.False(fixture.Editor.CanUndo);
    });

    [Fact]
    public void ReplaceOnlyChangesMatchingSelectionThenFindsNext() => Sta.Run(() =>
    {
        using var fixture = new Fixture();
        fixture.Editor.Load("cat dog cat");
        fixture.Model.SearchText = "cat";
        fixture.Model.ReplacementText = "fox";
        fixture.Editor.Select(4, 3);
        fixture.Model.ReplaceCommand.Execute(null);
        Assert.Equal("cat dog cat", fixture.Editor.Text);
        Assert.Equal(8, fixture.Editor.SelectionStart);
        fixture.Model.ReplaceCommand.Execute(null);
        Assert.Equal("cat dog fox", fixture.Editor.Text);
        Assert.Equal(0, fixture.Editor.SelectionStart);
    });

    [Fact]
    public void HighlightingLoadsAndSourceContentsAreNotHeadings() => Sta.Run(() =>
    {
        var definition = AsciiDocHighlighting.Load();
        var document = new TextDocument("= Title\n[source,csharp]\n----\n== code\n----\n== Heading\ninclude::test.adoc[]\n|===\nNOTE: 注意\n// comment\n:toc:\nimage::a.png[]\nxref:id[]\nhttps://example.com[Example]");
        using var highlighter = new DocumentHighlighter(document, definition);
        Assert.Equal("Title", highlighter.HighlightLine(1).Sections[0].Color.Name);
        Assert.Equal("Attribute", highlighter.HighlightLine(2).Sections[0].Color.Name);
        Assert.All(highlighter.HighlightLine(4).Sections, s => Assert.Equal("Code", s.Color.Name));
        Assert.Equal("Title", highlighter.HighlightLine(6).Sections[0].Color.Name);
        var names = new[] { "Macro", "Delimiter", "Admonition", "Comment", "Attribute", "Macro", "Macro", "Macro" };
        for (var i = 0; i < names.Length; i++) Assert.Equal(names[i], highlighter.HighlightLine(i + 7).Sections[0].Color.Name);
    });

    private sealed class Fixture : IDisposable
    {
        public AvalonEditAdapter Editor { get; } = new(new TextEditor());
        public FakeFiles Files { get; } = new();
        public FakeDialogs Dialogs { get; } = new();
        public FakeHistory History { get; } = new();
        public MainViewModel Model { get; }
        public Fixture() => Model = new(Editor, Files, Dialogs, History);
        public void Dispose() { Model.Dispose(); Editor.Dispose(); }
    }

    private sealed class FakeFiles : IFileService
    {
        public bool FailRead { get; set; }
        public bool FailWrite { get; set; }
        public string? SavedText { get; private set; }
        public string Read(string path) => FailRead ? throw new IOException("Read failed") : "loaded";
        public void Write(string path, string text)
        {
            if (FailWrite) throw new IOException("Write failed");
            SavedText = text;
        }
    }

    private sealed class FakeDialogs : IUserDialogs
    {
        public SaveDecision Decision { get; set; } = SaveDecision.Save;
        public string? SavePath { get; set; } = Path.GetFullPath("saved.adoc");
        public List<string> Errors { get; } = [];
        public string? ChooseOpenFile() => null;
        public string? ChooseSaveFile(string? currentPath) => SavePath;
        public SaveDecision ConfirmSave(string documentName) => Decision;
        public void ShowError(string message) => Errors.Add(message);
    }

    private sealed class FakeHistory : IRecentFilesStore
    {
        public bool FailSave { get; set; }
        public IReadOnlyList<string> Paths { get; private set; } = [];
        public IReadOnlyList<string> Load() => Paths;
        public void Save(IReadOnlyList<string> paths)
        {
            if (FailSave) throw new IOException("History unavailable");
            Paths = paths;
        }
    }
}
