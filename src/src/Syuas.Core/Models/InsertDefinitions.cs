namespace Syuas.Core.Models;

public enum AssistanceKind
{
    Heading, Image, Include, Link, CrossReference, Anchor, SourceBlock, Admonition,
    UnorderedList, OrderedList, Checklist, Bold, Italic, Monospace,
    ListingBlock, LiteralBlock, QuoteBlock, ExampleBlock, Table
}

public enum InlineFormat { Bold, Italic, Monospace }
public enum ListKind { Unordered, Ordered, Checklist }
public enum BlockKind { Listing, Literal, Quote, Example }
public enum AdmonitionKind { NOTE, TIP, IMPORTANT, CAUTION, WARNING }

public sealed record HeadingDefinition(int Level, string Title);
public sealed record IncludeDefinition(string FilePath, string? DocumentPath, bool Relative = true,
    string Lines = "", string Tag = "", string LevelOffset = "", string Tags = "", string Indent = "", string Encoding = "", bool Optional = false);
public sealed record ImageDefinition(string FilePath, string? DocumentPath, bool Inline = false,
    string AltText = "", string Title = "", string Width = "", string Height = "", string Id = "", bool Relative = true);
public sealed record LinkDefinition(string Target, string Text = "");
public sealed record SourceBlockDefinition(string Language, string Text);
public sealed record AdmonitionDefinition(AdmonitionKind Kind, string Text, bool UseBlock = false);

// Offsets use .NET string indices, matching the editor adapter contract.
public sealed record InsertionSnippet(string Text, int CaretOffset, bool IsBlock = false);
public sealed record InsertionContext(int Start, int Length, string Text, string NewLine, string? DocumentPath, string DocumentText = "");
