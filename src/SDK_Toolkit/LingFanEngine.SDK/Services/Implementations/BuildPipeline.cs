using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Constants;
using LingFanEngine.SDK.Cryptography;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 构建阶段上下文——承载项目/平台/解析路径/密钥，供各阶段读写（阶段间通过它传递 key 等）。
/// </summary>
public sealed class BuildContext
{
    /// <summary>项目配置</summary>
    public required ProjectConfig Project { get; init; }

    /// <summary>平台配置</summary>
    public required PlatformConfig Platform { get; init; }

    /// <summary>项目根目录（.lfproj 所在）</summary>
    public required string ProjectDir { get; init; }

    /// <summary>输出目录</summary>
    public required string OutputPath { get; init; }

    /// <summary>选择的平台 .csproj 绝对路径（探测阶段填充）</summary>
    public string CsprojFile { get; set; } = "";

    /// <summary>平台 .csproj 所在目录</summary>
    public string CsprojDir { get; set; } = "";

    /// <summary>核心项目目录（GeneratedKeys.cs 所在）</summary>
    public string CoreDir { get; set; } = "";

    /// <summary>Resources 目录</summary>
    public string ResourcesDir { get; set; } = "";

    /// <summary>Security 目录（核心项目下）</summary>
    public string SecurityDir { get; set; } = "";

    /// <summary>密钥（生成阶段填充，加密阶段消费）</summary>
    public byte[]? Key;

    /// <summary>失败时由失败阶段填充诊断信息</summary>
    public string? FailureMessage;

    /// <summary>进度回调（供子进程等直接流式报告）</summary>
    public IProgress<string>? Progress;

    /// <summary>解析平台 .csproj 后缀（按 RID/平台名）</summary>
    public string CsprojSuffix => Platform.RuntimeIdentifier switch
    {
        "win-x64" => ProjectConstants.CsprojSuffixWindows,
        "linux-x64" => ProjectConstants.CsprojSuffixLinux,
        var r when r.StartsWith("osx-", StringComparison.OrdinalIgnoreCase) => ProjectConstants.CsprojSuffixMac,
        _ => Platform.Name switch
        {
            "Android" => ProjectConstants.CsprojSuffixAndroid,
            "iOS" => ProjectConstants.CsprojSuffixIOS,
            "Browser" => ProjectConstants.CsprojSuffixBrowser,
            _ => ProjectConstants.CsprojSuffixDefault,
        },
    };

    /// <summary>按约定初始化路径。</summary>
    public static BuildContext Create(ProjectConfig project, PlatformConfig platform)
    {
        var projectDir = project.ProjectDirectory;
        var outputPath = Path.Combine(projectDir, project.Build.OutputPath, platform.RuntimeIdentifier);
        return new BuildContext
        {
            Project = project,
            Platform = platform,
            ProjectDir = projectDir,
            OutputPath = outputPath,
            ResourcesDir = Path.Combine(projectDir, ProjectConstants.ResourcesDir),
            SecurityDir = "",
        };
    }
}

/// <summary>构建阶段——可观测、可单测（确定性）。返回 false 即中止管线。</summary>
public interface IBuildStage
{
    /// <summary>阶段名（日志展示）</summary>
    string Name { get; }

    /// <summary>执行阶段；失败时设 ctx.FailureMessage 并返回 false。</summary>
    Task<bool> ExecuteAsync(BuildContext ctx, Action<string> log, CancellationToken ct);
}

/// <summary>
/// 构建管线——编排 探测/预处理/编译/加密+清单 各阶段，中止即返回失败结果。
/// <para>副作用写入均经明示辅助（沿用 PublishService 原语义），阶段化后更可观测、可单元测试。</para>
/// </summary>
public sealed class BuildPipeline
{
    private readonly IReadOnlyList<IBuildStage> _stages;

    /// <summary>创建管线（按序执行阶段）。</summary>
    public BuildPipeline(params IBuildStage[] stages) => _stages = stages;

