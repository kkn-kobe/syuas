using System.Text.RegularExpressions;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

// Conservative source navigation for editing, not a full AsciiDoc document parser.
// Compound blocks are excluded as well as verbatim blocks; includes are never expanded.
public static class AsciiDocTableLocator
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
    private static readonly Regex Anchor = new(@"\A(?:\[\[[^\]\s,]+(?:,[^\]]*)?\]\]|\[#[^\]\s]+\])\z", Options);
    private static readonly Regex ConditionalStart = new(@"\A(?:ifdef|ifndef|ifeval)::", Options);
    private static readonly Regex Fence = new(@"\A(?<delimiter>`{3,}|~{3,})(?<info>.*)\z", Options);

    public static TableLocationResult Locate(string source, int selectionStart, int selectionLength = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (selectionStart < 0 || selectionLength < 0 || selectionStart > source.Length || selectionLength > source.Length - selectionStart)
            return Failure(TableEditDiagnosticCode.InvalidSelection, "選択範囲が文書の範囲外です。");
        var lines = ReadLines(source);
        var candidates = new List<Candidate>();
        var blocks = new Stack<Block>();
        var conditionalDepth = 0;
        var literalParagraph = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var text = lines[i].Text;
            // Ignore conditional sections instead of trying to evaluate document attributes.
            if (ConditionalStart.IsMatch(text)) { conditionalDepth++; continue; }
            if (text.StartsWith("endif::", StringComparison.Ordinal)) { if (conditionalDepth > 0) conditionalDepth--; continue; }
            if (conditionalDepth > 0) continue;
            if (blocks.TryPeek(out var active))
            {
                if (active.Closes(text)) blocks.Pop();
                else if (active.Compound && ReadBlock(text) is { } nested) blocks.Push(nested);
                continue;
            }
            if (literalParagraph)
            {
                if (string.IsNullOrWhiteSpace(text)) literalParagraph = false;
                continue;
            }
            if (text.Length > 0 && char.IsWhiteSpace(text[0]) && !string.IsNullOrWhiteSpace(text))
            { literalParagraph = true; continue; }
            if (text == "|===")
            {
                var start = MetadataStart(lines, i);
                var closing = -1;
                var nextOpening = -1;
                for (var j = i + 1; j < lines.Count; j++)
                {
                    if (lines[j].Text != "|===") continue;
                    // A new table declaration before the putative closing delimiter means
                    // the previous table is incomplete (or its boundary is ambiguous).
                    if (j > i + 1 && IsAttribute(lines[j - 1].Text)) nextOpening = j;
                    else closing = j;
                    break;
                }
                if (closing < 0)
                {
                    var end = nextOpening < 0 ? source.Length : lines[MetadataStart(lines, nextOpening)].Start;
                    candidates.Add(new(start, lines[start].Start, nextOpening < 0 ? end : end - 1, end,
                        new(TableEditDiagnosticCode.MissingClosingDelimiter, "終了区切りを確定できません。次の表を巻き込まないよう、表の区切りを確認してください。", i + 1)));
                    if (nextOpening < 0) break;
                    i = nextOpening - 1;
                    continue;
                }
                TableEditDiagnostic? error = null;
                // Leading independent anchors/comments stay outside the replacement. Any
                // remaining metadata must be exactly [attributes], or title + [attributes].
                if (!(start == i - 1 && IsAttribute(lines[start].Text) ||
                    start == i - 2 && IsTitle(lines[start].Text) && IsAttribute(lines[start + 1].Text)))
                    error = new(TableEditDiagnosticCode.UnsupportedContext, "表のタイトル・属性・Anchorの対応を確定できません。現在はタイトル行と単独の属性行を持つ表に対応しています。", start + 1);
                // Without a paragraph boundary this could still be part of an undelimited
                // [source]/[literal] paragraph or a list continuation. Do not guess its context.
                var envelopeStart = AttachedStart(lines, i);
                if (envelopeStart > 0 && !string.IsNullOrWhiteSpace(lines[envelopeStart - 1].Text) && lines[envelopeStart - 1].Text != "|===")
                    error = new(TableEditDiagnosticCode.UnsupportedContext, "表の前の段落との境界を確定できません。表の前に空行を置いてください。リストの継続内の表は対象外です。", start + 1);
                var preceding = envelopeStart - 1;
                while (preceding >= 0 && string.IsNullOrWhiteSpace(lines[preceding].Text)) preceding--;
                if (preceding >= 0 && lines[preceding].Text == "+")
                    error = new(TableEditDiagnosticCode.UnsupportedContext, "リストの継続内の表は現在の再編集対象外です。", start + 1);
                var following = closing + 1;
                while (following < lines.Count && string.IsNullOrWhiteSpace(lines[following].Text)) following++;
                if (following < lines.Count && LooksLikeCell(lines[following].Text))
                    error = new(TableEditDiagnosticCode.AmbiguousBoundary, "終了区切りの後にもセルと思われる行があります。表の範囲を確定できません。", following + 1);
                candidates.Add(new(start, lines[start].Start, lines[closing].End, lines[closing].Next, error));
                i = closing;
                continue;
            }
            if (ReadBlock(text) is { } block) blocks.Push(block);
        }

        var selectionEnd = selectionStart + selectionLength;
        var matching = candidates.Where(c => selectionLength == 0
            ? selectionStart >= c.Start && selectionStart <= c.End
            : selectionStart < c.SelectionEnd && selectionEnd > c.Start).ToArray();
        if (matching.Length == 0)
            return Failure(TableEditDiagnosticCode.NotInTable, "再編集できる表の中にカーソルを置いてください。コード例・コメント・複合ブロック・条件分岐内の表は対象外です。", LineAt(lines, selectionStart));
        var candidate = matching[0];
        if (matching.Length != 1 || selectionStart < candidate.Start || selectionLength > 0 && selectionEnd > candidate.SelectionEnd)
            return Failure(TableEditDiagnosticCode.SelectionCrossesBoundary, "選択範囲が表の外側や複数の表にまたがっています。1つの表の中を選択してください。", LineAt(lines, selectionStart));
        if (candidate.Error is not null) return new(null, candidate.Error);
        var original = source[candidate.Start..candidate.End];
        var lf = original.IndexOf('\n');
        var newLine = lf > 0 && original[lf - 1] == '\r' ? "\r\n" : "\n";
        return new(new(candidate.Start, candidate.End - candidate.Start, candidate.StartLineIndex + 1, original, newLine), null);
    }

    private static int MetadataStart(IReadOnlyList<Line> lines, int opening)
    {
        var start = AttachedStart(lines, opening);
        while (start < opening && (Anchor.IsMatch(lines[start].Text) || IsComment(lines[start].Text))) start++;
        return start;
    }
    private static int AttachedStart(IReadOnlyList<Line> lines, int opening)
    {
        var start = opening;
        while (start > 0 && (IsAttribute(lines[start - 1].Text) || IsTitle(lines[start - 1].Text) || IsComment(lines[start - 1].Text))) start--;
        return start;
    }
    private static bool IsAttribute(string line) => line.StartsWith('[');
    private static bool IsTitle(string line) => line.StartsWith('.') &&
        !(line.TrimEnd().Length >= 4 && line.TrimEnd().All(c => c == '.'));
    private static bool IsComment(string line) => line.StartsWith("//", StringComparison.Ordinal) && !line.StartsWith("////", StringComparison.Ordinal);
    private static bool LooksLikeCell(string line)
    {
        var separator = line.IndexOf('|');
        return separator >= 0 && line[..separator].All(c => char.IsAsciiDigit(c) || c is '.' or '+');
    }

    private static Block? ReadBlock(string text)
    {
        var trimmed = text.TrimEnd();
        var fence = Fence.Match(trimmed);
        if (fence.Success) return new(fence.Groups["delimiter"].Value, false, true);
        if (trimmed == "--") return new(trimmed, true);
        if (trimmed.Length >= 4 && trimmed.All(c => c == trimmed[0]) && "-./+=_*".Contains(trimmed[0]))
            return new(trimmed, "=_*".Contains(trimmed[0]));
        if (trimmed.Length >= 4 && "|!,;:".Contains(trimmed[0]) && trimmed[1..].All(c => c == '='))
            return new(trimmed, false);
        return null;
    }
    private sealed record Block(string Delimiter, bool Compound, bool IsFence = false)
    {
        public bool Closes(string line)
        {
            var trimmed = line.TrimEnd();
            return IsFence ? trimmed.Length >= Delimiter.Length && trimmed.All(c => c == Delimiter[0]) : trimmed == Delimiter;
        }
    }
    private sealed record Line(string Text, int Start, int End, int Next);
    private sealed record Candidate(int StartLineIndex, int Start, int End, int SelectionEnd, TableEditDiagnostic? Error);
    private static List<Line> ReadLines(string source)
    {
        var lines = new List<Line>();
        var start = 0;
        while (true)
        {
            var lf = source.IndexOf('\n', start);
            var end = lf < 0 ? source.Length : lf;
            if (end > start && source[end - 1] == '\r') end--;
            lines.Add(new(source[start..end], start, end, lf < 0 ? source.Length : lf + 1));
            if (lf < 0) break;
            start = lf + 1;
        }
        return lines;
    }
    private static int LineAt(IReadOnlyList<Line> lines, int offset)
    {
        for (var i = lines.Count - 1; i >= 0; i--) if (offset >= lines[i].Start) return i + 1;
        return 1;
    }
    private static TableLocationResult Failure(TableEditDiagnosticCode code, string message, int line = 1)
        => new(null, new(code, message, line));
}
