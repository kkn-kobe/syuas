using Syuas.Core.Models;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

public sealed class GeneratorTests
{
    [Theory]
    [InlineData(1, "== 概要")]
    [InlineData(2, "=== 概要")]
    [InlineData(3, "==== 概要")]
    [InlineData(4, "===== 概要")]
    [InlineData(5, "====== 概要")]
    public void HeadingLevelsAndExistingMarkers(int level, string expected)
    {
        Assert.Equal(expected, AsciiDocHeadingGenerator.Generate(new(level, "概要")));
        Assert.Equal(expected, AsciiDocHeadingGenerator.Generate(new(level, "== 概要")));
    }

    [Fact]
    public void HeadingRejectsInvalidLevelsAndMultipleLines()
    {
        Assert.Throws<ArgumentException>(() => AsciiDocHeadingGenerator.Generate(new(0, "Title")));
        Assert.Throws<ArgumentException>(() => AsciiDocHeadingGenerator.Generate(new(6, "Title")));
        Assert.Throws<ArgumentException>(() => AsciiDocHeadingGenerator.Generate(new(1, "one\ntwo")));
    }

    [Theory]
    [InlineData(@"C:\Docs\manual\chapter\intro.adoc", "include::chapter/intro.adoc[]")]
    [InlineData(@"C:\Docs\shared.adoc", "include::../shared.adoc[]")]
    [InlineData(@"chapter\intro.adoc", "include::chapter/intro.adoc[]")]
    [InlineData(@"D:\other.adoc", "include::D:/other.adoc[]")]
    public void IncludeCalculatesPaths(string path, string expected)
        => Assert.Equal(expected, AsciiDocIncludeGenerator.Generate(new(path, @"C:\Docs\manual\main.adoc")));

    [Fact]
    public void IncludeSupportsOptionsAndAbsolutePaths()
    {
        Assert.Equal("include::chapter.adoc[lines=\"1..5;8;10..-1\",leveloffset=+1]",
            AsciiDocIncludeGenerator.Generate(new(@"C:\Docs\chapter.adoc", @"C:\Docs\main.adoc", Lines: "1..5;8;10..-1", LevelOffset: "+1")));
        Assert.Equal("include::C:/Docs/chapter.adoc[tag=\"a&b\",leveloffset=-2]",
            AsciiDocIncludeGenerator.Generate(new(@"C:\Docs\chapter.adoc", @"C:\Docs\main.adoc", false, Tag: "a&b", LevelOffset: "-2")));
        Assert.Equal("include::C:/Docs/chapter.adoc[]", AsciiDocIncludeGenerator.Generate(new(@"C:\Docs\chapter.adoc", null)));
        Assert.Throws<ArgumentException>(() => AsciiDocIncludeGenerator.Generate(new("relative.adoc", null)));
    }

    [Theory]
    [InlineData("0", "", "")]
    [InlineData("1..", "", "")]
    [InlineData("1", "tag", "")]
    [InlineData("", "bad]tag", "")]
    [InlineData("", "", "one")]
    public void IncludeRejectsInvalidOptions(string lines, string tag, string offset)
        => Assert.Throws<ArgumentException>(() => AsciiDocIncludeGenerator.Generate(new(@"C:\Docs\a.adoc", null, Lines: lines, Tag: tag, LevelOffset: offset)));

    [Fact]
    public void BlockImageIncludesRelativePathAndMetadata()
    {
        var definition = new ImageDefinition(@"C:\Docs\images\system.png", @"C:\Docs\main.adoc", AltText: "System, architecture", Title: "構成図", Width: "640", Height: "480", Id: "system");
        Assert.Equal("[#system]\n.構成図\nimage::images/system.png[\"System, architecture\",width=640,height=480]", AsciiDocImageGenerator.Generate(definition));
    }

    [Fact]
    public void InlineImageUsesInlineTitleAndEscapesAttributeValues()
    {
        var definition = new ImageDefinition(@"C:\Docs\icon.png", @"C:\Docs\main.adoc", true, "A \"quote\"]", "Tooltip", Id: "icon");
        Assert.Equal("image:icon.png[\"A &quot;quote&quot;&#93;\",id=icon,title=\"Tooltip\"]", AsciiDocImageGenerator.Generate(definition));
        Assert.Throws<ArgumentException>(() => AsciiDocImageGenerator.Generate(definition with { Width = "-1" }));
    }

    [Fact]
    public void LinksXrefsAndAnchors()
    {
        Assert.Equal("link:https://example.com/a%20b[Example]", AsciiDocLinkGenerator.Generate(new("https://example.com/a b", "Example")));
        Assert.Equal("link:https://example.com[\"a=b, c\"]", AsciiDocLinkGenerator.Generate(new("https://example.com", "a=b, c")));
        Assert.Equal("xref:installation.adoc#setup[手順]", AsciiDocLinkGenerator.Generate(new("installation.adoc#setup", "手順"), true));
        Assert.Equal("xref:setup[]", AsciiDocLinkGenerator.Generate(new("setup"), true));
        Assert.Equal("[[system-architecture]]", AsciiDocLinkGenerator.GenerateAnchor("system-architecture"));
        Assert.Throws<ArgumentException>(() => AsciiDocLinkGenerator.GenerateAnchor("bad id"));
        Assert.Throws<ArgumentException>(() => AsciiDocLinkGenerator.Generate(new("")));
    }