    /// <summary>运行管线，产出构建结果。</summary>
    public async Task<BuildResult> RunAsync(ProjectConfig project, PlatformConfig platform, IProgress<string>? progress)
    {
        var logs = new List<string>();
        var result = new BuildResult { Platform = platform.Name, Logs = logs };
        void Log(string msg)
        {
            logs.Add(msg);
            progress?.Report(msg);
        }

        var ctx = BuildContext.Create(project, platform);
        ctx.Progress = progress;

        try
        {
            Log($"开始构建 {project.Title} → {platform.Name}");

            foreach (var stage in _stages)
            {
                Log($"[{stage.Name}]");
                if (!await stage.ExecuteAsync(ctx, Log, default).ConfigureAwait(false))
                {
                    result.Success = false;
                    result.ErrorMessage = ctx.FailureMessage ?? $"{stage.Name} 阶段失败";
                    Log($"构建失败！({stage.Name}) {ctx.FailureMessage}");
                    return result;
                }
            }

            result.Success = true;
            result.OutputPath = ctx.OutputPath;
            Log($"构建成功！输出路径: {ctx.OutputPath}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            Log($"构建异常: {ex.Message}");
        }

        return result;
    }
}

/// <summary>阶段①：探测——定位平台 .csproj 与核心项目，解析相关目录。</summary>
public sealed class DetectStage : IBuildStage
{
    public string Name => "探测";

    public Task<bool> ExecuteAsync(BuildContext ctx, Action<string> log, CancellationToken ct)
    {
        var suffix = ctx.CsprojSuffix;
        var csprojFile = FindCsprojBySuffix(ctx.ProjectDir, suffix);

        if (csprojFile == null)
        {
            var allCsprojs = Directory.GetFiles(ctx.ProjectDir, "*.csproj", SearchOption.AllDirectories);
            var fileList = allCsprojs.Length > 0
                ? string.Join("\n  ", allCsprojs.Select(p => Path.GetRelativePath(ctx.ProjectDir, p)))
                : "(无)";
            ctx.FailureMessage = $"未找到 {ctx.Platform.Name} 平台对应的 .csproj（后缀: {suffix}）。\n项目目录: {ctx.ProjectDir}\n找到的 .csproj 文件:\n  {fileList}";
            log($"未找到 {suffix}");
            return Task.FromResult(false);
        }

        ctx.CsprojFile = csprojFile;
        ctx.CsprojDir = Path.GetDirectoryName(csprojFile) ?? ctx.ProjectDir;
        log($"项目文件: {Path.GetFileName(csprojFile)}");

        var coreCsproj = FindCoreCsproj(ctx.ProjectDir, ctx.Project.Name);
        if (coreCsproj != null)
            log($"核心项目: {Path.GetRelativePath(ctx.ProjectDir, coreCsproj)}");
        else
            log($"警告: 未找到核心项目 {ctx.Project.Name}.csproj，密钥文件将放在平台项目目录");
        ctx.CoreDir = coreCsproj != null
            ? Path.GetDirectoryName(coreCsproj) ?? ctx.CsprojDir
            : ctx.CsprojDir;
        ctx.SecurityDir = Path.Combine(ctx.CoreDir, ProjectConstants.SecurityDir);

        return Task.FromResult(true);
    }

    private static string? FindCsprojBySuffix(string projectDir, string suffix)
    {
        foreach (var file in Directory.GetFiles(projectDir, "*.csproj", SearchOption.AllDirectories))
        {
            if (file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return file;
        }
        return null;
    }

    private static string? FindCoreCsproj(string projectDir, string projectName)
    {
        var targetName = $"{projectName}.csproj";
        foreach (var file in Directory.GetFiles(projectDir, "*.csproj", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).Equals(targetName, StringComparison.OrdinalIgnoreCase))
                return file;
        }
        return null;
    }
}

/// <summary>阶段②：预处理——DLL 同步、资源迁移、路径修复、源解密、密钥生成、杀进程、清空输出。</summary>
public sealed class PreprocessStage : IBuildStage
{
    public string Name => "预处理";

