using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using Syuas.App.Views;
using Syuas.Core.Services;

namespace Syuas.Tests;

// Opt-in integration check: the production WebView2 preview and bundled Asciidoctor.js.
internal static class TableRenderingChecks
{
    internal static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var reportPath = Path.Combine(directory, "results.json");
        File.WriteAllText(reportPath, "{\"completed\":false}");
        using var preview = new HtmlPreviewControl();
        var host = new Window { Content = preview, Width = 850, Height = 650, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        host.Show();
        var results = new List<object>();
        try
        {
            foreach (var example in TableCompatibilityFixtures.All)
            {
                var source = TableCompatibilityFixtures.Read(example.Name);
                var model = TableCompatibilityFixtures.ParseAndVerify(example, source);
                var regenerated = AsciiDocTableGenerator.Generate(model);
                model.CellAt(0, 0).Text += " 更新";
                var edited = AsciiDocTableGenerator.Generate(model);
                JsonElement? original = null;
                foreach (var (variant, text) in new[] { ("original", source), ("roundtrip", regenerated), ("edited", edited) })
                {
                    var marker = example.Name + "-" + variant;
                    var previousVersion = preview.RenderedVersion;
                    var render = host.Dispatcher.Invoke(() => preview.RenderAsync("== " + marker + "\n\n" + text, null));
                    Pump(host, () => render.IsCompleted && (preview.RenderedVersion > previousVersion || preview.StatusText.Contains("できません") || preview.StatusText.Contains("失敗")));
                    render.GetAwaiter().GetResult();
                    Assert.True(preview.RenderedVersion > previousVersion, preview.StatusText);
                    JsonElement state = default;
                    Pump(host, () =>
                    {
                        var inspect = ((WebView2)preview.FindName("Browser")).ExecuteScriptAsync(InspectScript);
                        Pump(host, () => inspect.IsCompleted);
                        state = JsonSerializer.Deserialize<JsonElement>(inspect.GetAwaiter().GetResult());
                        return state.ValueKind == JsonValueKind.Object && state.GetProperty("heading").GetString() == marker;
                    });
                    File.WriteAllText(Path.Combine(directory, marker + ".html"), state.GetProperty("html").GetString());
                    File.WriteAllText(Path.Combine(directory, marker + ".json"), state.GetRawText());
                    var table = Assert.Single(state.GetProperty("tables").EnumerateArray());
                    Verify(example, table, variant == "edited");
                    if (variant == "original") original = table.Clone();
                    if (variant == "roundtrip") Assert.Equal(original!.Value.GetRawText(), table.GetRawText());
                    results.Add(new { fixture = example.Name, variant, passed = true });
                    if (variant == "edited" && example.Name is "header" or "spans" or "entities")
                    {
                        using var stream = File.Create(Path.Combine(directory, marker + ".png"));
                        var capture = ((WebView2)preview.FindName("Browser")).CoreWebView2.CapturePreviewAsync(
                            Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, stream);
                        Pump(host, () => capture.IsCompleted);
                        capture.GetAwaiter().GetResult();
                    }
                }
            }
            var browser = (WebView2)preview.FindName("Browser");
            var engineHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "PreviewAssets", "asciidoctor.js"))));
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                completed = true, utc = DateTimeOffset.UtcNow, engineSha256 = engineHash,
                webView2 = browser.CoreWebView2.Environment.BrowserVersionString, cases = results
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { host.Close(); }
    }

    private static void Verify(TableCompatibilityFixtures.Example expected, JsonElement table, bool edited)
    {
        Assert.Equal(expected.Header ? 1 : 0, table.GetProperty("headerRows").GetInt32());
        var caption = table.GetProperty("caption").GetString();
        if (expected.Title.Length == 0) Assert.Equal("", caption);
        else Assert.EndsWith(expected.Title, caption);
        var widths = table.GetProperty("widths").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        Assert.Equal(expected.Columns, widths.Length);
        for (var i = 0; i < widths.Length; i++)
            Assert.InRange(widths[i], expected.Widths[i] * 100.0 / expected.Widths.Sum() - 0.001, expected.Widths[i] * 100.0 / expected.Widths.Sum() + 0.001);
        var rows = table.GetProperty("rows").EnumerateArray().ToArray();
        // Asciidoctor emits rows only where cells start. In a fully merged table,
        // the one TD retains rowspan=3, but there are no two empty TR elements.
        var anchorRows = expected.Cells.Select(c => c.Row).Distinct().ToArray();
        Assert.Equal(anchorRows.Length, rows.Length);
        var occupied = new bool[expected.Rows, expected.Columns];
        var cells = new List<(int Row, int Column, int RowSpan, int ColumnSpan, string Text)>();
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var r = anchorRows[rowIndex];
            var column = 0;
            foreach (var cell in rows[rowIndex].EnumerateArray())
            {
                while (column < expected.Columns && occupied[r, column]) column++;
                var rowSpan = cell.GetProperty("rowSpan").GetInt32();
                var colSpan = cell.GetProperty("colSpan").GetInt32();
                Assert.InRange(column + colSpan, 1, expected.Columns);
                Assert.InRange(r + rowSpan, 1, expected.Rows);
                Assert.Equal(expected.Header && r == 0 ? "TH" : "TD", cell.GetProperty("tag").GetString());
                for (var y = r; y < r + rowSpan; y++)
                    for (var x = column; x < column + colSpan; x++)
                    { Assert.False(occupied[y, x]); occupied[y, x] = true; }
                cells.Add((r, column, rowSpan, colSpan, cell.GetProperty("text").GetString()!));
                column += colSpan;
            }
        }
        Assert.All(occupied.Cast<bool>(), Assert.True);
        Assert.Equal(expected.Cells.Select((c, i) => (c.Row, c.Column, c.RowSpan, c.ColumnSpan, (c.Text + (edited && i == 0 ? " 更新" : "")).Trim())), cells);
        if (expected.Name == "entities")
        {
            Assert.Equal(1, table.GetProperty("strong").GetInt32());
            Assert.Equal(1, table.GetProperty("code").GetInt32());
            Assert.Equal(2, rows[1][1].GetProperty("paragraphs").GetInt32());
        }
    }

    private const string InspectScript = """
        (() => {
          const d = document.getElementById('preview')?.contentDocument;
          if (!d?.body) return null;
          const text = e => e.textContent.replace(/\s+/g, ' ').trim();
          return { heading: d.querySelector('h2')?.textContent, html: d.body.innerHTML,
            tables: Array.from(d.querySelectorAll('table')).map(t => ({
              caption: t.caption ? text(t.caption) : '', headerRows: t.tHead?.rows.length ?? 0,
              widths: Array.from(t.querySelectorAll('col')).map(c => parseFloat(c.style.width || c.getAttribute('width'))),
              strong: t.querySelectorAll('strong').length, code: t.querySelectorAll('code').length,
              rows: Array.from(t.rows).map(r => Array.from(r.cells).map(c => ({
                tag: c.tagName, rowSpan: c.rowSpan, colSpan: c.colSpan, text: text(c), paragraphs: c.querySelectorAll('p').length
              })))
            })) };
        })()
        """;

    private static void Pump(Window host, Func<bool> completed)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!completed())
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), "Table compatibility preview timed out.");
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(10);
        }
    }
}
