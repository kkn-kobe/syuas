using ICSharpCode.AvalonEdit;
using Syuas.App.Adapters;
using Syuas.Core.Models;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

public sealed class Phase4Tests
{
    [Fact]
    public void OutlinePreservesHierarchyLineNumbersAndCrLfOffsets()
    {
        var source = "= Document\r\n\r\n== Overview\r\n=== Details\r\n== 日本語\r\n";
        var result = DocumentStructureParser.Parse(source);
        var root = Assert.Single(result.Roots);
        Assert.Equal("Document", root.Title); Assert.Null(root.Id);
        Assert.Equal(2, root.Children.Count);
        Assert.Equal("Details", Assert.Single(root.Children[0].Children).Title);
        Assert.Equal(3, root.Children[0].Line);
        Assert.Equal(source.IndexOf("== Overview", StringComparison.Ordinal), root.Children[0].Offset);
        Assert.Contains(result.References, r => r.Target == "_日本語");
    }

    [Theory]
    [InlineData("----")]
    [InlineData("-----")]
    [InlineData("....")]
    [InlineData("////")]
    [InlineData("++++")]
    [InlineData("|===")]
    [InlineData("====")]
    public void VerbatimAndDelimitedBlocksDoNotPolluteOutline(string delimiter)
    {
        var source = $"== Before\n{delimiter}\n== Fake\n[[fake]]\n{delimiter}\n== After";
        var result = DocumentStructureParser.Parse(source);
        Assert.Equal(new[] { "Before", "After" }, result.Headings.Select(h => h.Title));
        Assert.DoesNotContain(result.References, r => r.Target == "fake");
    }

    [Fact]
    public void CustomAndGeneratedIdsAppearAsCandidates()
    {
        var source = ":idprefix: id-\n:idseparator: -\n[[setup]]\n== Installation\n[#config]\n== Configuration\n== Hello, World!\n== Hello, World!\ntext [[inline-id]]\n== Title [[title-id]]";
        var result = DocumentStructureParser.Parse(source);
        Assert.Equal(new[] { "setup", "config", "id-hello-world", "id-hello-world-2", "inline-id", "title-id" }, result.References.Select(r => r.Target));
        Assert.Equal("Installation", result.References[0].Title);
        Assert.Equal("Title", result.Headings.Last().Title);
    }

    [Fact]
    public void DisabledSectionIdsDoNotInventReferenceTargets()
    {
        var result = DocumentStructureParser.Parse(":sectids!:\n== A\n[[b]]\n== B");
        Assert.Equal(2, result.Headings.Count);
        Assert.Equal("b", Assert.Single(result.References).Target);
    }

    [Fact]
    public void ReferenceSelectionUpdatesTargetButKeepsCustomLabel()
    {
        var source = "== Overview\n[[custom]]\n== Configuration";
        var form = new InputFormViewModel(AssistanceKind.CrossReference, new(0, 0, "custom label", "\n", null, source));
        form.SelectedReference = form.ReferenceCandidates.Single(r => r.Target == "custom");
        Assert.Equal("xref:custom[custom label]", form.Preview);
        form["Text"].Value = "";
        form.SelectedReference = form.ReferenceCandidates[0];
        Assert.Equal("xref:_overview[Overview]", form.Preview);
    }

    [Fact]
    public void AnchorSuggestionsAvoidExistingIdsAndRejectManualDuplicates()
    {
        var form = new InputFormViewModel(AssistanceKind.Anchor, new(0, 0, "Setup", "\n", null, "[[setup]]\n== Install"));
        Assert.Equal("setup-2", form["Id"].Value);
        Assert.True(form.InsertCommand.CanExecute(null));
        form["Id"].Value = "setup";
        Assert.False(form.InsertCommand.CanExecute(null));
        Assert.Contains("使用されています", form.Error);
    }

    [Fact]
    public void OutlineNavigationDoesNotEditOrMoveCaretDuringRefresh() => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load("== First\n\n=== Child\n"); editor.Select(2, 0);
        var model = new DocumentStructureViewModel(editor);
        Assert.Equal(2, editor.SelectionStart);
        var child = model.Roots[0].Children[0];
        model.NavigateCommand.Execute(child);
        Assert.Equal(child.Offset, editor.SelectionStart);
        Assert.False(editor.CanUndo);
        editor.Replace(0, 0, "\n");
        model.Refresh();
        Assert.Equal(2, model.Roots[0].Line);
    });

    [Fact]
    public void AdvancedIncludeOptionsGenerateValidAttributeList()
    {
        Assert.Equal("include::parts/code.cs[leveloffset=+1,tags=\"**;!internal\",indent=0,encoding=Shift_JIS,opts=optional]",
            AsciiDocIncludeGenerator.Generate(new(@"C:\Docs\parts\code.cs", @"C:\Docs\main.adoc", LevelOffset: "+1", Tags: "**;!internal", Indent: "0", Encoding: "Shift_JIS", Optional: true)));
    }

    [Theory]
    [InlineData("1", "", "a", "", "")]
    [InlineData("", "a", "b", "", "")]
    [InlineData("", "", "a]bad", "", "")]
    [InlineData("", "", "", "-1", "")]
    [InlineData("", "", "", "3.5", "")]
    [InlineData("", "", "", "", "UTF-8,opts=bad")]
    public void InvalidAdvancedIncludeOptionsAreRejected(string lines, string tag, string tags, string indent, string encoding)
        => Assert.Throws<ArgumentException>(() => AsciiDocIncludeGenerator.Generate(new(@"C:\Docs\a.adoc", null, Lines: lines, Tag: tag, Tags: tags, Indent: indent, Encoding: encoding)));

    [Fact]
    public void PreviewResourcesStayWithinDocumentFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "SYUAS-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var doc = Path.Combine(root, "main.adoc");
            Assert.Equal(Path.Combine(root, "images", "日本語.png"), PreviewResourcePolicy.Resolve("https://document.syuas.local/images/%E6%97%A5%E6%9C%AC%E8%AA%9E.png", doc));
            Assert.Null(PreviewResourcePolicy.Resolve("https://example.com/secret", doc));
            Assert.Null(PreviewResourcePolicy.Resolve("file:///C:/secret", doc));
            Assert.Null(PreviewResourcePolicy.Resolve("https://document.syuas.local/%2e%2e%5csecret", doc));
            Assert.Null(PreviewResourcePolicy.Resolve("https://document.syuas.local/a:secret", doc));
            Assert.Null(PreviewResourcePolicy.Resolve("https://document.syuas.local/a.adoc", null));
        }
        finally { Directory.Delete(root); }
    }
}
