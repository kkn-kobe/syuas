using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Syuas.Core.Services;

namespace Syuas.App.Views;

public partial class HtmlPreviewControl : UserControl, IDisposable
{
    private const string AppHost = "app.syuas.local";
    private Task? initialization;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? documentPath;
    private long version;
    private bool disposed;
    public string StatusText => Status.Text;
    public long RenderedVersion { get; private set; }
    public HtmlPreviewControl() => InitializeComponent();

    public void InvalidateDocument()
    {
        version++;
        documentPath = null;
        Browser.Visibility = System.Windows.Visibility.Hidden;
        Status.Text = "HTMLプレビューを更新しています…";
    }

    public async Task RenderAsync(string source, string? path)
    {
        if (disposed) return;
        var requestVersion = ++version;
        documentPath = path;
        Status.Text = "HTMLプレビューを更新しています…";
        try
        {
            initialization ??= InitializeBrowserAsync();
            await initialization;
            if (disposed || requestVersion != version) return;
            var request = JsonSerializer.Serialize(new { source, version = requestVersion, saved = path is not null,
                resourceBase = $"https://{requestVersion}.document.syuas.local" });
            await Browser.ExecuteScriptAsync($"window.renderPreview({request});");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (!disposed && requestVersion == version)
                Status.Text = "プレビューを表示できません。WebView2 Runtimeの導入状態を確認してください。\n" + e.Message;
        }
    }

    private async Task InitializeBrowserAsync()
    {
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SYUAS", "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: data);
        if (disposed) return;
        await Browser.EnsureCoreWebView2Async(environment);
        if (disposed) return;
        var core = Browser.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.SetVirtualHostNameToFolderMapping(AppHost, Path.Combine(AppContext.BaseDirectory, "PreviewAssets"), CoreWebView2HostResourceAccessKind.DenyCors);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnResourceRequested;
        core.WebMessageReceived += OnMessage;
        core.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != AppHost || uri.AbsolutePath != "/index.html") e.Cancel = true;
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.Navigate($"https://{AppHost}/index.html");
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (disposed || !e.Source.StartsWith($"https://{AppHost}/", StringComparison.Ordinal)) return;
        using var json = JsonDocument.Parse(e.WebMessageAsJson);
        var message = json.RootElement;
        var type = message.GetProperty("type").GetString();
        if (type == "ready") { ready.TrySetResult(); return; }
        if (message.GetProperty("version").GetInt64() != version) return;
        var details = message.GetProperty("message").GetString();
        if (type == "rendered")
        {
            Browser.Visibility = System.Windows.Visibility.Visible;
            RenderedVersion = version;
            Status.Text = string.IsNullOrEmpty(details)
                ? documentPath is null ? "更新済み（未保存文書のincludeは展開しません）" : "更新済み（文書フォルダー内の画像・includeに対応）"
                : "更新済み・変換メッセージ: " + details;
        }
        else Status.Text = "HTML変換に失敗しました: " + details;
    }

    private async void OnResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == AppHost) return;
        var deferral = e.GetDeferral();
        var requestVersion = version;
        try
        {
            // Resource URLs carry a render identity so a previous tab cannot read the new tab's files.
            var resourceUri = Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var requested)
                && requested.Scheme == "https" && requested.Host == $"{requestVersion}.document.syuas.local"
                ? new UriBuilder(requested) { Host = "document.syuas.local" }.Uri.AbsoluteUri : "";
            var path = PreviewResourcePolicy.Resolve(resourceUri, documentPath);
            if (path is not null && File.Exists(path) && new FileInfo(path).Length <= 16 * 1024 * 1024)
            {
                var bytes = await File.ReadAllBytesAsync(path);
                if (disposed || requestVersion != version) return;
                var mime = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".svg" => "image/svg+xml",
                    ".webp" => "image/webp", ".bmp" => "image/bmp", _ => "text/plain"
                };
                e.Response = Browser.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK",
                    $"Content-Type: {mime}\r\nAccess-Control-Allow-Origin: https://{AppHost}\r\nCache-Control: no-store");
                return;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        finally
        {
            try
            {
                if (!disposed && e.Response is null)
                    e.Response = Browser.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not Found", $"Access-Control-Allow-Origin: https://{AppHost}");
                deferral.Dispose();
            }
            catch (System.Runtime.InteropServices.COMException) when (disposed) { }
        }
    }

    public void Dispose() { disposed = true; version++; ready.TrySetCanceled(); Browser.Dispose(); }
}

