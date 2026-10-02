using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Syuas.Core.Services;
using Syuas.Core.Models;

namespace Syuas.App.Views;

public partial class HtmlPreviewControl : UserControl, IDisposable
{
    private const string AppHost = "app.syuas.local";
    private Task? initialization;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? documentPath;
    private long version;
    private bool disposed;
    private readonly PreviewDataMode dataMode;
    private readonly string dataRoot;
    private PreviewDataSession? session;
    private CoreWebView2Environment? environment;
    private bool browserStarted;
    private readonly TaskCompletionSource browserExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string StatusText => Status.Text;
    public long RenderedVersion { get; private set; }
    public HtmlPreviewControl() : this(PreviewDataMode.Keep,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SYUAS")) { }
    public HtmlPreviewControl(PreviewDataMode mode, string root)
    {
        dataMode = mode;
        dataRoot = root;
        InitializeComponent();
    }

    public void InvalidateDocument()
    {
        version++;
        documentPath = null;
        Browser.Visibility = System.Windows.Visibility.Hidden;
        Status.Text = "HTMLプレビューを更新しています…";
    }

    public async Task RenderAsync(string source, string? path)
    {
        if (disposed || dataMode == PreviewDataMode.Disabled) return;
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
        var data = Path.Combine(dataRoot, "WebView2");
        if (dataMode == PreviewDataMode.DeleteOnExit)
        {
            session = new PreviewDataSession(Path.Combine(dataRoot, "PreviewSessions"));
            data = session.DataFolder;
        }
        environment = await CoreWebView2Environment.CreateAsync(userDataFolder: data);
        // Environment overrides can redirect WebView2. Do not send document content there.
        if (!string.Equals(Path.GetFullPath(environment.UserDataFolder), Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase))
            throw new IOException("WebView2の保存先が設定と異なるため、プレビューを停止しました。");
        environment.BrowserProcessExited += OnBrowserExited;
        if (disposed) return;
        await Browser.EnsureCoreWebView2Async(environment);
        browserStarted = true;
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

    private void OnBrowserExited(object? sender, CoreWebView2BrowserProcessExitedEventArgs e) => browserExited.TrySetResult();

    public async Task<string?> ShutdownAsync()
    {
        Dispose();
        try
        {
            // Initialization may be awaiting WebView2 creation. Let it settle before cleanup.
            if (initialization is not null)
            {
                try { await initialization.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException) { return "プレビューの初期化終了を確認できません。残存データは次回起動時に削除を再試行します。"; }
                catch (Exception e) when (e is not OutOfMemoryException) { }
            }
            if (session is null) return null;
            if (browserStarted)
                await browserExited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Failed initialization may still leave locked files. Report a deletion failure.
            await Task.Run(session.DeleteData);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException)
        { return $"プレビューデータを削除できませんでした。次回起動時に再試行します。\n{session?.DataFolder}\n{e.Message}"; }
        finally
        {
            if (environment is not null) environment.BrowserProcessExited -= OnBrowserExited;
            session?.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; version++; ready.TrySetCanceled(); Browser.Dispose();
    }
}

