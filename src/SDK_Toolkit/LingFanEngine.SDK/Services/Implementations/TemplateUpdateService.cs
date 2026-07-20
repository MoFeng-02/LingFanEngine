using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Constants;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 模板更新服务实现（从 GitHub Release 拉取模板 zip 并做版本管理）。
/// <para>HTTP 通过 IHttpClientFactory 命名客户端（engine-update，与引擎更新共用），由工厂管理底层 handler 池，避免套接字耗尽。</para>
/// <para>流程：GET manifest → 版本比对 → 下载模板 zip → sha256 校验 → 解压到模板缓存 current/ → 写 template.lock.json。</para>
/// <para>模板缓存仅作为「覆盖内置嵌入模板」的源：分发模式下若缓存版本高于内置，则建项目用缓存；否则用内置嵌入 zip。</para>
/// </summary>
public class TemplateUpdateService : ITemplateUpdateService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public TemplateUpdateService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc/>
    public string CurrentTemplateVersion
    {
        get
        {
            var lockFile = ReadTemplateLock();
            if (lockFile != null && !string.IsNullOrWhiteSpace(lockFile.TemplateVersion)
                && lockFile.TemplateVersion != "0.0.0")
            {
                return lockFile.TemplateVersion;
            }
            return TemplateDefaults.BuiltinVersion;
        }
    }

    /// <inheritdoc/>
    public async Task<TemplateUpdateManifest?> CheckForTemplateUpdatesAsync(CancellationToken ct = default)
    {
        try
        {
            var url = ResolveManifestUrl();
            var client = CreateClient();
            using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var manifest = await System.Text.Json.JsonSerializer.DeserializeAsync(
                stream, SdkJsonContext.Default.TemplateUpdateManifest, ct);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
                return null;

            // 全局版本比对：远端 > 本地当前版本（含内置基线）即视为有更新（绝不降级）
            if (!IsNewer(manifest.Version, CurrentTemplateVersion))
                return null;

            return manifest;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TemplateUpdateService] CheckForTemplateUpdatesAsync 失败: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<TemplateUpdateResult> UpdateTemplateAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        void Log(string msg) => progress?.Report(msg);

        try
        {
            var manifest = await CheckForTemplateUpdatesAsync(ct);
            if (manifest == null)
            {
                Log("模板已是最新。");
                return TemplateUpdateResult.UpToDate();
            }

            // minSdkVersion 校验：本地 SDK 版本低于要求则拒绝（提示升级 SDK）
            if (!string.IsNullOrWhiteSpace(manifest.MinSdkVersion) && !IsSdkVersionAtLeast(manifest.MinSdkVersion))
            {
                var msg = $"模板要求 SDK ≥ {manifest.MinSdkVersion}，当前 SDK 版本过低，请先升级 SDK。";
                Log(msg);
                return TemplateUpdateResult.Failed(msg);
            }

            Log($"发现模板新版本 {manifest.Version}，开始下载...");
            var cacheRoot = PathHelper.GetTemplateCacheDirectory();
            PathHelper.EnsureDirectory(cacheRoot);
            var currentDir = Path.Combine(cacheRoot, TemplateDefaults.CurrentDirName);
            var updatesDir = PathHelper.GetEngineUpdatesDirectory(); // 复用引擎更新工作目录存放临时 zip
            PathHelper.EnsureDirectory(updatesDir);
            var zipPath = Path.Combine(updatesDir, TemplateDefaults.TemplateZipFileName);
            var extractedDir = Path.Combine(updatesDir, TemplateDefaults.ExtractedDirName);

            if (Directory.Exists(extractedDir)) Directory.Delete(extractedDir, recursive: true);
            PathHelper.EnsureDirectory(extractedDir);

            // 1. 下载
            var client = CreateClient();
            using (var resp = await client.GetAsync(manifest.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(zipPath);
                await src.CopyToAsync(dst, ct);
            }
            Log("  下载完成。");

            // 2. 整包 sha256 校验（防篡改）
            if (!string.IsNullOrWhiteSpace(manifest.AssetSha256))
            {
                if (!VerifySha256(zipPath, manifest.AssetSha256))
                {
                    File.Delete(zipPath);
                    throw new InvalidDataException("模板 zip sha256 校验失败，可能被篡改。");
                }
                Log("  整包 sha256 校验通过。");
            }

            // 3. 解压（排除 bin/obj/.vs/.git 等构建产物与残留）
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    var fullName = entry.FullName.Replace('\\', '/');
                    if (fullName.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                        fullName.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                        fullName.Contains("/.vs/", StringComparison.OrdinalIgnoreCase) ||
                        fullName.Contains("/.git/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var destPath = Path.Combine(extractedDir, entry.FullName);
                    var dir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(dir))
                        PathHelper.EnsureDirectory(dir);
                    if (string.IsNullOrEmpty(entry.Name))
                        continue; // 目录条目已由 EnsureDirectory 创建
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }
            Log("  解压完成。");

            // 4. 整体覆盖 current/（先清后拷，保持幂等）
            if (Directory.Exists(currentDir))
                Directory.Delete(currentDir, recursive: true);
            PathHelper.EnsureDirectory(currentDir);
            CopyDirectory(extractedDir, currentDir);

            // 5. 写 lock（来源标记为 download，版本取清单版本）
            WriteTemplateLock(new TemplateLockFile
            {
                TemplateVersion = manifest.Version,
                Source = "download",
                LastCheckedUtc = DateTime.UtcNow.ToString("o"),
            });

            // 清理临时文件
            try { File.Delete(zipPath); } catch { /* 忽略 */ }
            try { Directory.Delete(extractedDir, recursive: true); } catch { /* 忽略 */ }

            Log($"模板已更新到 {manifest.Version}。");
            return new TemplateUpdateResult { Status = TemplateUpdateStatus.UpdateApplied, ManifestVersion = manifest.Version };
        }
        catch (Exception ex)
        {
            var msg = $"模板更新失败：{ex.Message}";
            Log(msg);
            return TemplateUpdateResult.Failed(msg);
        }
    }

    /// <inheritdoc/>
    public string? GetCachedTemplateDir()
    {
        var cacheRoot = PathHelper.GetTemplateCacheDirectory();
        var currentDir = Path.Combine(cacheRoot, TemplateDefaults.CurrentDirName);
        if (!Directory.Exists(currentDir) || !Directory.GetFileSystemEntries(currentDir).Any())
            return null;

        var lockFile = ReadTemplateLock();
        // 仅当缓存版本高于内置时才优先使用（内置嵌入模板可能随 SDK 升级而更新，不应被旧下载覆盖）
        if (lockFile != null && IsNewer(lockFile.TemplateVersion, TemplateDefaults.BuiltinVersion))
            return currentDir;
        return null;
    }

    // ===== 内部辅助 =====

    private HttpClient CreateClient()
    {
        // 命名客户端由 IHttpClientFactory 管理底层 handler 池，避免套接字耗尽
        return _httpClientFactory.CreateClient(TemplateDefaults.HttpClientName);
    }

    private string ResolveManifestUrl()
    {
        var settings = TryLoadSdkSettings();
        var url = settings?.TemplateUpdateManifestUrl;
        if (string.IsNullOrWhiteSpace(url))
            url = TemplateDefaults.DefaultManifestUrl;

        // 67.6 安全：仅允许白名单主机拉取 manifest，防止恶意 manifest 投毒
        if (!IsAllowedManifestHost(url))
            throw new InvalidOperationException($"模板 manifest 地址主机不在白名单内，已拒绝：{url}");

        return url;
    }

    private static bool IsAllowedManifestHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        var host = uri.Host;
        return EngineUpdateDefaults.AllowedManifestHosts.Any(allowed =>
            string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase));
    }

    private static SdkSettings? TryLoadSdkSettings()
    {
        try
        {
            var path = PathHelper.GetSdkSettingsFilePath();
            if (!File.Exists(path))
                return null;
            var json = File.ReadAllText(path);
            return JsonHelper.Deserialize(json, SdkJsonContext.Default.SdkSettings);
        }
        catch
        {
            return null;
        }
    }

    private static void CopyDirectory(string srcDir, string destDir)
    {
        PathHelper.EnsureDirectory(destDir);
        foreach (var file in Directory.GetFiles(srcDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var sub in Directory.GetDirectories(srcDir))
        {
            CopyDirectory(sub, Path.Combine(destDir, Path.GetFileName(sub)));
        }
    }

    private static bool VerifySha256(string filePath, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return true;
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(actual, expected.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }

    /// <summary>本地 SDK 程序集版本是否 ≥ 指定最低版本（语义化版本 X.Y.Z）。</summary>
    private static bool IsSdkVersionAtLeast(string minVersion)
    {
        var sdkVer = typeof(TemplateUpdateService).Assembly.GetName().Version;
        if (sdkVer == null)
            return false;
        return Version.TryParse(minVersion, out var min) && sdkVer >= min;
    }

    /// <summary>返回 a 是否比 b 新（语义化版本 X.Y.Z）。</summary>
    private static bool IsNewer(string a, string b)
    {
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
            return va > vb;
        return !string.Equals(a, b, StringComparison.Ordinal) &&
               string.Compare(a, b, StringComparison.Ordinal) > 0;
    }

    private TemplateLockFile? ReadTemplateLock()
    {
        var path = Path.Combine(PathHelper.GetTemplateCacheDirectory(), TemplateDefaults.TemplateLockFileName);
        if (!File.Exists(path))
            return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonHelper.Deserialize(json, SdkJsonContext.Default.TemplateLockFile);
        }
        catch
        {
            return null;
        }
    }

    private void WriteTemplateLock(TemplateLockFile lockFile)
    {
        var dir = PathHelper.GetTemplateCacheDirectory();
        PathHelper.EnsureDirectory(dir);
        var path = Path.Combine(dir, TemplateDefaults.TemplateLockFileName);
        var json = JsonHelper.Serialize(lockFile, SdkJsonContext.Default.TemplateLockFile);
        File.WriteAllText(path, json);
    }
}