    public async Task<bool> ExecuteAsync(BuildContext ctx, Action<string> log, CancellationToken ct)
    {
        var project = ctx.Project;

        BuildPipelineHelpers.UpdateEngineDlls(ctx.ProjectDir, log);
        BuildPipelineHelpers.MigrateResourcesToResourcesDir(ctx.ProjectDir, ctx.CoreDir, log);
        BuildPipelineHelpers.EnsureCsprojResourcePaths(ctx.ProjectDir, log);
        BuildPipelineHelpers.DecryptSourceFilesIfNeeded(ctx.ResourcesDir, project.ProjectPath, log);

        if (project.Encryption is { Enabled: true } && project.Build.EncryptResources)
        {
            log("正在生成密钥代码...");
            var key = KeyManager.GetOrCreateKey(project.ProjectPath);
            var namespaceName = $"{project.Name}.Security";
            var keyCode = KeyInjector.GenerateKeyFile(namespaceName, key, project.Encryption.KeyShardCount);

            foreach (var oldFile in Directory.GetFiles(ctx.ProjectDir, ProjectConstants.GeneratedKeysFileName, SearchOption.AllDirectories))
            {
                try { File.Delete(oldFile); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
            }

            var keyFilePath = Path.Combine(ctx.SecurityDir, ProjectConstants.GeneratedKeysFileName);
            await FileHelper.WriteAllTextAsync(keyFilePath, keyCode).ConfigureAwait(false);
            log($"密钥代码已生成: {Path.GetRelativePath(ctx.ProjectDir, keyFilePath)}");
            ctx.Key = key;

            BuildPipelineHelpers.EnsureKeyProviderRegistered(ctx.ProjectDir, project.Name, log);
        }

        BuildPipelineHelpers.KillRunningProcesses(ctx.ProjectDir, project.Name);

        // 清理输出目录
        if (Directory.Exists(ctx.OutputPath))
        {
            log("清理输出目录...");
            try
            {
                Directory.Delete(ctx.OutputPath, recursive: true);
            }
            catch (Exception ex)
            {
                log($"警告: 整目录删除失败 ({ex.Message})，尝试逐个清理资源目录...");
                foreach (var dirName in ProjectConstants.ResourceDirNames)
                {
                    var resourceDir = Path.Combine(ctx.OutputPath, dirName);
                    if (Directory.Exists(resourceDir))
                    {
                        try { Directory.Delete(resourceDir, recursive: true); } catch { /* 尽力而为 */ }
                    }
                }
            }
        }

        return true;
    }
}

/// <summary>阶段③：编译——dotnet publish。</summary>
public sealed class PublishStage : IBuildStage
{
    public string Name => "编译";

    public async Task<bool> ExecuteAsync(BuildContext ctx, Action<string> log, CancellationToken ct)
    {
        log($"正在执行 dotnet publish (RID: {ctx.Platform.RuntimeIdentifier})...");

        var publishArgs = $"publish \"{ctx.CsprojFile}\" -c {ctx.Project.Build.Configuration} -r {ctx.Platform.RuntimeIdentifier}";
        publishArgs += $" -o \"{ctx.OutputPath}\"";
        publishArgs += $" -p:{ProjectConstants.MsbuildDebugTypeNone} -p:{ProjectConstants.MsbuildDebugSymbolsFalse}";

        log($"执行: dotnet {publishArgs}");
        var exitCode = await ProcessHelper.RunDotNetAsync(publishArgs, ctx.CsprojDir, ctx.Progress).ConfigureAwait(false);
        if (exitCode != 0)
        {
            ctx.FailureMessage = $"dotnet publish 失败，退出码: {exitCode}";
            log($"构建失败！退出码: {exitCode}");
            return false;
        }
        return true;
    }
}

/// <summary>阶段④：即解即用加密 + 生成清单。</summary>
public sealed class EncryptStage : IBuildStage
{
    public string Name => "加密+清单";

