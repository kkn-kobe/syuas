using System.Text.RegularExpressions;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

// Lightweight source navigation only. Conversion remains the responsibility of Asciidoctor.
public static class DocumentStructureParser
{
    public static DocumentStructure Parse(string source)
    {
        List<OutlineEntry> roots = [], headings = [];
        List<ReferenceCandidate> references = [];
        Stack<OutlineEntry> parents = new();
        HashSet<string> ids = new(StringComparer.Ordinal);
        string? block = null, pendingId = null;
        var prefix = "_"; var separator = "_"; var sectionIds = true;
        var offset = 0; var lineNumber = 0;
        foreach (var raw in source.Split('\n'))
        {
            lineNumber++;
            var line = raw.TrimEnd('\r');
            var currentOffset = offset; offset += raw.Length + 1;
            if (block is not null) { if (line.TrimEnd() == block) block = null; continue; }
            if (Regex.IsMatch(line, @"^(?:-{4,}|\.{4,}|/{4,}|\+{4,}|={4,}|_{4,}|\*{4,}|\|={3,})\s*$"))
            { block = line.TrimEnd(); pendingId = null; continue; }
            if (line.StartsWith("```", StringComparison.Ordinal)) { block = "```"; pendingId = null; continue; }
            if (line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (line.StartsWith(":idprefix:", StringComparison.Ordinal)) { prefix = line[10..].Trim(); continue; }
            if (line.StartsWith(":idseparator:", StringComparison.Ordinal)) { separator = line[13..].Trim(); continue; }
            if (line is ":sectids!:" or ":!sectids:") { sectionIds = false; continue; }
            if (line == ":sectids:") { sectionIds = true; continue; }

            var anchor = Regex.Match(line, @"^\s*(?:\[\[([^,\]\s]+)(?:,[^\]]*)?\]\]|\[#([^\]\s]+)\]|\[id=""?([^\]""\s]+)""?\])\s*$");
            if (anchor.Success)
            {
                pendingId = anchor.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value;
                ids.Add(pendingId);
                references.Add(new(pendingId, pendingId, lineNumber, currentOffset, true));
                continue;
            }
            var heading = Regex.Match(line, @"^(={1,6})[ \t]+(.+?)\s*$");
            if (heading.Success)
            {
                var title = heading.Groups[2].Value;
                var inlineId = Regex.Match(title, @"\s+\[\[([^,\]\s]+)(?:,[^\]]*)?\]\]$");
                var id = inlineId.Success ? inlineId.Groups[1].Value : pendingId;
                if (inlineId.Success) title = title[..inlineId.Index];
                var explicitId = id is not null;
                var level = heading.Groups[1].Length - 1;
                if (id is null && level > 0 && sectionIds) id = UniqueId(CreateId(title, prefix, separator), ids, separator);
                if (id is not null)
                {
                    ids.Add(id);
                    if (id == pendingId) references.RemoveAll(r => r.Target == id && r.Offset < currentOffset && r.Title == id);
                    references.Add(new(id, title, lineNumber, currentOffset, explicitId));
                }
                var entry = new OutlineEntry(level, title, lineNumber, currentOffset, id);
                while (parents.Count > 0 && parents.Peek().Level >= level) parents.Pop();
                if (parents.Count == 0) roots.Add(entry); else parents.Peek().Children.Add(entry);
                parents.Push(entry); headings.Add(entry); pendingId = null;
                continue;
            }
            foreach (Match match in Regex.Matches(line, @"(?<!\\)\[\[([^,\]\s]+)(?:,[^\]]*)?\]\]"))
            {
                var id = match.Groups[1].Value; ids.Add(id);
                references.Add(new(id, id, lineNumber, currentOffset + match.Index, true));
            }
            if (!line.StartsWith('[')) pendingId = null;
        }
        return new(roots, headings, references.DistinctBy(r => r.Target).ToArray());
    }

    public static string CreateId(string title, string prefix = "_", string separator = "_")
    {
        var plain = Regex.Replace(title.ToLowerInvariant(), "<[^>]+>", "");
        plain = Regex.Replace(plain, @"[^\p{L}\p{N}\p{M}\s_-]", "");
        plain = Regex.Replace(plain.Trim(), @"[\s_-]+", _ => separator);
        return prefix + (plain.Length == 0 ? "section" : plain);
    }

    public static string UniqueId(string basis, IEnumerable<string> existing, string separator = "_")
    {
        var ids = existing.ToHashSet(StringComparer.Ordinal);
        if (!ids.Contains(basis)) return basis;
        for (var number = 2; ; number++)
        {
            var candidate = basis + separator + number;
            if (!ids.Contains(candidate)) return candidate;
        }
    }
}
