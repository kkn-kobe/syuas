namespace Syuas.Core.Models;

public sealed record ReferenceCandidate(string Target, string Title, int Line, int Offset, bool Explicit)
{
    public string DisplayName => $"{Title}  [{Target}] — {Line}行";
}

public sealed class OutlineEntry(int level, string title, int line, int offset, string? id)
{
    public int Level { get; } = level;
    public string Title { get; } = title;
    public int Line { get; } = line;
    public int Offset { get; } = offset;
    public string? Id { get; } = id;
    public string Location => $"{Line}行";
    public List<OutlineEntry> Children { get; } = [];
}

public sealed record DocumentStructure(IReadOnlyList<OutlineEntry> Roots, IReadOnlyList<OutlineEntry> Headings, IReadOnlyList<ReferenceCandidate> References);
