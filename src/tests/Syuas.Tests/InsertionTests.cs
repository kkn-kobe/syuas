using ICSharpCode.AvalonEdit;
using Syuas.App.Adapters;
using Syuas.Core.Models;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

public sealed class InsertionTests
{
    [Theory]
    [InlineData(AssistanceKind.Bold, "*text*")]
    [InlineData(AssistanceKind.Italic, "_text_")]
    [InlineData(AssistanceKind.Monospace, "`text`")]
    public void InlineSelectionIsOneUndo(AssistanceKind kind, string expected) => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load("text"); editor.Select(0, 4);
        var vm = new InputAssistanceViewModel(editor, () => null, new Dialogs());
        vm.Insert(kind);
        Assert.Equal(expected, editor.Text);
        editor.Undo();
        Assert.Equal("text", editor.Text);
        Assert.False(editor.IsModified);
        Assert.False(editor.CanUndo);
    });

    [Fact]
    public void InlineFormattingWithinAWordUsesDoubleMarkers() => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load("abcdef"); editor.Select(2, 2);
        new InputAssistanceViewModel(editor, () => null, new Dialogs()).Insert(AssistanceKind.Bold);
        Assert.Equal("ab**cd**ef", editor.Text);
    });

    [Fact]
    public void EmptyFormatPlacesCaretBetweenMarkers() => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        new InputAssistanceViewModel(editor, () => null, new Dialogs()).Insert(AssistanceKind.Italic);
        Assert.Equal("__", editor.Text);
        Assert.Equal(1, editor.SelectionStart);
        Assert.Equal(0, editor.SelectionLength);
    });

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ListsExpandToWholeLinesButExcludeNextUnselectedLine(string nl) => Sta.Run(() =>
    {
        var source = "Apple" + nl + "Banana" + nl + "Orange";
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load(source);
        editor.Select(2, 9 + nl.Length * 2);
        var vm = new InputAssistanceViewModel(editor, () => null, new Dialogs());
        vm.Insert(AssistanceKind.UnorderedList);
        Assert.Equal("* Apple" + nl + "* Banana" + nl + nl + "Orange", editor.Text);
        editor.Undo(); Assert.Equal(source, editor.Text); Assert.False(editor.CanUndo);
    });

    [Fact]
    public void HeadingAtCaretReplacesExistingLineAndRestoresWithUndo() => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load("== 構成\n\n本文"); editor.Select(4, 0);
        var dialogs = new Dialogs { Configure = form => form["Level"].Value = "2" };
        new InputAssistanceViewModel(editor, () => null, dialogs).Insert(AssistanceKind.Heading);
        Assert.Equal("=== 構成\n\n本文", editor.Text);
        editor.Undo(); Assert.Equal("== 構成\n\n本文", editor.Text);
    });

    [Fact]
    public void SourceBlockWrapsSelectionAndSeparatesSurroundingProse() => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load("beforeCodeafter"); editor.Select(6, 4);
        new InputAssistanceViewModel(editor, () => null, new Dialogs()).Insert(AssistanceKind.SourceBlock);
        var nl = Environment.NewLine;
        Assert.Equal($"before{nl}{nl}[source,csharp]{nl}----{nl}Code{nl}----{nl}{nl}after", editor.Text);
        Assert.Equal("Code", editor.Text.Substring(editor.SelectionStart, 4));
        editor.Undo(); Assert.Equal("beforeCodeafter", editor.Text); Assert.False(editor.CanUndo);
    });

    [Theory]
    [InlineData(AssistanceKind.ListingBlock, "----")]
    [InlineData(AssistanceKind.LiteralBlock, "....")]
    [InlineData(AssistanceKind.QuoteBlock, "____")]
    [InlineData(AssistanceKind.ExampleBlock, "====")]
    public void EmptyBlockPlacesCaretInside(AssistanceKind kind, string delimiter) => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        new InputAssistanceViewModel(editor, () => null, new Dialogs()).Insert(kind);
        Assert.Equal(delimiter + Environment.NewLine + Environment.NewLine + delimiter, editor.Text);
        Assert.Equal(delimiter.Length + Environment.NewLine.Length, editor.SelectionStart);
        editor.Undo(); Assert.Empty(editor.Text);
    });

    [Fact]
    public void CancelDoesNotModifyTextSelectionOrHistory() => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load("keep me"); editor.Select(2, 3);
        new InputAssistanceViewModel(editor, () => null, new Dialogs { Cancel = true }).Insert(AssistanceKind.Link);
        Assert.Equal("keep me", editor.Text);
        Assert.Equal(2, editor.SelectionStart); Assert.Equal(3, editor.SelectionLength);
        Assert.False(editor.IsModified); Assert.False(editor.CanUndo);
    });

    private sealed class Dialogs : IInputAssistanceDialogs
    {
        public bool Cancel { get; init; }
        public Action<InputFormViewModel>? Configure { get; init; }
        public InsertionSnippet? Show(AssistanceKind kind, InsertionContext context)
        {
            if (Cancel) return null;
            var form = new InputFormViewModel(kind, context);
            Configure?.Invoke(form);
            return form.Snippet;
        }
    }
}