    public async Task<bool> ExecuteAsync(BuildContext ctx, Action<string> log, CancellationToken ct)
    {
        var project = ctx.Project;
        if (ctx.Key == null || project.Encryption is not { Enabled: true } encConfig || !project.Build.EncryptResources)
            return true;

        log("正在加密资源（即解即用模式）...");

        var encryptTypes = encConfig.EncryptFileTypes;
        if (encryptTypes == null || encryptTypes.Count == 0)
            encryptTypes = EncryptionConfig.AllEncryptableTypes.ToList();
        var extSet = new HashSet<string>(encryptTypes.Select(e => e.ToLowerInvariant()));

        var manifest = new ManifestData();
        var encryptedCount = 0;

        var excludeDirs = new HashSet<string>(ProjectConstants.EncryptExcludeDirs, StringComparer.OrdinalIgnoreCase);
        foreach (var subDir in Directory.GetDirectories(ctx.OutputPath))
        {
            var dirName = Path.GetFileName(subDir);
            if (excludeDirs.Contains(dirName)) continue;
            encryptedCount += await BuildPipelineHelpers.EncryptFilesInPlaceAsync(subDir, ctx.OutputPath, extSet, ctx.Key, manifest, log).ConfigureAwait(false);
        }

        log($"已加密 {encryptedCount} 个文件");

        if (encConfig.GenerateManifest && manifest.Files.Count > 0)
        {
            var manifestPath = Path.Combine(ctx.OutputPath, ProjectConstants.ManifestFileName);
            var manifestJson = BuildPipelineHelpers.SerializeManifest(manifest);
            if (encConfig.EncryptManifest)
            {
                var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);
                var encryptedManifest = AesEncryptor.Encrypt(manifestBytes, ctx.Key);
                await File.WriteAllBytesAsync(manifestPath, encryptedManifest).ConfigureAwait(false);
                log("清单已加密生成");
            }
            else
            {
                await File.WriteAllTextAsync(manifestPath, manifestJson).ConfigureAwait(false);
                log("清单已生成（未加密）");
            }
        }

        return true;
    }
}

/// <summary>构建管线辅助（原 PublishService 静态工具集中于此）。</summary>
internal static class BuildPipelineHelpers
{
    /// <summary>原地加密目录中匹配扩展名的文件。</summary>
    public static async Task<int> EncryptFilesInPlaceAsync(
        string dir, string outputRoot, HashSet<string> extSet, byte[] key, ManifestData manifest, Action<string> log)
    {
        var count = 0;
        var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (!extSet.Contains(ext)) continue;

            var data = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
            if (AesEncryptor.IsEncrypted(data))
            {
                try
                {
                    data = AesEncryptor.Decrypt(data, key);
                    log($"  重新加密（解密旧密文）: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    log($"  ⚠ 跳过（无法解密旧密文，可能密钥已变更）: {Path.GetFileName(file)} — {ex.Message}");
                    continue;
                }
            }

            var encrypted = AesEncryptor.Encrypt(data, key);
            await File.WriteAllBytesAsync(file, encrypted).ConfigureAwait(false);

            var sha256 = SHA256.HashData(encrypted);
            manifest.Files.Add(new ManifestEntry
            {
                Path = Path.GetRelativePath(outputRoot, file).Replace('\\', '/'),
                Size = encrypted.Length,
                Sha256 = Convert.ToHexString(sha256).ToLowerInvariant(),
            });
            count++;
        }

        return count;
    }

    /// <summary>手写 JSON 序列化清单（避免 AOT 警告）。</summary>
    public static string SerializeManifest(ManifestData manifest)
    {
        using var sw = new StringWriter();
        sw.Write("{\"files\":[");
        for (var i = 0; i < manifest.Files.Count; i++)
        {
            if (i > 0) sw.Write(',');
            var f = manifest.Files[i];
            sw.Write("{\"path\":");
            WriteEscapedJsonString(sw, f.Path);
            sw.Write($",\"size\":{f.Size},\"sha256\":");
            WriteEscapedJsonString(sw, f.Sha256);
            sw.Write('}');
        }
        sw.Write("]}");
        return sw.ToString();
    }

