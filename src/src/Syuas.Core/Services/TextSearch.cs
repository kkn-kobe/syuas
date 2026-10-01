namespace Syuas.Core.Services;

public static class TextSearch
{
    public static int FindNext(string text, string query, int start, bool matchCase)
    {
        if (query.Length == 0) return -1;
        start = Math.Clamp(start, 0, text.Length);
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var index = text.IndexOf(query, start, comparison);
        return index >= 0 ? index : text.IndexOf(query, comparison);
    }

    public static IReadOnlyList<int> FindAll(string text, string query, bool matchCase)
    {
        List<int> matches = [];
        if (query.Length == 0) return matches;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var start = 0; start <= text.Length - query.Length;)
        {
            var index = text.IndexOf(query, start, comparison);
            if (index < 0) break;
            matches.Add(index);
            start = index + query.Length;
        }
        return matches;
    }
}
