using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using OnShow.App.Views;
using OnShow.Core;

namespace OnShow.App.Services;

public sealed record UpdateProgress(int Percent, string Status);

/// <summary>
/// 更新落地流程:下载 zip → SHA256 校验 → 解压出新 OnShow.exe →
/// 以 --apply-update 参数启动新 exe 完成自替换(见 ApplyUpdateRunner)。
/// </summary>
internal static class UpdateService
{
    static string TempDir => Path.Combine(Path.GetTempPath(), "OnShow.Update");

    public static async Task<string> DownloadAndPrepareAsync(
        GitHubReleaseInfo release, IProgress<UpdateProgress> progress, CancellationToken ct)
    {
        if (release.ZipAssetUrl is null || release.ZipAssetName is null)
            throw new InvalidOperationException("该版本未提供 Windows 安装包,请到 GitHub Releases 手动下载。");

        Directory.CreateDirectory(TempDir);
        var zipPath = Path.Combine(TempDir, release.ZipAssetName);

        progress.Report(new UpdateProgress(0, "准备下载…"));
        await DownloadFileAsync(release.ZipAssetUrl, zipPath, release.ZipSize, progress, ct);

        progress.Report(new UpdateProgress(100, "校验完整性…"));
        if (release.ChecksumsUrl is not null)
        {
            var checksumText = await UpdateCheck.Http.GetStringAsync(release.ChecksumsUrl, ct);
            var expected = ChecksumFile.FindFor(checksumText, release.ZipAssetName);
            if (expected is not null)
            {
                var actual = await ComputeSha256Async(zipPath, ct);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("安装包 SHA256 校验失败,已中止更新(下载可能不完整)。请重试或到 GitHub Releases 手动下载。");
            }
        }

        progress.Report(new UpdateProgress(100, "解压新版本…"));
        var extractDir = Path.Combine(TempDir, "new");
        Directory.CreateDirectory(extractDir);
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var entry = zip.GetEntry("OnShow.exe")
                        ?? throw new InvalidDataException("安装包中未找到 OnShow.exe,安装包可能已损坏。");
            var newExe = Path.Combine(extractDir, "OnShow.exe");
            entry.ExtractToFile(newExe, overwrite: true);
        }

        try { File.Delete(zipPath); } catch { /* 尽力而为 */ }
        return Path.Combine(extractDir, "OnShow.exe");
    }

    /// <summary>启动新版本 exe 执行自替换(替换目标 = 当前正在运行的 exe 路径)。</summary>
    public static void LaunchApply(string newExePath)
    {
        var target = Environment.ProcessPath
                     ?? throw new InvalidOperationException("无法确定当前程序路径,无法自动更新。");
        var psi = new ProcessStartInfo(newExePath) { UseShellExecute = false };
        psi.ArgumentList.Add("--apply-update");
        psi.ArgumentList.Add(target);
        Process.Start(psi);
    }

    static async Task DownloadFileAsync(string url, string path, long knownSize,
        IProgress<UpdateProgress> progress, CancellationToken ct)
    {
        using var resp = await UpdateCheck.Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? knownSize;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(path);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0)
            {
                var mb = 1048576.0;
                progress.Report(new UpdateProgress(
                    (int)Math.Min(100, read * 100 / total),
                    $"下载中 {read / mb:F1} / {total / mb:F1} MB"));
            }
        }
    }

    static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// 更新安装器:新版本 exe 以 --apply-update &lt;目标路径&gt; 启动后进入本流程
/// (在 App.OnStartup 中早于单实例互斥锁处理)。
/// 等待旧进程退出释放文件锁 → 覆盖 → 重启新版本。
/// </summary>
internal static class ApplyUpdateRunner
{
    public static void Run(string targetExe)
    {
        var newExe = Environment.ProcessPath!;
        Log.Info($"更新安装:等待旧进程退出后覆盖 {targetExe}");

        var replaced = false;
        for (var i = 0; i < 120; i++)
        {
            try
            {
                File.Copy(newExe, targetExe, overwrite: true);
                replaced = true;
                break;
            }
            catch (Exception) when (i < 119)
            {
                Thread.Sleep(500);   // 旧进程尚未完全退出,或杀软短暂占用:重试
            }
        }

        if (!replaced)
        {
            Log.Error("更新安装失败:主程序文件长期被占用");
            Dialog.Warn(null, "OnShow 更新", "替换主程序失败(文件被占用,可能被杀毒软件扫描)。本次更新已取消,程序可正常使用,请稍后重新检查更新。");
            return;
        }

        Log.Info("更新安装完成,重启应用");
        try
        {
            Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("更新后自动重启失败", ex);
            Dialog.Warn(null, "OnShow 更新", "新版本已安装,但自动启动失败,请手动运行 OnShow.exe。");
        }
    }
}