    private static void WriteEscapedJsonString(StringWriter sw, string s)
    {
        sw.Write('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sw.Write("\\\""); break;
                case '\\': sw.Write("\\\\"); break;
                case '\n': sw.Write("\\n"); break;
                case '\r': sw.Write("\\r"); break;
                case '\t': sw.Write("\\t"); break;
                default: sw.Write(c); break;
            }
        }
        sw.Write('"');
    }

    /// <summary>将 SDK DLL 复制到用户项目 DLL/（更新引擎依赖）。</summary>
    public static void UpdateEngineDlls(string projectRootDir, Action<string> log)
    {
        try
        {
            var sdkDllDir = Path.Combine(AppContext.BaseDirectory, ProjectConstants.DllDir);
            if (!Directory.Exists(sdkDllDir))
            {
                log($"警告: SDK DLL 目录不存在: {sdkDllDir}，跳过引擎 DLL 更新");
                return;
            }

            var projectDllDir = Path.Combine(projectRootDir, ProjectConstants.DllDir);
            if (!Directory.Exists(projectDllDir))
                Directory.CreateDirectory(projectDllDir);

            var engineDlls = ProjectConstants.SdkDistributedDlls;
            var updated = 0;
            var missing = 0;
            foreach (var dllName in engineDlls)
            {
                var srcPath = Path.Combine(sdkDllDir, dllName);
                if (!File.Exists(srcPath))
                {
                    log($"  警告: 源 DLL 不存在: {dllName}（请重新编译引擎核心）");
                    missing++;
                    continue;
                }
                var destPath = Path.Combine(projectDllDir, dllName);
                File.Copy(srcPath, destPath, overwrite: true);
                log($"  已复制: {dllName}");
                updated++;
            }

            log($"引擎 DLL 更新完成: {updated} 个成功, {missing} 个缺失");
        }
        catch (Exception ex)
        {
            log($"警告: 更新引擎 DLL 失败: {ex.Message}");
        }
    }

