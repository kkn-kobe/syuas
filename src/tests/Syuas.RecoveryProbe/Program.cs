using System.Diagnostics;
using System.Reflection;
using Syuas.Core.Models;
using Syuas.Core.Services;

const string originalText = "original\n";
const string recoveredText = "= 障害時の検証\r\n\r\n未保存の本文 😀\n";

if (args is ["--worker", var mode, var workerRoot])
{
    using var store = new RecoveryStore(workerRoot);
    var originalPath = Path.Combine(workerRoot, "original.adoc");
    var baseline = new FileBaseline(originalPath, FileFingerprint.FromBytes(System.Text.Encoding.UTF8.GetBytes(originalText)));
    switch (mode)
    {
        case "write":
        case "pump":
            var snapshot = new RecoverySnapshot(Guid.NewGuid(), 1, DateTimeOffset.UtcNow, "previous", baseline, 2, 4, 6);
            store.Write(snapshot);
            snapshot = snapshot with { Text = recoveredText, Revision = 2 };
            store.Write(snapshot);
            // Emulate an incomplete temporary file as well as terminating the live writer.
            File.WriteAllText(Path.Combine(workerRoot, store.SessionId.ToString("N"), $"{snapshot.DocumentId:N}.{Guid.NewGuid():N}.tmp"), "{partial");
            Console.WriteLine("READY");
            if (mode == "write") await Task.Delay(Timeout.Infinite);
            else
                while (true)
                {
                    snapshot = snapshot with { Revision = snapshot.Revision + 1, CapturedAt = DateTimeOffset.UtcNow };
                    store.Write(snapshot);
                }
            break;
        case "empty":
            Require(store.ListCandidates().Count == 0, "使用中または整理済みのコピーが候補に現れました。");
            break;
        case "claim":
        case "retire":
            var candidates = store.ListCandidates();
            Require(candidates.Count == 1, $"候補数が不正です: {candidates.Count}");
            var copy = store.Claim(candidates[0].Key);
            Require(copy.Text == recoveredText && copy.Revision >= 2, "本文またはリビジョンが一致しません。");
            Require(copy.Baseline == baseline, "退避時の保存基準が失われました。");
            Require(copy.SelectionStart == 2 && copy.SelectionLength == 4 && copy.CaretOffset == 6, "カーソル情報が一致しません。");
            if (mode == "claim")
            {
                Console.WriteLine("READY");
                await Task.Delay(Timeout.Infinite);
            }
            else store.Retire(copy.DocumentId);
            break;
        default: throw new ArgumentException("Unknown worker mode");
    }
    return;
}

if (args.Length != 0) throw new ArgumentException("引数なしで実行してください。");
// Only this newly created test directory is used. Never touch the user's application data.
var root = Path.Combine(Path.GetTempPath(), "SYUAS.RecoveryProbe", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Console.WriteLine($"検証フォルダー: {root}");
try
{
    var originalPath = Path.Combine(root, "original.adoc");
    File.WriteAllText(originalPath, originalText);
    using (var writer = Worker.Start("write", root))
    {
        await writer.ReadyAsync();
        using var observer = Worker.Start("empty", root);
        await observer.CompleteAsync();
        Console.WriteLine("PASS: 他プロセスの使用中コピーを除外");
        await writer.KillAsync();
    }
    File.WriteAllText(originalPath, "external version\n");
    using (var claimant = Worker.Start("claim", root))
    {
        await claimant.ReadyAsync();
        using var observer = Worker.Start("empty", root);
        await observer.CompleteAsync();
        Console.WriteLine("PASS: 強制終了後に本文・保存基準・カーソルを復元し、所有権を移管");
        await claimant.KillAsync();
    }
    using (var recovery = Worker.Start("retire", root)) await recovery.CompleteAsync();
    using (var observer = Worker.Start("empty", root)) await observer.CompleteAsync();
    Require(File.ReadAllText(originalPath) == "external version\n", "復元処理が元ファイルを書き換えました。");
    Console.WriteLine("PASS: 復元直後の強制終了から再復元し、整理後は候補なし。外部版も維持");

    File.WriteAllText(originalPath, originalText);
    using (var writer = Worker.Start("pump", root))
    {
        await writer.ReadyAsync();
        await Task.Delay(100);
        await writer.KillAsync();
    }
    using (var recovery = Worker.Start("retire", root)) await recovery.CompleteAsync();
    using (var observer = Worker.Start("empty", root)) await observer.CompleteAsync();
    Console.WriteLine("PASS: 連続退避中の強制終了でも有効な世代から復元");
    Directory.Delete(root, recursive: true);
    Console.WriteLine("すべてのプロセス検証に成功しました。");
}
catch
{
    Console.Error.WriteLine($"検証失敗。調査用データを保持しました: {root}");
    throw;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

// Owns only processes started by this probe. Timeouts cannot leave a writer running.
sealed class Worker : IDisposable
{
    private readonly Process process;
    private readonly Task<string> errors;
    private Worker(Process process)
    {
        this.process = process;
        errors = process.StandardError.ReadToEndAsync();
    }

    public static Worker Start(string mode, string root)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { Assembly.GetExecutingAssembly().Location, "--worker", mode, root }) start.ArgumentList.Add(argument);
        return new(Process.Start(start) ?? throw new InvalidOperationException("検証プロセスを開始できません。"));
    }

    public async Task ReadyAsync()
    {
        var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
        if (line != "READY") throw new InvalidOperationException($"検証プロセスの準備に失敗: {line}\n{await errors.WaitAsync(TimeSpan.FromSeconds(5))}");
    }

    public async Task CompleteAsync()
    {
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        if (process.ExitCode != 0) throw new InvalidOperationException(await errors);
    }

    public async Task KillAsync()
    {
        if (process.HasExited) throw new InvalidOperationException($"強制終了前にプロセスが終了しました: {await errors}");
        process.Kill();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
    }

    public void Dispose()
    {
        if (!process.HasExited)
        {
            process.Kill();
            process.WaitForExit(15000);
        }
        process.Dispose();
    }
}
