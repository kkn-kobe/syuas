using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Syuas.App.Views;
using Syuas.Core.Models;

namespace Syuas.Tests;

public sealed class MermaidPreviewTests
{
    [Fact]
    public void DistributedBundleAndSourcesMatchLicenseInventory()
    {
        var root = AppContext.BaseDirectory;
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Licenses", "Mermaid-INVENTORY.json")));
        var data = inventory.RootElement;
        Assert.Equal("12.3.0", data.GetProperty("version").GetString());
        Assert.Equal(data.GetProperty("distributedBundleSha256").GetString(), Hash(Path.Combine(root, "PreviewAssets", "mermaid", "mermaid.min.js")));
        foreach (var archive in data.GetProperty("sourceArchives").EnumerateArray())
            Assert.Equal(archive.GetProperty("sha256").GetString(), Hash(Path.Combine(root, "Licenses", "MermaidSources", archive.GetProperty("file").GetString()!)));
        var notices = File.ReadAllText(Path.Combine(root, "Licenses", "Mermaid-NOTICES.txt"));
        foreach (var package in data.GetProperty("packages").EnumerateArray())
        {
            Assert.Contains(package.GetProperty("name").GetString()! + " " + package.GetProperty("version").GetString(), notices);
            Assert.Contains(package.GetProperty("selectedLicense").GetString()!, new[] { "MIT", "ISC", "BSD-3-Clause", "Apache-2.0", "EPL-2.0", "Unlicense" });
        }
        Assert.Contains("Eclipse Public License", notices);
        Assert.Contains("EMF GWT", notices);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OfflineWebViewRendersDiagramsAndKeepsDocumentsInert(bool openSampleFirst)
    {
        if (Environment.GetEnvironmentVariable("SYUAS_MERMAID_SMOKE") is not { Length: > 0 } output) return;
        Sta.Run(() => CheckWebView(Path.Combine(Path.GetFullPath(output), openSampleFirst ? "sample-first" : "offline-first"), openSampleFirst), timeoutSeconds: 150);
    }

    private static void CheckWebView(string output, bool openSampleFirst)
    {
        Directory.CreateDirectory(output);
        var dataRoot = Path.Combine(output, "profile-" + Guid.NewGuid().ToString("N"));
        using var preview = new HtmlPreviewControl(PreviewDataMode.DeleteOnExit, dataRoot);
        var host = new Window { Content = preview, Width = 1000, Height = 850, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(host.Dispatcher));
        host.Show();
        try
        {
            preview.InvalidateDocument();
            var samplePath = Path.Combine(AppContext.BaseDirectory, "MermaidFixtures", "local-preview.adoc");
            Render(openSampleFirst ? File.ReadAllText(samplePath) : "== Initial", openSampleFirst ? samplePath : null);
            var browser = (WebView2)preview.FindName("Browser");
            var core = browser.CoreWebView2;
            if (openSampleFirst)
            {
                Assert.DoesNotContain("Mermaid 図", preview.StatusText);
                Assert.Equal(3, Eval<int>("document.getElementById('preview').contentDocument.querySelectorAll('img.mermaid-diagram').length"));
                Assert.True(Eval<bool>("Array.from(document.getElementById('preview').contentDocument.querySelectorAll('img.mermaid-diagram')).every(i=>i.complete&&i.naturalWidth>40&&i.naturalHeight>40)"));
                Assert.Equal("visible", Eval<string>("getComputedStyle(document.getElementById('preview')).visibility"));
                using var screenshot = File.Create(Path.Combine(output, "local-preview.png"));
                WaitTask(core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot));
            }
            // The plain-document case blocks the network before Mermaid is first loaded.
            Wait(core.CallDevToolsProtocolMethodAsync("Network.enable", "{}"));
            Wait(core.CallDevToolsProtocolMethodAsync("Network.emulateNetworkConditions",
                "{\"offline\":true,\"latency\":0,\"downloadThroughput\":0,\"uploadThroughput\":0}"));
            var remoteRequests = new List<string>();
            var events = core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
            events.DevToolsProtocolEventReceived += (_, e) =>
            {
                using var message = JsonDocument.Parse(e.ParameterObjectAsJson);
                var url = message.RootElement.GetProperty("request").GetProperty("url").GetString()!;
                if (url.StartsWith("http", StringComparison.Ordinal) && !new Uri(url).Host.EndsWith(".syuas.local", StringComparison.Ordinal)) remoteRequests.Add(url);
            };
            var source = """
                = Mermaid 検証

                本文は通常どおり表示します。

                .日本語のフローチャート
                [mermaid]
                ----
                flowchart LR
                    A[文書を編集] --> B[ローカル描画]
                    B --> C[プレビュー]
                ----

                [mermaid]
                ----
                sequenceDiagram
                    participant U as 利用者
                    participant S as SYUAS
                    U->>S: 図を入力
                    S-->>U: SVG を表示
                ----

                [mermaid]
                ----
                classDiagram
                    Document --> Preview
                ----

                [mermaid]
                ----
                stateDiagram-v2
                    [*] --> Editing
                    Editing --> Preview
                ----
                """;
            // MainWindow invalidates the preview on document/tab changes.
            // Reproduce that lifecycle rather than rendering into an already visible WebView.
            preview.InvalidateDocument();
            Render(source);
            Assert.DoesNotContain("Mermaid 図", preview.StatusText);
            Pump(() => Eval<int>("document.getElementById('preview').contentDocument.querySelectorAll('img.mermaid-diagram').length") == 4);
            Pump(() => Eval<bool>("Array.from(document.getElementById('preview').contentDocument.querySelectorAll('img.mermaid-diagram')).every(i=>i.complete&&i.naturalWidth>40&&i.naturalHeight>40)"));
            Assert.Equal(Visibility.Visible, browser.Visibility);
            Assert.Equal("visible", Eval<string>("getComputedStyle(document.getElementById('preview')).visibility"));
            var svgs = Eval<string[]>("Array.from(document.getElementById('preview').contentDocument.querySelectorAll('img.mermaid-diagram')).map(i=>decodeURIComponent(i.getAttribute('src').split(',').slice(1).join(',')))");
            Assert.Contains("文書を編集", svgs[0]);
            Assert.Contains("利用者", svgs[1]);
            Assert.All(svgs, svg => { Assert.Contains("<svg", svg); Assert.DoesNotContain("<foreignObject", svg); });
            Assert.Empty(remoteRequests);
            using (var screenshot = File.Create(Path.Combine(output, "mermaid-preview.png")))
                WaitTask(core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot));

            Render("""
                == Safe document

                [mermaid]
                ----
                flowchart LR
                    A[Safe] --> B[Still safe]
                    click A "https://example.invalid/leak"
                ----

                [mermaid]
                ----
                this is not a diagram
                ----

                [mermaid]
                ----
                %%{init: {"theme":"neutral"}}%%
                flowchart LR
                    A --> B
                ----

                [source,mermaid]
                ----
                flowchart LR
                    Code --> Only
                ----

                Normal text after the diagrams.
                """);
            Pump(() => Eval<int>("document.getElementById('preview').contentDocument.querySelectorAll('.mermaid-error').length") == 2);
            Assert.Equal(1, Eval<int>("document.getElementById('preview').contentDocument.querySelectorAll('img.mermaid-diagram').length"));
            var html = Eval<string>("document.getElementById('preview').contentDocument.body.innerHTML");
            Assert.Contains("Normal text after", html);
            Assert.Contains("Code", html);
            Assert.Empty(remoteRequests);
            Assert.Equal("allow-same-origin", Eval<string>("document.getElementById('preview').getAttribute('sandbox')"));
            Assert.Equal("allow-scripts", Eval<string>("document.querySelector('iframe[aria-hidden]').getAttribute('sandbox')"));

            Render("[mermaid]\n----\n" + new string('A', 20001) + "\n----");
            Pump(() => Eval<bool>("document.getElementById('preview').contentDocument.body.textContent.includes('20,000')"));
            Render("[mermaid]\n----\nflowchart LR\nA --> B\n----");
            Pump(() => Eval<int>("document.getElementById('preview').contentDocument.querySelectorAll('img.mermaid-diagram').length") == 1);
            // Include files go through the normal AsciiDoc parser.
            File.WriteAllText(Path.Combine(output, "diagram.adoc"), "[mermaid]\r\n----\r\nflowchart LR\r\nIncluded --> Local\r\n----\r\n");
            Render("include::diagram.adoc[]", Path.Combine(output, "main.adoc"));
            Pump(() => Eval<int>("document.getElementById('preview').contentDocument.querySelectorAll('img.mermaid-diagram').length") == 1);
            var old = preview.RenderAsync(source, null);
            var latest = preview.RenderAsync("== Latest document", null);
            WaitTask(old); WaitTask(latest);
            Pump(() => Eval<bool>("document.getElementById('preview').contentDocument.body.textContent.includes('Latest document')"));
            Assert.Equal(0, Eval<int>("document.getElementById('preview').contentDocument.querySelectorAll('img.mermaid-diagram').length"));
            Assert.Empty(remoteRequests);

            void Render(string text, string? path = null)
            {
                var previous = preview.RenderedVersion;
                WaitTask(preview.RenderAsync(text, path));
                Pump(() => preview.RenderedVersion > previous || preview.StatusText.Contains("失敗") || preview.StatusText.Contains("できません"));
                Assert.True(preview.RenderedVersion > previous, preview.StatusText);
            }
            T Eval<T>(string script) => JsonSerializer.Deserialize<T>(Wait(browser.ExecuteScriptAsync(script)))!;
        }
        finally
        {
            Assert.Null(Wait(preview.ShutdownAsync()));
            host.Close();
        }

        T Wait<T>(Task<T> task) { Pump(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
        void WaitTask(Task task) { Pump(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
        void Pump(Func<bool> done)
        {
            var timer = Stopwatch.StartNew();
            while (!done())
            {
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(35), "Mermaid WebView2 check timed out. " + preview.StatusText);
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(15);
            }
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