    /// <summary>杀掉从项目目录运行的游戏进程（防止 DLL/PDB 文件锁定）。</summary>
    public static void KillRunningProcesses(string projectDir, string projectName)
    {
        try
        {
            var normalizedDir = Path.GetFullPath(projectDir).TrimEnd(Path.DirectorySeparatorChar);
            foreach (var proc in System.Diagnostics.Process.GetProcesses())
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
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
    }

    /// <summary>解密源目录中可能已加密的文件（源文件必须始终明文）。</summary>
    public static void DecryptSourceFilesIfNeeded(string resourcesDir, string projectPath, Action<string> log)
    {
        try
        {
            var key = KeyManager.LoadKey(projectPath);
            if (key == null) return;

            var resourceDirs = ProjectConstants.ResourceDirNames;
            var decryptedCount = 0;

            foreach (var dirName in resourceDirs)
            {
                var dirPath = Path.Combine(resourcesDir, dirName);
                if (!Directory.Exists(dirPath)) continue;

                var files = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var data = File.ReadAllBytes(file);
                        if (!AesEncryptor.IsEncrypted(data)) continue;
                        var plain = AesEncryptor.Decrypt(data, key);
                        File.WriteAllBytes(file, plain);
                        decryptedCount++;
                        log($"  已解密源文件: {Path.GetRelativePath(resourcesDir, file)}");
                    }
                    catch (Exception ex)
                    {
                        log($"  ⚠ 无法解密源文件: {Path.GetRelativePath(resourcesDir, file)} — {ex.Message}");
                    }
                }
            }

            if (decryptedCount > 0)
                log($"  源文件解密完成: {decryptedCount} 个文件已恢复为明文");
        }
        catch (Exception ex)
        {
            log($"  警告: 解密源文件检查失败: {ex.Message}");
        }
    }

    /// <summary>修复旧项目 .csproj 的资源路径（移除旧引用行）。</summary>
    public static void EnsureCsprojResourcePaths(string projectDir, Action<string> log)
    {
        try
        {
            var csprojFiles = Directory.GetFiles(projectDir, "*.csproj", SearchOption.AllDirectories);
            var patched = 0;
            var resourceDirs = ProjectConstants.ResourceDirNames;

            foreach (var csproj in csprojFiles)
            {
                if (csproj.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    csproj.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;

                var content = File.ReadAllText(csproj);
                var changed = false;

                foreach (var dir in resourceDirs)
                {
                    var v3Pattern = $@"<None\s+Include=""{dir}\\\*\*""[^/]*/>";
                    var v2Pattern = $@"<None\s+Include=""\.\.\\{dir}\\\*\*""[^/]*/>";
                    var v1Pattern = $@"<None\s+Include=""\.\.\\\.\.\\\.\.\\\.\.\\Resources\\{dir}\\\*\*""[^/]*/>";

                    foreach (var pattern in new[] { v3Pattern, v2Pattern, v1Pattern })
                    {
                        if (System.Text.RegularExpressions.Regex.Matches(content, pattern,
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count > 0)
                        {
                            content = System.Text.RegularExpressions.Regex.Replace(content, pattern, "",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            changed = true;
                        }
                    }
                }

                if (content.Contains("LinkBase="))
                {
                    content = System.Text.RegularExpressions.Regex.Replace(content, @"\s+LinkBase=""[^""]*""", "");
                    changed = true;
                }

                if (changed)
                    content = System.Text.RegularExpressions.Regex.Replace(content, @"\n\s*\n\s*\n", "\n\n");

                if (changed)
                {
                    File.WriteAllText(csproj, content);
                    patched++;
                    log($"  已清理 .csproj 旧资源引用: {Path.GetRelativePath(projectDir, csproj)}");
                }
            }

            if (patched > 0)
                log($"  .csproj 资源路径清理完成: {patched} 个文件");
        }
        catch (Exception ex)
        {
            log($"  警告: 修复 .csproj 资源路径失败: {ex.Message}");
        }
    }

    /// <summary>迁移旧项目资源到 Resources 目录。</summary>
    public static void MigrateResourcesToResourcesDir(string projectDir, string coreDir, Action<string> log)
    {
        try
        {
            var resourcesDir = Path.Combine(projectDir, ProjectConstants.ResourcesDir);
            var resourceDirs = ProjectConstants.ResourceDirNames;
            var migrated = 0;

            Directory.CreateDirectory(resourcesDir);

            foreach (var dirName in resourceDirs)
            {
                var destPath = Path.Combine(resourcesDir, dirName);
                var srcCore = Path.Combine(coreDir, dirName);
                var srcRoot = Path.Combine(projectDir, dirName);

                if (Directory.Exists(srcCore) && !string.Equals(srcCore, destPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(destPath))
                    {
                        CopyDirectoryMerge(srcCore, destPath, overwrite: false);
                        Directory.Delete(srcCore, recursive: true);
                        log($"  已合并迁移: {dirName}/ → Resources/{dirName}（从核心项目目录）");
                    }
                    else
                    {
                        Directory.Move(srcCore, destPath);
                        log($"  已迁移: {dirName}/ → Resources/{dirName}（从核心项目目录）");
                    }
                    migrated++;
                }
                else if (Directory.Exists(srcRoot) && !string.Equals(srcRoot, destPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(destPath))
                    {
                        CopyDirectoryMerge(srcRoot, destPath, overwrite: false);
                        Directory.Delete(srcRoot, recursive: true);
                        log($"  已合并迁移: {dirName}/ → Resources/{dirName}（从项目根目录）");
                    }
                    else
                    {
                        Directory.Move(srcRoot, destPath);
                        log($"  已迁移: {dirName}/ → Resources/{dirName}（从项目根目录）");
                    }
                    migrated++;
                }
            }

            if (migrated > 0)
                log($"  资源目录迁移完成: {migrated} 个目录已移至 Resources/");
        }
        catch (Exception ex)
        {
            log($"  警告: 资源目录迁移失败: {ex.Message}");
        }
    }

    /// <summary>递归合并目录（源→目标），不覆盖已存在文件。</summary>
    private static void CopyDirectoryMerge(string sourceDir, string destDir, bool overwrite)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destPath = Path.Combine(destDir, Path.GetFileName(file));
            if (!File.Exists(destPath) || overwrite)
                File.Copy(file, destPath, overwrite: true);
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
            CopyDirectoryMerge(subDir, Path.Combine(destDir, Path.GetFileName(subDir)), overwrite);
    }

    /// <summary>确保用户项目 DI 注册 GeneratedKeyProvider。</summary>
    public static void EnsureKeyProviderRegistered(string projectDir, string projectName, Action<string> log)
    {
        try
        {
            var securityNamespace = $"{projectName}.Security";
            var csFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories);
            string? patchedFile = null;

            foreach (var csFile in csFiles)
            {
                if (csFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    csFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;

                var content = File.ReadAllText(csFile);
                if (!content.Contains(ProjectConstants.AddLingFanEngineMethod)) continue;
                if (content.Contains(ProjectConstants.GeneratedKeyProviderClass)) continue;

                var idx = content.IndexOf(ProjectConstants.AddLingFanEngineMethod);
                if (idx < 0) continue;

                var lineStart = content.LastIndexOf('\n', idx - 1);
                if (lineStart < 0) lineStart = 0;
                var indent = "";
                for (var i = lineStart + 1; i < idx && i < content.Length; i++)
                {
                    if (content[i] == ' ' || content[i] == '\t') indent += content[i];
                    else break;
                }

                var usingsToAdd = new List<string>();
                if (!content.Contains($"using {securityNamespace};"))
                    usingsToAdd.Add($"using {securityNamespace};");
                if (!content.Contains($"using {ProjectConstants.EncryptionKeyProviderNamespace};"))
                    usingsToAdd.Add($"using {ProjectConstants.EncryptionKeyProviderNamespace};");

                if (usingsToAdd.Count > 0)
                {
                    var firstUsingEnd = content.IndexOf(';');
                    if (firstUsingEnd >= 0)
                    {
                        var lineEnd = content.IndexOf('\n', firstUsingEnd);
                        if (lineEnd < 0) lineEnd = content.Length;
                        content = content[..(lineEnd + 1)] + string.Join("\n", usingsToAdd) + "\n" + content[(lineEnd + 1)..];
                        idx = content.IndexOf(ProjectConstants.AddLingFanEngineMethod);
                    }
                }

                lineStart = content.LastIndexOf('\n', idx - 1);
                if (lineStart < 0) lineStart = 0;
                else lineStart++;

                var registrationLine =
                    $"{indent}services.AddSingleton<IEncryptionKeyProvider, GeneratedKeyProvider>();\n" +
                    $"{indent}// ↑ 自动注入：加密密钥提供者必须在 AddLingFanEngine 之前注册\n";

                content = content[..lineStart] + registrationLine + content[lineStart..];
                File.WriteAllText(csFile, content);
                patchedFile = csFile;
                log($"  已自动注入 GeneratedKeyProvider 注册: {Path.GetRelativePath(projectDir, csFile)}");
                break;
            }

            if (patchedFile == null)
                log("  GeneratedKeyProvider 注册检查完成（已存在或无需注入）");
        }
        catch (Exception ex)
        {
            log($"  警告: 自动注入 GeneratedKeyProvider 失败: {ex.Message}");
        }
    }
}

/// <summary>清单数据（内部）。</summary>
internal class ManifestData
{
    public List<ManifestEntry> Files { get; set; } = [];
}

/// <summary>清单条目（内部）。</summary>
internal class ManifestEntry
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}