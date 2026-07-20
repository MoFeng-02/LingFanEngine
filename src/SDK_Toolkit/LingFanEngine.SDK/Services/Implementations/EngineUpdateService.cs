using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Constants;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 引擎 DLL 独立更新服务实现。
/// <para>HTTP 通过 IHttpClientFactory 创建命名客户端（engine-update），由工厂管理 HttpClientHandler 池，
/// 避免 DNS 变更/端口耗尽导致的套接字耗尽（socket exhaustion）。</para>
/// <para>流程：GET manifest → 版本比对 → 下载 asset zip → 逐 DLL sha256 校验 → 应用（热替换/pending）。</para>
/// </summary>
public class EngineUpdateService : IEngineUpdateService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public EngineUpdateService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc/>
    public string CurrentEngineVersion
    {
        get
        {
            // 优先读引擎缓存 lock 的 engineVersion（缓存由安装包自带 / 联网更新填充）
            var cacheLock = ReadCacheLock();
            if (cacheLock != null && !string.IsNullOrWhiteSpace(cacheLock.EngineVersion)
                && cacheLock.EngineVersion != "0.0.0")
            {
                return cacheLock.EngineVersion;
            }

            // 缓存为空时回退：SDK 自身 DLL/ 内 LingFanEngine.dll 元数据
            var bundled = GetSingleDllVersion(
                Path.Combine(AppContext.BaseDirectory, ProjectConstants.DllDir, ProjectConstants.EngineCoreDll));
            if (bundled != "0.0.0")
                return bundled;

            // 再回退到设置文件
            var settings = TryLoadSdkSettings();
            if (!string.IsNullOrWhiteSpace(settings?.EngineVersion))
                return settings!.EngineVersion;
            return "0.0.0";
        }
    }

    /// <inheritdoc/>
    public string GetProjectEngineVersion(string projectRootDir)
    {
        // 版本真相优先读 engine.lock.json（声明式）；缺失回落 DLL 元数据（兼容旧项目迁移）
        var lockFile = ReadProjectLock(projectRootDir);
        if (lockFile != null && !string.IsNullOrWhiteSpace(lockFile.EngineVersion)
            && lockFile.EngineVersion != "0.0.0")
        {
            return lockFile.EngineVersion;
        }

        var dllDir = Path.Combine(projectRootDir, ProjectConstants.DllDir);
        var ver = GetSingleDllVersion(Path.Combine(dllDir, ProjectConstants.EngineCoreDll));
        return ver == "0.0.0" ? string.Empty : ver;
    }

    /// <summary>
    /// 读取指定目录内「全部 4 个引擎 DLL」的 AssemblyName 版本（X.Y.Z），返回 DLL 名 → 版本 的表。
    /// <para>用于逐 DLL 版本隔离：任一 DLL 版本落后远端即触发更新，覆盖「只更新了某个依赖」的情形。</para>
    /// </summary>
    private static Dictionary<string, string> GetDllVersions(string dllDirectory)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dllName in ProjectConstants.SdkDistributedDlls)
        {
            map[dllName] = GetSingleDllVersion(Path.Combine(dllDirectory, dllName));
        }
        return map;
    }

    /// <summary>
    /// 读取单个 DLL 的 AssemblyName 版本（X.Y.Z）；
    /// 文件不存在/损坏/被锁定时返回 "0.0.0"（视为全新，应被任何远端版本判定为「有更新」）。
    /// </summary>
    private static string GetSingleDllVersion(string dllPath)
    {
        try
        {
            if (File.Exists(dllPath))
            {
                var ver = AssemblyName.GetAssemblyName(dllPath).Version;
                if (ver != null)
                    return ver.ToString(3); // X.Y.Z
            }
        }
        catch
        {
            // 损坏或锁定，回退 0.0.0
        }
        return "0.0.0";
    }

    /// <inheritdoc/>
    public async Task<EngineUpdateManifest?> CheckForUpdatesAsync(Dictionary<string, string>? currentVersions = null, CancellationToken ct = default)
    {
        try
        {
            var url = ResolveManifestUrl();
            var client = CreateClient();
            using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var manifest = await System.Text.Json.JsonSerializer.DeserializeAsync(
                stream, SdkJsonContext.Default.EngineUpdateManifest, ct);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
                return null;

            // 逐 DLL 版本比对：清单中任一 DLL 版本 > 本地对应 DLL 版本即视为有更新（绝不降级）
            var baseline = currentVersions ?? GetDllVersions(
                Path.Combine(AppContext.BaseDirectory, ProjectConstants.DllDir));
            if (!IsUpdateNeeded(manifest, baseline))
                return null;

            return manifest;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EngineUpdateService] CheckForUpdatesAsync 失败: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<EngineUpdateResult> UpdateProjectAsync(
        string projectRootDir, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        void Log(string msg) => progress?.Report(msg);

        try
        {
            // 以「项目自身」DLL/ 内 4 个 DLL 的版本表为基线逐 DLL 比对：项目某 DLL 落后远端则更新；
            // 已是最新或比远端新则不降级、不重复覆盖
            var projectDllDir = Path.Combine(projectRootDir, ProjectConstants.DllDir);

            // D1: 项目锁定版本时跳过更新（用户主动固定）
            var existingLock = ReadProjectLock(projectRootDir);
            if (existingLock?.Pinned == true)
            {
                Log("项目已锁定引擎版本，跳过更新。如需更新请先解除锁定。");
                return EngineUpdateResult.UpToDate();
            }

            var projectVersions = GetDllVersions(projectDllDir);

            var manifest = await CheckForUpdatesAsync(projectVersions, ct);
            if (manifest == null)
            {
                Log("已是最新版本，无需更新。");
                return EngineUpdateResult.UpToDate();
            }

            if (manifest.BreakingAbstractions)
            {
                const string msg = "远端包含破坏性 Abstractions 变更，无法热更——请升级 SDK 整体。";
                Log(msg);
                return EngineUpdateResult.Failed(msg);
            }

            // 最低 SDK 版本校验（v2）：本地 SDK 版本低于要求则拒绝热更，提示升级 SDK
            if (!string.IsNullOrWhiteSpace(manifest.MinSdkVersion) && !IsSdkVersionAtLeast(manifest.MinSdkVersion))
            {
                var msg = $"此引擎更新需要 SDK ≥ {manifest.MinSdkVersion}，当前 SDK 过低，请先升级 SDK。";
                Log(msg);
                return EngineUpdateResult.Failed(msg);
            }

            Log($"发现新版本 {manifest.Version}，开始下载...");

            // 下载 + 校验 + 解压
            var staged = await DownloadAndStageAsync(manifest, Log, ct);

            // 用户项目 DLL/：4 个 DLL 全部可热替换（不被 SDK 进程加载）
            PathHelper.EnsureDirectory(projectDllDir);

            KillRunningProcesses(projectRootDir);

            // 版本基线：优先用项目 lock 中记录的逐 DLL 版本；缺失则取磁盘实际版本
            var baselineVersions = existingLock?.DllVersions != null
                ? new Dictionary<string, string>(existingLock.DllVersions, StringComparer.OrdinalIgnoreCase)
                : GetDllVersions(projectDllDir);

            var updated = new List<string>();
            foreach (var dllName in ProjectConstants.SdkDistributedDlls)
            {
                if (!staged.TryGetValue(dllName, out var stagedPath))
                {
                    Log($"  警告: 暂存区缺少 {dllName}，跳过。");
                    continue;
                }

                // 逐 DLL 破坏性变更：此类 DLL 不可热更，需升级 SDK 整体，跳过并更新记录
                if (manifest.IsDllBreaking(dllName))
                {
                    Log($"  跳过（破坏性变更，需升级 SDK）: {dllName}");
                    continue;
                }

                // 仅覆盖版本真正变化的 DLL（67.3 逐 DLL 粒度）：本地已最新则不重复覆盖
                var remoteVer = manifest.GetDllVersion(dllName);
                baselineVersions.TryGetValue(dllName, out var localVer);
                localVer ??= "0.0.0";
                if (!IsNewer(remoteVer, localVer))
                {
                    Log($"  跳过（已最新）: {dllName}");
                    continue;
                }

                var dest = Path.Combine(projectDllDir, dllName);
                File.Copy(stagedPath, dest, overwrite: true);
                updated.Add(dllName);
                Log($"  已更新: {dllName}（{localVer} → {remoteVer}）");
            }

            Log($"项目引擎 DLL 更新完成：{updated.Count} 个。");

            // 回写 engine.lock.json（版本真相持久化，逐 DLL 版本 + 检查时间）
            var lockFile = existingLock ?? new EngineLockFile();
            lockFile.EngineVersion = manifest.Version;
            lockFile.DllVersions = manifest.GetDllVersions();
            lockFile.LastCheckedUtc = DateTime.UtcNow.ToString("o");
            // lockFile.Pinned 保留（更新不改变锁定状态）
            WriteProjectLock(projectRootDir, lockFile);

            return new EngineUpdateResult
            {
                Status = EngineUpdateStatus.UpdateApplied,
                ManifestVersion = manifest.Version,
                UpdatedDlls = updated,
            };
        }
        catch (Exception ex)
        {
            Log($"更新失败: {ex.Message}");
            return EngineUpdateResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<EngineUpdateResult> UpdateSdkCacheAsync(
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        void Log(string msg) => progress?.Report(msg);

        try
        {
            var manifest = await CheckForUpdatesAsync(
                GetDllVersions(Path.Combine(AppContext.BaseDirectory, ProjectConstants.DllDir)), ct);
            if (manifest == null)
            {
                Log("已是最新版本，无需更新。");
                return EngineUpdateResult.UpToDate();
            }

            if (manifest.BreakingAbstractions)
            {
                const string msg = "远端包含破坏性 Abstractions 变更，无法热更——请升级 SDK 整体。";
                Log(msg);
                return EngineUpdateResult.Failed(msg);
            }

            // 最低 SDK 版本校验（v2）：本地 SDK 版本低于要求则拒绝热更，提示升级 SDK
            if (!string.IsNullOrWhiteSpace(manifest.MinSdkVersion) && !IsSdkVersionAtLeast(manifest.MinSdkVersion))
            {
                var msg = $"此引擎更新需要 SDK ≥ {manifest.MinSdkVersion}，当前 SDK 过低，请先升级 SDK。";
                Log(msg);
                return EngineUpdateResult.Failed(msg);
            }

            Log($"发现新版本 {manifest.Version}，开始下载...");

            var staged = await DownloadAndStageAsync(manifest, Log, ct);

            var sdkDllDir = Path.Combine(AppContext.BaseDirectory, ProjectConstants.DllDir);
            PathHelper.EnsureDirectory(sdkDllDir);

            var updated = new List<string>();
            var pending = new List<string>();
            var pendingEntries = new List<PendingUpdateEntry>();

            foreach (var dllName in ProjectConstants.SdkDistributedDlls)
            {
                if (!staged.TryGetValue(dllName, out var stagedPath))
                {
                    Log($"  警告: 暂存区缺少 {dllName}，跳过。");
                    continue;
                }

                // 逐 DLL 破坏性变更：不可热更，需升级 SDK 整体，跳过
                if (manifest.IsDllBreaking(dllName))
                {
                    Log($"  跳过（破坏性变更，需升级 SDK）: {dllName}");
                    continue;
                }

                // 仅覆盖版本真正变化的 DLL（与 67.3 UpdateProjectAsync 一致）：本地已最新则不重复覆盖
                var remoteVer = manifest.GetDllVersion(dllName);
                var localVer = GetSingleDllVersion(Path.Combine(sdkDllDir, dllName));
                if (!IsNewer(remoteVer, localVer))
                {
                    Log($"  跳过（已最新）: {dllName}");
                    continue;
                }

                var dest = Path.Combine(sdkDllDir, dllName);

                // 包内 DLL/ 是「独立于 SDK 运行时、仅供用户项目再分发」的载荷：SDK 通过 ProjectReference
                // 编译期依赖引擎源码（3 个被烤进 exe），运行时不加载这些松散 DLL 文件，故可直接覆盖。
                // 仅当被外部进程（如杀软实时扫描/文件管理器预览）偶发锁定时，TryCopyOrPending 才回落
                // pending（防御性，罕见），重启 SDK 后由 ApplyPendingUpdatesAsync 应用。
                TryCopyOrPending(dest, stagedPath, dllName, manifest, updated, pending, pendingEntries, Log);
            }

            // 持久化 pending 清单（若有）
            if (pendingEntries.Count > 0)
            {
                await SavePendingManifestAsync(new PendingUpdateManifest
                {
                    Version = manifest.Version,
                    Entries = pendingEntries,
                }, ct);
            }
            else
            {
                ClearPendingManifest();
            }

            var status = pending.Count > 0
                ? EngineUpdateStatus.PendingRestart
                : EngineUpdateStatus.UpdateApplied;
            Log($"SDK 缓存更新完成：热替换 {updated.Count} 个，pending {pending.Count} 个。");
            SyncCacheFromSdkDlls(); // 同步缓存，使新建项目 / CurrentEngineVersion 跟随 SDK 最新
            return new EngineUpdateResult
            {
                Status = status,
                ManifestVersion = manifest.Version,
                UpdatedDlls = updated,
                PendingDlls = pending,
            };
        }
        catch (Exception ex)
        {
            Log($"更新失败: {ex.Message}");
            return EngineUpdateResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task ApplyPendingUpdatesAsync(CancellationToken ct = default)
    {
        var pending = await LoadPendingManifestAsync(ct);
        if (pending == null || pending.Entries.Count == 0)
            return;

        var remaining = new List<PendingUpdateEntry>();
        foreach (var entry in pending.Entries)
        {
            if (!File.Exists(entry.StagedPath))
                continue; // 暂存文件丢失，丢弃

            // 应用前再校验一次 sha256
            if (!string.IsNullOrEmpty(entry.Sha256) &&
                !VerifySha256(entry.StagedPath, entry.Sha256))
            {
                continue; // 校验失败，丢弃
            }

            try
            {
                File.Copy(entry.StagedPath, entry.TargetPath, overwrite: true);
                Debug.WriteLine($"[EngineUpdateService] pending 应用成功: {entry.DllName}");
            }
            catch (IOException)
            {
                // 仍被锁定，保留 pending 等下次启动
                remaining.Add(entry);
            }
            catch (UnauthorizedAccessException)
            {
                remaining.Add(entry);
            }
        }

        if (remaining.Count == 0)
        {
            ClearPendingManifest();
            Debug.WriteLine("[EngineUpdateService] pending 全部应用完成。");
        }
        else
        {
            await SavePendingManifestAsync(new PendingUpdateManifest
            {
                Version = pending.Version,
                Entries = remaining,
            }, ct);
            Debug.WriteLine($"[EngineUpdateService] pending 仍剩 {remaining.Count} 个待应用（重启后重试）。");
        }

        // pending 应用后 SDK/DLL/ 已是真实最新，同步缓存使离线建项目 / CurrentEngineVersion 对齐
        SyncCacheFromSdkDlls();
    }

    // ===== engine.lock.json（版本真相声明） =====

    /// <inheritdoc/>
    public EngineLockFile? ReadProjectLock(string projectRootDir)
    {
        var path = Path.Combine(projectRootDir, ProjectConstants.EngineLockFileName);
        if (!File.Exists(path))
            return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonHelper.Deserialize(json, SdkJsonContext.Default.EngineLockFile);
        }
        catch
        {
            return null;
        }
    }

    private void WriteProjectLock(string projectRootDir, EngineLockFile lockFile)
    {
        var path = Path.Combine(projectRootDir, ProjectConstants.EngineLockFileName);
        var json = JsonHelper.Serialize(lockFile, SdkJsonContext.Default.EngineLockFile);
        File.WriteAllText(path, json);
    }

    private EngineLockFile? ReadCacheLock()
    {
        var path = Path.Combine(PathHelper.GetEngineCacheDirectory(), ProjectConstants.EngineLockFileName);
        if (!File.Exists(path))
            return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonHelper.Deserialize(json, SdkJsonContext.Default.EngineLockFile);
        }
        catch
        {
            return null;
        }
    }

    private void WriteCacheLock(EngineLockFile lockFile)
    {
        var cacheDir = PathHelper.GetEngineCacheDirectory();
        PathHelper.EnsureDirectory(cacheDir);
        var path = Path.Combine(cacheDir, ProjectConstants.EngineLockFileName);
        var json = JsonHelper.Serialize(lockFile, SdkJsonContext.Default.EngineLockFile);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// 确保引擎缓存齐全：若缓存目录缺任意引擎 DLL，则从 SDK 自身 DLL/ 初始化复制全部 4 个，
    /// 并写缓存 lock。这样离线/首次使用时缓存已是「已下载最新」的 4 个齐全状态（非 3 个降级）。
    /// <para>缓存本身已 4 个齐全时直接返回（幂等）。</para>
    /// </summary>
    public Task EnsureCacheSeededAsync()
    {
        var cacheDir = PathHelper.GetEngineCacheDirectory();
        PathHelper.EnsureDirectory(cacheDir);

        var cacheLock = ReadCacheLock();
        var allPresent = ProjectConstants.SdkDistributedDlls.All(d =>
            File.Exists(Path.Combine(cacheDir, d)));

        if (allPresent && cacheLock != null)
            return Task.CompletedTask; // 已齐全（离线也能用）

        // 从 SDK/DLL/ 初始化缓存（安装包自带全部 4 个 DLL）
        var sdkDllDir = Path.Combine(AppContext.BaseDirectory, ProjectConstants.DllDir);
        if (!Directory.Exists(sdkDllDir))
            throw new InvalidOperationException(
                "引擎缓存为空且 SDK 内置 DLL 缺失，无法播种引擎。请联网更新或重新安装 SDK。");

        foreach (var dll in ProjectConstants.SdkDistributedDlls)
        {
            var src = Path.Combine(sdkDllDir, dll);
            if (!File.Exists(src))
                throw new InvalidOperationException(
                    $"SDK 内置 DLL 缺失: {dll}，无法播种引擎缓存。请重新编译或重装 SDK。");
            File.Copy(src, Path.Combine(cacheDir, dll), overwrite: true);
        }

        // 写缓存 lock（engineVersion + 逐 DLL 版本）
        var lockFile = cacheLock ?? new EngineLockFile();
        var versions = GetDllVersions(cacheDir);
        lockFile.EngineVersion = versions.TryGetValue(ProjectConstants.EngineCoreDll, out var ev) ? ev : "0.0.0";
        lockFile.DllVersions = versions;
        lockFile.LastCheckedUtc = DateTime.UtcNow.ToString("o");
        WriteCacheLock(lockFile);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 将 SDK 自带 DLL/ 的当前 4 个 DLL 同步到引擎缓存（engine-cache）并刷新缓存 lock。
    /// <para>缓存是离线建项目/预览的种子源，必须与 SDK 已知最新的 DLL/ 保持一致：
    /// 用户通过 <see cref="UpdateSdkCacheAsync"/> 更新 SDK 自身 DLL、或重启后
    /// <see cref="ApplyPendingUpdatesAsync"/> 应用 pending 后，都应调用此方法，
    /// 使缓存 lock 反映真实最新版本，避免新建项目 / <see cref="CurrentEngineVersion"/> 落后于 SDK。</para>
    /// <para>若 SDK/DLL/ 目录不存在（极端情况）则不覆盖缓存现状，保持幂等。</para>
    /// </summary>
    private void SyncCacheFromSdkDlls()
    {
        var sdkDllDir = Path.Combine(AppContext.BaseDirectory, ProjectConstants.DllDir);
        if (!Directory.Exists(sdkDllDir))
            return; // 无源可同步，保持缓存现状

        var cacheDir = PathHelper.GetEngineCacheDirectory();
        PathHelper.EnsureDirectory(cacheDir);

        foreach (var dll in ProjectConstants.SdkDistributedDlls)
        {
            var src = Path.Combine(sdkDllDir, dll);
            if (File.Exists(src))
                File.Copy(src, Path.Combine(cacheDir, dll), overwrite: true);
        }

        // 重算缓存版本并写 lock（以复制后的真实 DLL 为准）
        var versions = GetDllVersions(cacheDir);
        var lockFile = ReadCacheLock() ?? new EngineLockFile();
        lockFile.EngineVersion = versions.TryGetValue(ProjectConstants.EngineCoreDll, out var ev) ? ev : "0.0.0";
        lockFile.DllVersions = versions;
        lockFile.LastCheckedUtc = DateTime.UtcNow.ToString("o");
        WriteCacheLock(lockFile);
    }

    /// <inheritdoc/>
    public async Task SeedNewProjectEngineAsync(string projectRootDir)
    {
        await EnsureCacheSeededAsync();

        var cacheDir = PathHelper.GetEngineCacheDirectory();
        var projectDllDir = Path.Combine(projectRootDir, ProjectConstants.DllDir);
        PathHelper.EnsureDirectory(projectDllDir);

        var cacheLock = ReadCacheLock()
            ?? throw new InvalidOperationException(
                "引擎缓存为空（离线且未安装引擎 DLL）。请联网更新引擎缓存或重新安装 SDK。");

        foreach (var dll in ProjectConstants.SdkDistributedDlls)
        {
            var src = Path.Combine(cacheDir, dll);
            if (!File.Exists(src))
                throw new InvalidOperationException(
                    $"引擎缓存缺少 {dll}，无法播种项目。请联网更新引擎缓存或重新安装 SDK。");
            File.Copy(src, Path.Combine(projectDllDir, dll), overwrite: true);
        }

        // 写项目 engine.lock.json（版本真相，来自缓存 lock）
        var projectLock = new EngineLockFile
        {
            EngineVersion = cacheLock.EngineVersion,
            DllVersions = new Dictionary<string, string>(cacheLock.DllVersions, StringComparer.OrdinalIgnoreCase),
            Pinned = false,
            LastCheckedUtc = DateTime.UtcNow.ToString("o"),
        };
        WriteProjectLock(projectRootDir, projectLock);
    }

    // ===== 内部辅助 =====

    private HttpClient CreateClient()
    {
        // 命名客户端由 IHttpClientFactory 管理底层 handler 池，避免套接字耗尽
        var client = _httpClientFactory.CreateClient(EngineUpdateDefaults.HttpClientName);
        return client;
    }

    private string ResolveManifestUrl()
    {
        var settings = TryLoadSdkSettings();
        var url = settings?.EngineUpdateManifestUrl;
        if (string.IsNullOrWhiteSpace(url))
            url = EngineUpdateDefaults.DefaultManifestUrl;

        // 67.6 安全：仅允许白名单主机拉取 manifest，防止恶意 manifest 投毒
        if (!IsAllowedManifestHost(url))
            throw new InvalidOperationException($"manifest 地址主机不在白名单内，已拒绝：{url}");

        return url;
    }

    /// <summary>
    /// 校验 manifest URL 主机是否在白名单内（精确匹配或官方子域）。
    /// <para>白名单见 <see cref="EngineUpdateDefaults.AllowedManifestHosts"/>（官方 GitHub 源）。</para>
    /// </summary>
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

    /// <summary>下载 asset zip → 整包 sha256 校验 → 解压 → 逐 DLL sha256 校验，返回暂存的 DLL 路径表。</summary>
    private async Task<Dictionary<string, string>> DownloadAndStageAsync(
        EngineUpdateManifest manifest, Action<string> log, CancellationToken ct)
    {
        var updatesDir = PathHelper.GetEngineUpdatesDirectory();
        PathHelper.EnsureDirectory(updatesDir);

        var zipPath = Path.Combine(updatesDir, EngineUpdateDefaults.AssetZipFileName);
        var extractedDir = Path.Combine(updatesDir, EngineUpdateDefaults.ExtractedDirName);
        var pendingDir = Path.Combine(updatesDir, EngineUpdateDefaults.PendingDirName);

        // 清理旧暂存
        if (Directory.Exists(extractedDir)) Directory.Delete(extractedDir, recursive: true);
        PathHelper.EnsureDirectory(extractedDir);
        PathHelper.EnsureDirectory(pendingDir);

        // 1. 下载
        log("  下载 asset zip...");
        var client = CreateClient();
        using (var resp = await client.GetAsync(manifest.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zipPath);
            await src.CopyToAsync(dst, ct);
        }
        log("  下载完成。");

        // 2. 整包 sha256 校验（清单提供时）
        if (!string.IsNullOrWhiteSpace(manifest.AssetSha256))
        {
            if (!VerifySha256(zipPath, manifest.AssetSha256))
            {
                File.Delete(zipPath);
                throw new InvalidDataException("asset zip sha256 校验失败，可能被篡改。");
            }
            log("  整包 sha256 校验通过。");
        }

        // 3. 解压（仅取 .dll）
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                var dest = Path.Combine(extractedDir, Path.GetFileName(entry.Name));
                entry.ExtractToFile(dest, overwrite: true);
            }
        }

        // 4. 逐 DLL sha256 校验
        var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dllName in ProjectConstants.SdkDistributedDlls)
        {
            if (!manifest.DllChecksums.TryGetValue(dllName, out var expected))
                throw new InvalidDataException($"清单缺少 {dllName} 的 sha256，拒绝应用。");
            var extractedPath = Path.Combine(extractedDir, dllName);
            if (!File.Exists(extractedPath))
                throw new InvalidDataException($"解压结果缺少 {dllName}。");

            if (!VerifySha256(extractedPath, expected))
                throw new InvalidDataException($"{dllName} sha256 校验失败，可能被篡改。");

            // 67.6 安全：Authenticode 代码签名校验（带签名则验链，无签名放行回落 sha256）
            if (!VerifyAuthenticode(extractedPath))
                throw new InvalidDataException($"{dllName} Authenticode 签名校验失败（签名无效），拒绝应用。");

            // 校验通过 → 移入 pendingDir 作为权威暂存（pending 应用时从这里取）
            var stagedPath = Path.Combine(pendingDir, dllName);
            File.Copy(extractedPath, stagedPath, overwrite: true);
            staged[dllName] = stagedPath;
        }
        log("  全部 DLL sha256 校验通过。");

        // 清理 zip 与解压目录（保留 pendingDir）
        try { File.Delete(zipPath); } catch { /* 忽略 */ }
        try { Directory.Delete(extractedDir, recursive: true); } catch { /* 忽略 */ }

        return staged;
    }

    /// <summary>尝试直接覆盖；失败则转入 pending（兜底，正常路径不会到这里——LingFanEngine.dll 不被锁定）。</summary>
    private static void TryCopyOrPending(
        string dest, string stagedPath, string dllName, EngineUpdateManifest manifest,
        List<string> updated, List<string> pending, List<PendingUpdateEntry> pendingEntries,
        Action<string> log)
    {
        try
        {
            File.Copy(stagedPath, dest, overwrite: true);
            updated.Add(dllName);
            log($"  已热替换: {dllName}");
        }
        catch (IOException)
        {
            StagePending(dest, stagedPath, dllName, manifest, pending, pendingEntries);
            log($"  {dllName} 被占用，已暂存(pending)。");
        }
    }

    private static void StagePending(
        string dest, string stagedPath, string dllName, EngineUpdateManifest manifest,
        List<string> pending, List<PendingUpdateEntry> pendingEntries)
    {
        manifest.DllChecksums.TryGetValue(dllName, out var sha);
        pending.Add(dllName);
        pendingEntries.Add(new PendingUpdateEntry
        {
            DllName = dllName,
            TargetPath = dest,
            StagedPath = stagedPath,
            Sha256 = sha ?? "",
        });
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

    /// <summary>
    /// Authenticode 代码签名校验（67.6 安全）。
    /// <para>规则：DLL 带内嵌签名 → 校验证书链 + 有效期 + 吊销，无效则拒绝；
    /// 无签名 → 放行（回落 sha256 校验，CI 未配代码签名证书时不阻塞发布）；
    /// 非 Windows 或异常 → 保守放行，避免误杀正常更新。</para>
    /// </summary>
    private static bool VerifyAuthenticode(string filePath)
    {
        try
        {
            // 读取 PE 内嵌签名证书；无签名时抛 CryptographicException
            // 注意：CreateFromSignedFile 在 .NET 10 标记 SYSLIB0057 过时，但它是读取 PE 签名证书的唯一 API，
            // 尚无 X509CertificateLoader 等价物，故在此局部抑制该过时警告。
#pragma warning disable SYSLIB0057
            using var cert = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            using var cert2 = new X509Certificate2(cert);
            // Verify 校验链/有效期/吊销（需可信根；CI 应使用受信任的代码签名证书）
            return cert2.Verify();
        }
        catch (CryptographicException)
        {
            // 无内嵌签名：放行，回落 sha256
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            // 非 Windows：Authenticode 不适用，放行
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EngineUpdateService] VerifyAuthenticode 异常: {ex.Message}");
            return true;
        }
    }

    /// <summary>本地 SDK 程序集版本是否 ≥ 指定最低版本（语义化版本 X.Y.Z）。</summary>
    private static bool IsSdkVersionAtLeast(string minVersion)
    {
        var sdkVer = typeof(EngineUpdateService).Assembly.GetName().Version;
        if (sdkVer == null)
            return false;
        return Version.TryParse(minVersion, out var min) && sdkVer >= min;
    }

    /// <summary>返回 a 是否比 b 新（语义化版本 X.Y.Z）。</summary>
    private static bool IsNewer(string a, string b)
    {
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
            return va > vb;
        // 回退字符串比较
        return !string.Equals(a, b, StringComparison.Ordinal) &&
               string.Compare(a, b, StringComparison.Ordinal) > 0;
    }

    /// <summary>
    /// 逐 DLL 判定是否需要更新：清单中任一 DLL 的版本严格大于本地对应 DLL 版本，即返回 true。
    /// <para>这样可覆盖「只更新了某个依赖（如 Pidgin/DslCore/Abstractions）而 LingFanEngine.dll 版本号未变」的情形，
    /// 不会因为 LingFanEngine 没动就误判整体已最新而漏掉那几个 DLL 的更新。</para>
    /// </summary>
    /// <param name="manifest">远端清单（提供全局或逐 DLL 版本）。</param>
    /// <param name="current">本地逐 DLL 版本表（缺失的 DLL 视为 "0.0.0"，应被任何远端版本判定为需更新）。</param>
    private static bool IsUpdateNeeded(EngineUpdateManifest manifest, Dictionary<string, string> current)
    {
        foreach (var dllName in ProjectConstants.SdkDistributedDlls)
        {
            var remote = manifest.GetDllVersion(dllName);
            current.TryGetValue(dllName, out var local);
            local ??= "0.0.0";
            if (IsNewer(remote, local))
                return true;
        }
        return false;
    }

    private static void KillRunningProcesses(string projectDir)
    {
        try
        {
            var normalizedDir = Path.GetFullPath(projectDir).TrimEnd(Path.DirectorySeparatorChar);
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id == Environment.ProcessId) continue;
                    var path = proc.MainModule?.FileName;
                    if (path != null && path.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase))
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(3000);
                    }
                }
                catch { /* 忽略无权限访问的进程 */ }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[EngineUpdateService] KillRunningProcesses: {ex.Message}"); }
    }

    // ===== pending 持久化 =====

    private static string PendingManifestPath =>
        Path.Combine(PathHelper.GetEngineUpdatesDirectory(), EngineUpdateDefaults.PendingManifestFileName);

    private static async Task<PendingUpdateManifest?> LoadPendingManifestAsync(CancellationToken ct)
    {
        var path = PendingManifestPath;
        if (!File.Exists(path))
            return null;
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonHelper.Deserialize(json, SdkJsonContext.Default.PendingUpdateManifest);
        }
        catch
        {
            return null;
        }
    }

    private static async Task SavePendingManifestAsync(PendingUpdateManifest manifest, CancellationToken ct)
    {
        var path = PendingManifestPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            PathHelper.EnsureDirectory(dir);
        var json = JsonHelper.Serialize(manifest, SdkJsonContext.Default.PendingUpdateManifest);
        await File.WriteAllTextAsync(path, json, ct);
    }

    private static void ClearPendingManifest()
    {
        try
        {
            var path = PendingManifestPath;
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { /* 忽略 */ }
    }
}
