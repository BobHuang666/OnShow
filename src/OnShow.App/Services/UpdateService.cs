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
/// 更新落地流程:下载 zip(分段并行 + 可选镜像加速,失败回退直连)→ SHA256 校验 →
/// 解压出新 OnShow.exe → 以 --apply-update 参数启动新 exe 完成自替换(见 ApplyUpdateRunner)。
/// 安全设计:checksums.txt 永远从 GitHub 官方地址下载(镜像只加速 zip),完整性由哈希校验兜底。
/// </summary>
internal static class UpdateService
{
    const int SegmentCount = 6;
    static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(20);
    static readonly long SegmentedMinSize = 8 * 1024 * 1024;

    static string TempDir => Path.Combine(Path.GetTempPath(), "OnShow.Update");

    public static async Task<string> DownloadAndPrepareAsync(
        GitHubReleaseInfo release, IProgress<UpdateProgress> progress, CancellationToken ct, string? mirrorPrefix)
    {
        if (release.ZipAssetUrl is null || release.ZipAssetName is null)
            throw new InvalidOperationException("该版本未提供 Windows 安装包,请到 GitHub Releases 手动下载。");

        Directory.CreateDirectory(TempDir);
        var zipPath = Path.Combine(TempDir, release.ZipAssetName);

        progress.Report(new UpdateProgress(0, "准备下载…"));
        await DownloadAssetAsync(release.ZipAssetUrl, zipPath, release.ZipSize, progress, ct, mirrorPrefix);

        progress.Report(new UpdateProgress(100, "校验完整性…"));
        var expected = await TryGetExpectedHashAsync(release, ct)
                       ?? throw new InvalidDataException(
                           "无法从 GitHub 官方获取校验信息(checksums 与 API 均不可达)。为安全起见已中止安装,请稍后重试或到 GitHub Releases 手动下载。");

        var actual = await ComputeSha256Async(zipPath, ct);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装包 SHA256 校验失败,已中止更新(下载可能不完整)。请重试或到 GitHub Releases 手动下载。");

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

    /// <summary>
    /// 取得官方信任锚点的期望哈希:优先官方 checksums.txt(重试 3 次);
    /// 不可用时回退到 GitHub API 的 asset digest(检查更新时已随 API 返回,同为官方来源)。
    /// 镜像只加速 zip 下载,永远不作为校验锚点。
    /// </summary>
    static async Task<string?> TryGetExpectedHashAsync(GitHubReleaseInfo release, CancellationToken ct)
    {
        if (release.ChecksumsUrl is not null)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var text = await UpdateCheck.Http.GetStringAsync(release.ChecksumsUrl, ct);
                    var hash = ChecksumFile.FindFor(text, release.ZipAssetName!);
                    if (hash is not null)
                    {
                        Log.Info("校验锚点:官方 checksums.txt");
                        return hash;
                    }
                    break;   // checksums 存在但无对应行,重试无意义
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) when (attempt < 3)
                {
                    Log.Warn($"获取 checksums.txt 失败(第 {attempt} 次):{ex.Message}");
                    await Task.Delay(2000, ct);
                }
                catch (Exception ex)
                {
                    Log.Warn($"获取 checksums.txt 失败(已重试):{ex.Message}");
                    break;
                }
            }
        }

        if (release.ZipDigestHex is { } digest)
        {
            Log.Info("校验锚点:GitHub API asset digest");
            return digest;
        }
        return null;
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

    // ---------- 下载 ----------

    static string ApplyMirror(string prefix, string url) =>
        prefix.TrimEnd().TrimEnd('/') + "/" + url;

    /// <summary>镜像优先、官方兜底;任一源失败自动切换。</summary>
    static async Task DownloadAssetAsync(string url, string path, long totalSize,
        IProgress<UpdateProgress> progress, CancellationToken ct, string? mirrorPrefix)
    {
        var attempts = new List<string>();
        if (!string.IsNullOrWhiteSpace(mirrorPrefix)) attempts.Add(ApplyMirror(mirrorPrefix, url));
        attempts.Add(url);

        Exception? last = null;
        foreach (var attempt in attempts)
        {
            try
            {
                await DownloadCoreAsync(attempt, path, totalSize, progress, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warn($"下载源失败 {attempt}:{ex.Message}");
            }
        }
        throw new InvalidDataException(
            $"下载失败({last?.Message})。请检查网络后重试,或在设置中更换下载加速镜像,也可到 GitHub Releases 页手动下载。");
    }

    static async Task DownloadCoreAsync(string url, string path, long totalSize,
        IProgress<UpdateProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // 探测真实大小(Range 0-0 不拉正文,只取头)
        long total = totalSize;
        using (var probe = await UpdateCheck.Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            probe.EnsureSuccessStatusCode();
            total = probe.Content.Headers.ContentLength ?? totalSize;
        }

        if (total > SegmentedMinSize)
        {
            try
            {
                if (await TrySegmentedAsync(url, path, total, progress, ct)) return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Log.Warn("分段下载失败,回退单流:" + ex.Message);
            }
        }
        await SingleStreamAsync(url, path, total, progress, ct);
    }

    /// <summary>多段并行下载(Range 请求),任一段非 206 或停滞则失败,由上层回退。</summary>
    static async Task<bool> TrySegmentedAsync(string url, string path, long total,
        IProgress<UpdateProgress> progress, CancellationToken ct)
    {
        var segSize = total / SegmentCount;
        if (segSize < 1024 * 1024) return false;

        var parts = Enumerable.Range(0, SegmentCount).Select(i => $"{path}.part{i}").ToArray();
        try
        {
            long done = 0;
            var sync = new object();
            var sw = Stopwatch.StartNew();
            var lastReport = DateTime.MinValue;

            async Task RunSegment(int i)
            {
                var start = (long)i * segSize;
                var end = i == SegmentCount - 1 ? total - 1 : start + segSize - 1;

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);
                using var resp = await UpdateCheck.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)resp.StatusCode != 206)
                    throw new IOException($"分段请求返回 {(int)resp.StatusCode},服务端不支持分段");

                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(parts[i], FileMode.Create, FileAccess.Write,
                    FileShare.None, 81920, useAsync: true);

                var buf = new byte[1 << 16];
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(StallTimeout);
                int n;
                while ((n = await src.ReadAsync(buf, stall.Token)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    stall.CancelAfter(StallTimeout);
                    lock (sync)
                    {
                        done += n;
                        if ((DateTime.UtcNow - lastReport).TotalMilliseconds >= 400)
                        {
                            lastReport = DateTime.UtcNow;
                            var speed = done / Math.Max(1, sw.Elapsed.TotalSeconds);
                            progress.Report(new UpdateProgress(
                                (int)Math.Min(99, done * 100 / total),
                                $"下载中 {done / 1048576.0:F1} / {total / 1048576.0:F1} MB({speed / 1048576.0:F1} MB/s)"));
                        }
                    }
                }
            }

            await Task.WhenAll(Enumerable.Range(0, SegmentCount).Select(RunSegment));

            await using (var merged = File.Create(path))
            {
                foreach (var part in parts)
                    await using (var src = File.OpenRead(part))
                        await src.CopyToAsync(merged, ct);
            }
            return true;
        }
        finally
        {
            foreach (var part in parts) { try { File.Delete(part); } catch { /* 尽力而为 */ } }
        }
    }

    /// <summary>单流下载(小文件 / 服务端不支持分段时的兜底),带停滞超时。</summary>
    static async Task SingleStreamAsync(string url, string path, long total,
        IProgress<UpdateProgress> progress, CancellationToken ct)
    {
        using var resp = await UpdateCheck.Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        total = resp.Content.Headers.ContentLength ?? total;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(path);
        var buf = new byte[1 << 16];
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(StallTimeout);

        long read = 0;
        var sw = Stopwatch.StartNew();
        var lastReport = DateTime.MinValue;
        int n;
        while ((n = await src.ReadAsync(buf, stall.Token)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            stall.CancelAfter(StallTimeout);
            read += n;
            if ((DateTime.UtcNow - lastReport).TotalMilliseconds >= 400)
            {
                lastReport = DateTime.UtcNow;
                var speed = read / Math.Max(1, sw.Elapsed.TotalSeconds);
                progress.Report(new UpdateProgress(
                    total > 0 ? (int)Math.Min(99, read * 100 / total) : 0,
                    $"下载中 {read / 1048576.0:F1} / {total / 1048576.0:F1} MB({speed / 1048576.0:F1} MB/s)"));
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
