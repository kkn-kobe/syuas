using ICSharpCode.AvalonEdit;
using Syuas.App.Adapters;
using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Tests;

public sealed class TableEditingTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ReplacePreservesSurroundingsAndIsOneUndoRedo(string nl) => Sta.Run(() =>
    {
        var table = TableLocatorTests.Table.Replace("\n", nl);
        var prefix = "= 見出し" + nl + nl + "[[id]]" + nl;
        var suffix = nl + nl + "後ろの文章" + nl;
        using var f = new Fixture(prefix + table + suffix);
        f.Editor.Select(prefix.Length + table.IndexOf("日本語", StringComparison.Ordinal), 2);
        var context = f.Begin();
        var session = f.Documents.Session;
        var revision = f.Editor.ContentRevision;
        context.Definition.Title = "更新した表";
        context.Definition.Merge(new(0, 0, 2, 1));
        context.Definition.CellAt(0, 0).Text = "更新した本文\n\n二段落";
        var replacement = AsciiDocTableGenerator.Generate(context.Definition, nl);
        Assert.Equal(TableEditApplyStatus.Applied, f.Service.Apply(context).Status);
        Assert.Equal(prefix + replacement + suffix, f.Editor.Text);
        Assert.Equal(session.DocumentId, f.Documents.Session.DocumentId);
        Assert.Equal(session.Baseline, f.Documents.Session.Baseline);
        Assert.True(f.Documents.Session.IsModified);
        Assert.True(f.Editor.ContentRevision > revision);
        Assert.Equal("更新した本文", f.Editor.Text.Substring(f.Editor.CaretOffset, "更新した本文".Length));
        Assert.Equal(0, f.Editor.SelectionLength);
        f.Editor.Undo();
        Assert.Equal(prefix + table + suffix, f.Editor.Text);
        Assert.False(f.Editor.IsModified);
        Assert.False(f.Editor.CanUndo);
        f.Editor.Redo();
        Assert.Equal(prefix + replacement + suffix, f.Editor.Text);
        Assert.True(f.Editor.IsModified);
        Assert.Equal(TableEditApplyStatus.Rejected, f.Service.Apply(context).Status);
    });

    [Fact]
    public void CancelByDiscardingWorkingModelLeavesDocumentSelectionAndUndoUntouched() => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table);
        f.Editor.Replace(f.Editor.Text.IndexOf("日本語", StringComparison.Ordinal), 3, "編集済");
        f.Editor.Select(f.Editor.Text.IndexOf("編集済", StringComparison.Ordinal), 2);
        var before = Snapshot(f.Editor);
        var context = f.Begin();
        context.Definition.Resize(5, 5);
        context.Definition.Title = "cancelled";
        Assert.Equal(before, Snapshot(f.Editor));
        f.Editor.Undo();
        Assert.Equal(TableLocatorTests.Table, f.Editor.Text);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnchangedApplyPreservesRevisionDirtyFlagSelectionAndRedo(bool dirty) => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table);
        if (dirty) f.Editor.Replace(f.Editor.Text.IndexOf("日本語", StringComparison.Ordinal), 3, "編集中");
        f.Editor.Replace(f.Editor.Text.Length, 0, "\n");
        f.Editor.Undo();
        f.Editor.Select(1, 2);
        var context = f.Begin();
        var originalTitle = context.Definition.Title;
        context.Definition.Title = "temporary";
        context.Definition.Title = originalTitle;
        var before = Snapshot(f.Editor);
        Assert.Equal(TableEditApplyStatus.Unchanged, f.Service.Apply(context).Status);
        Assert.Equal(before, Snapshot(f.Editor));
        Assert.True(f.Editor.CanRedo);
    });

    [Theory]
    [InlineData("table")]
    [InlineData("outside")]
    [InlineData("undo")]
    [InlineData("new")]
    public void ChangesSinceCaptureRejectApplyWithoutTouchingCurrentState(string change) => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table + "\n\n本文");
        var context = f.Begin();
        context.Definition.Title = "new title";
        if (change == "new") { f.Documents.New(); f.Editor.Replace(0, 0, TableLocatorTests.Table); }
        else
        {
            var offset = change == "outside" ? f.Editor.Text.Length : f.Editor.Text.IndexOf("日本語", StringComparison.Ordinal);
            f.Editor.Replace(offset, 0, "changed");
            if (change == "undo") f.Editor.Undo();
        }
        var before = Snapshot(f.Editor);
        var result = f.Service.Apply(context);
        Assert.Equal(TableEditApplyStatus.Rejected, result.Status);
        Assert.Equal(TableEditDiagnosticCode.DocumentChanged, result.Diagnostic!.Code);
        Assert.Equal(before, Snapshot(f.Editor));
    });

    [Fact]
    public void DocumentIdentityCheckDoesNotDependOnTextOrRevisionChange() => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table);
        var session = f.Documents.Session;
        var service = new TableEditingService(f.Editor, () => session);
        var context = service.BeginEdit().Context!;
        context.Definition.Title = "changed";
        session = session with { DocumentId = Guid.NewGuid() };
        var before = Snapshot(f.Editor);
        Assert.Equal(TableEditDiagnosticCode.DocumentChanged, service.Apply(context).Diagnostic!.Code);
        Assert.Equal(before, Snapshot(f.Editor));
    });

    [Fact]
    public void ContextBelongsToTheServiceThatCapturedIt() => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table);
        var context = f.Begin();
        context.Definition.Title = "changed";
        var another = new TableEditingService(f.Editor, () => f.Documents.Session);
        Assert.Equal(TableEditDiagnosticCode.ForeignContext, another.Apply(context).Diagnostic!.Code);
        Assert.Equal(TableLocatorTests.Table, f.Editor.Text);
    });

    [Theory]
    [InlineData("width")]
    [InlineData("title")]
    [InlineData("directive")]
    public void InvalidEditsAreRejectedBeforeCreatingUndoOrDirtyState(string change) => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table);
        var context = f.Begin();
        if (change == "width") context.Definition.SetColumnWidth(0, "invalid");
        if (change == "title") context.Definition.Title = "multi\nline";
        if (change == "directive") context.Definition.CellAt(0, 0).Text = "include::file.adoc[]";
        var before = Snapshot(f.Editor);
        var result = f.Service.Apply(context);
        Assert.Equal(TableEditApplyStatus.Rejected, result.Status);
        Assert.Equal(TableEditDiagnosticCode.InvalidTable, result.Diagnostic!.Code);
        Assert.Equal(before, Snapshot(f.Editor));
    });

    [Fact]
    public void ParserDiagnosticIsMappedToDocumentLineAndColumn() => Sta.Run(() =>
    {
        var prefix = "= Title\n\ntext\n\n";
        var table = TableLocatorTests.Table.Replace("|B", "a|B", StringComparison.Ordinal);
        using var f = new Fixture(prefix + table);
        f.Editor.Select(prefix.Length, 0);
        var before = Snapshot(f.Editor);
        var result = f.Service.BeginEdit();
        Assert.False(result.Succeeded);
        Assert.Equal(TableEditDiagnosticCode.InvalidTable, result.Diagnostic!.Code);
        Assert.Equal(TableParseDiagnosticCode.UnsupportedSyntax, result.Diagnostic.ParseCode);
        Assert.Equal(9, result.Diagnostic.Line);
        Assert.Equal(1, result.Diagnostic.Column);
        Assert.Equal(before, Snapshot(f.Editor));
    });

    [Fact]
    public void SavingWithoutChangingContentKeepsContextUsableAndPreservesNewBaseline() => Sta.Run(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"SYUAS.TableEditing.{Guid.NewGuid():N}.adoc");
        try
        {
            using var f = new Fixture(TableLocatorTests.Table);
            var context = f.Begin();
            Assert.True(f.Documents.Save(path, null).Succeeded);
            var baseline = f.Documents.Session.Baseline;
            context.Definition.Title = "after save";
            Assert.Equal(TableEditApplyStatus.Applied, f.Service.Apply(context).Status);
            Assert.Equal(baseline, f.Documents.Session.Baseline);
            Assert.Equal(TableLocatorTests.Table, File.ReadAllText(path));
            f.Editor.Undo();
            Assert.False(f.Editor.IsModified);
        }
        finally { File.Delete(path); }
    });

    [Fact]
    public void CaretPlacementUsesDelimiterLineInsteadOfDelimiterTextInTitle() => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table);
        var context = f.Begin();
        context.Definition.Title = "|===";
        Assert.Equal(TableEditApplyStatus.Applied, f.Service.Apply(context).Status);
        Assert.Equal("日本語", f.Editor.Text.Substring(f.Editor.CaretOffset, 3));
    });

    [Fact]
    public void MovingCaretAfterCaptureDoesNotChangeTarget() => Sta.Run(() =>
    {
        var source = TableLocatorTests.Table + "\n\n" + TableLocatorTests.Table;
        using var f = new Fixture(source);
        var context = f.Begin();
        f.Editor.Select(source.LastIndexOf("日本語", StringComparison.Ordinal), 0);
        context.Definition.Title = "first only";
        Assert.Equal(TableEditApplyStatus.Applied, f.Service.Apply(context).Status);
        Assert.EndsWith("\n\n" + TableLocatorTests.Table, f.Editor.Text);
    });

    [Theory]
    [InlineData("literal-title")]
    [InlineData("trailing-attribute")]
    public void ApplyRejectsOutputWhoseBoundariesWouldBeAmbiguousOnNextEdit(string change) => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table);
        var context = f.Begin();
        if (change == "literal-title") context.Definition.Title = "...";
        else context.Definition.CellAt(1, 1).Text = "last\n[cols=\"1*\",options=\"noheader\"]";
        var before = Snapshot(f.Editor);
        Assert.Equal(TableEditDiagnosticCode.InvalidTable, f.Service.Apply(context).Diagnostic!.Code);
        Assert.Equal(before, Snapshot(f.Editor));
    });

    [Fact]
    public void TitleCanBeRemovedAndAddedAgainWithoutChangingClosingNewline() => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table + "\n");
        var context = f.Begin();
        context.Definition.Title = "";
        Assert.Equal(TableEditApplyStatus.Applied, f.Service.Apply(context).Status);
        Assert.StartsWith("[cols=", f.Editor.Text);
        Assert.EndsWith("|===\n", f.Editor.Text);
        var next = f.Begin();
        next.Definition.Title = ".new title";
        Assert.Equal(TableEditApplyStatus.Applied, f.Service.Apply(next).Status);
        Assert.StartsWith("..new title\n", f.Editor.Text);
        Assert.Equal(".new title", f.Begin().Definition.Title);
        Assert.EndsWith("|===\n", f.Editor.Text);
    });

    [Fact]
    public void TableReplacementDoesNotMergeWithEarlierTextEditInUndoHistory() => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table);
        f.Editor.Replace(f.Editor.Text.IndexOf("日本語", StringComparison.Ordinal), 3, "先の編集");
        var previous = f.Editor.Text;
        var context = f.Begin();
        context.Definition.InsertRow(1);
        Assert.Equal(TableEditApplyStatus.Applied, f.Service.Apply(context).Status);
        f.Editor.Undo();
        Assert.Equal(previous, f.Editor.Text);
        Assert.True(f.Editor.IsModified);
        f.Editor.Undo();
        Assert.Equal(TableLocatorTests.Table, f.Editor.Text);
        Assert.False(f.Editor.IsModified);
    });

    private static object Snapshot(AvalonEditAdapter editor)
        => (editor.Text, editor.ContentRevision, editor.IsModified, editor.SelectionStart, editor.SelectionLength, editor.CaretOffset, editor.CanUndo, editor.CanRedo);
    private sealed class Fixture : IDisposable
    {
        public AvalonEditAdapter Editor { get; } = new(new TextEditor());
        public DocumentSessionController Documents { get; }
        public TableEditingService Service { get; }
        public Fixture(string source)
        {
            Editor.Load(source);
            Documents = new(Editor, new Utf8FileService());
            Service = new(Editor, () => Documents.Session);
        }
        public TableEditContext Begin()
        {
            var result = Service.BeginEdit();
            Assert.True(result.Succeeded, result.Diagnostic?.Message);
            return result.Context;
        }
        public void Dispose() { Documents.Dispose(); Editor.Dispose(); }
    }
}