    [Theory]
    [InlineData("C#", "csharp")]
    [InlineData("C++", "cpp")]
    [InlineData("PowerShell", "powershell")]
    [InlineData("my-language", "my-language")]
    public void SourceBlockNormalizesLanguagesAndNewlines(string language, string expected)
        => Assert.Equal($"[source,{expected}]\r\n----\r\none\r\ntwo\r\n----",
            AsciiDocBlockGenerator.Generate(new SourceBlockDefinition(language, "one\ntwo"), "\r\n"));

    [Fact]
    public void SourceBlockAvoidsDelimiterCollisionAndPreservesTrailingNewlines()
    {
        Assert.Equal("[source]\n-----\n----\n\n-----", AsciiDocBlockGenerator.Generate(new SourceBlockDefinition("", "----\n\n")));
        Assert.Throws<ArgumentException>(() => AsciiDocBlockGenerator.Generate(new SourceBlockDefinition("csharp]\nNOTE: bad", "")));
    }

    [Theory]
    [InlineData(AdmonitionKind.NOTE)]
    [InlineData(AdmonitionKind.TIP)]
    [InlineData(AdmonitionKind.IMPORTANT)]
    [InlineData(AdmonitionKind.CAUTION)]
    [InlineData(AdmonitionKind.WARNING)]
    public void AdmonitionsChooseParagraphOrBlock(AdmonitionKind kind)
    {
        Assert.Equal($"{kind}: 注意", AsciiDocBlockGenerator.Generate(new AdmonitionDefinition(kind, "注意")));
        Assert.Equal($"[{kind}]\n====\none\ntwo\n====", AsciiDocBlockGenerator.Generate(new AdmonitionDefinition(kind, "one\ntwo")));
        Assert.Equal($"[{kind}]\n====\n注意\n====", AsciiDocBlockGenerator.Generate(new AdmonitionDefinition(kind, "注意", true)));
    }

    [Theory]
    [InlineData(InlineFormat.Bold, "*重要*")]
    [InlineData(InlineFormat.Italic, "_重要_")]
    [InlineData(InlineFormat.Monospace, "`重要`")]
    public void InlineFormatting(InlineFormat format, string expected)
        => Assert.Equal(expected, AsciiDocTextGenerator.Format("重要", format));

    [Theory]
    [InlineData(ListKind.Unordered, "* Apple\n* Banana\n* Orange")]
    [InlineData(ListKind.Ordered, ". Apple\n. Banana\n. Orange")]
    [InlineData(ListKind.Checklist, "* [ ] Apple\n* [ ] Banana\n* [ ] Orange")]
    public void ListsReuseExistingText(ListKind kind, string expected)
        => Assert.Equal(expected, AsciiDocTextGenerator.List("Apple\nBanana\nOrange", kind));

    [Theory]
    [InlineData(BlockKind.Listing, "----")]
    [InlineData(BlockKind.Literal, "....")]
    [InlineData(BlockKind.Quote, "____")]
    [InlineData(BlockKind.Example, "====")]
    public void GenericBlocks(BlockKind kind, string delimiter)
        => Assert.Equal($"{delimiter}\n本文\n{delimiter}", AsciiDocBlockGenerator.Generate(kind, "本文"));

    [Fact]
    public void FormValidatesBeforeInsertAndPreservesSelectedText()
    {
        var context = new InsertionContext(0, 2, "本文", "\n", @"C:\Docs\main.adoc");
        var form = new InputFormViewModel(AssistanceKind.Link, context);
        Assert.False(form.InsertCommand.CanExecute(null));
        Assert.Equal("本文", form["Text"].Value);
        form["Target"].Value = "https://example.com";
        Assert.True(form.InsertCommand.CanExecute(null));
        Assert.Equal("link:https://example.com[本文]", form.Preview);
        form["Text"].Value = "";
        Assert.Equal(form.Preview.IndexOf('[') + 1, form.Snippet!.CaretOffset);
    }

    [Fact]
    public void FormPrefillsHeadingLevelAndExposesFileBrowseAndAdvancedOptions()
    {
        var context = new InsertionContext(0, 6, "=== 見出し", "\n", null);
        var heading = new InputFormViewModel(AssistanceKind.Heading, context);
        Assert.Equal("2", heading["Level"].Value);
        Assert.Equal("見出し", heading["Title"].Value);
        var include = new InputFormViewModel(AssistanceKind.Include, context, field => field.Value = @"C:\Docs\a.adoc");
        include["File"].BrowseCommand!.Execute(null);
        Assert.True(include.InsertCommand.CanExecute(null));
        Assert.Contains("絶対パス", include.Note);
        Assert.Equal(7, include.AdvancedFields.Count());
    }
}
