using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using LingFanEngine.SDK.Cryptography;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 构建发布服务实现
/// <para>Phase 50：即解即用加密模式——publish 后原地加密选定文件类型，生成清单。</para>
/// </summary>
public class PublishService : IPublishService
{
    private readonly IPackToolService? _packToolService;

    /// <summary>默认构造（无 PackTool）</summary>
    public PublishService() { }

    /// <summary>注入 PackTool 的构造（兼容旧 .lfpack 模式）</summary>
    public PublishService(IPackToolService packToolService)
    {
        _packToolService = packToolService;
    }

    /// <inheritdoc/>
    public async Task<BuildResult> BuildAsync(
        ProjectConfig project, PlatformConfig platform, IProgress<string>? progress = null)
    {
        var logs = new List<string>();
        var result = new BuildResult { Platform = platform.Name, Logs = logs };

        void Log(string msg)
        {
            logs.Add(msg);
            progress?.Report(msg);
        }

        try
        {
            Log($"开始构建 {project.Title} → {platform.Name}");

            var projectDir = project.ProjectDirectory;
            var outputPath = Path.Combine(projectDir, project.Build.OutputPath, platform.RuntimeIdentifier);

            // 1. 根据平台查找对应的 .csproj 文件
            var csprojSuffix = platform.Name switch
            {
                "Windows" => ".Desktop.Windows.csproj",
                "Linux" => ".Desktop.Linux.csproj",
                "macOS" => ".Desktop.Mac.csproj",
                "Android" => ".Android.csproj",
                "iOS" => ".iOS.csproj",
                "Browser" => ".Browser.csproj",
                _ => ".csproj",
            };

            // 在项目目录中查找匹配的 .csproj（递归搜索，防层级不统一）
            var csprojFile = FindCsprojBySuffix(projectDir, csprojSuffix);

            if (csprojFile == null)
            {
                // 列出所有找到的 .csproj 文件供诊断
                var allCsprojs = Directory.GetFiles(projectDir, "*.csproj", SearchOption.AllDirectories);
                var fileList = allCsprojs.Length > 0
                    ? string.Join("\n  ", allCsprojs.Select(p => Path.GetRelativePath(projectDir, p)))
                    : "(无)";
                result.Success = false;
                result.ErrorMessage = $"未找到 {platform.Name} 平台对应的 .csproj（后缀: {csprojSuffix}）。\n项目目录: {projectDir}\n找到的 .csproj 文件:\n  {fileList}";
                Log($"构建失败！未找到 {csprojSuffix}");
                Log($"项目目录下找到的 .csproj 文件:");
                foreach (var f in allCsprojs)
                    Log($"  {Path.GetRelativePath(projectDir, f)}");
                return result;
            }

            var csprojDir = Path.GetDirectoryName(csprojFile) ?? projectDir;
            Log($"项目文件: {Path.GetFileName(csprojFile)}");

            // 2. 查找核心项目目录（GeneratedKeys.cs 需要放在核心项目里，因为核心项目引用了引擎 DLL）
            var coreCsproj = FindCoreCsproj(projectDir, project.Name);
            if (coreCsproj != null)
                Log($"核心项目: {Path.GetRelativePath(projectDir, coreCsproj)}");
            else
                Log($"警告: 未找到核心项目 {project.Name}.csproj，密钥文件将放在平台项目目录");
            var coreDir = coreCsproj != null
                ? Path.GetDirectoryName(coreCsproj) ?? csprojDir
                : csprojDir;
            // 核心项目的 Security 目录路径（相对对核心项目）
            var securityDir = Path.Combine(coreDir, "Security");

            // 2.5 更新项目引用的引擎 DLL（确保使用最新编译的引擎代码）
            UpdateEngineDlls(coreDir, Log);

            // 3. 生成 GeneratedKeys.cs（必须在 publish 前，因为要编译进程序集）
            byte[]? key = null;
            if (project.Encryption is { Enabled: true } && project.Build.EncryptResources)
            {
                Log("正在生成密钥代码...");
                key = KeyManager.GetOrCreateKey(project.ProjectPath);
                var namespaceName = $"{project.Name}.Security";
                var keyCode = KeyInjector.GenerateKeyFile(namespaceName, key, project.Encryption.KeyShardCount);

                // 清理旧的 GeneratedKeys.cs（可能在平台项目目录中）
                foreach (var oldFile in Directory.GetFiles(projectDir, "GeneratedKeys.cs", SearchOption.AllDirectories))
                {
                    try { File.Delete(oldFile); } catch { }
                }

                var keyFilePath = Path.Combine(securityDir, "GeneratedKeys.cs");
                await FileHelper.WriteAllTextAsync(keyFilePath, keyCode);
                Log($"密钥代码已生成: {Path.GetRelativePath(projectDir, keyFilePath)}");
            }

            // 4. 杀掉可能在运行的游戏进程（防止 PDB/DLL 文件被锁定）
            KillRunningProcesses(projectDir, project.Name);

            // 5. 清理输出目录（确保从零开始，避免增量构建残留旧加密文件）
            if (Directory.Exists(outputPath))
            {
                Log("清理输出目录...");
                try
                {
                    Directory.Delete(outputPath, recursive: true);
                }
                catch (Exception ex)
                {
                    Log($"警告: 整目录删除失败 ({ex.Message})，尝试逐个清理资源目录...");
                    // Fallback：逐个删除资源子目录，确保 dotnet publish 能复制新鲜源文件
                    var resourceDirNames = new[] { "Stories", "Media", "Images", "Audio", "Video", "Live2D", "Mods", "Lang" };
                    foreach (var dirName in resourceDirNames)
                    {
                        var resourceDir = Path.Combine(outputPath, dirName);
                        if (Directory.Exists(resourceDir))
                        {
                            try { Directory.Delete(resourceDir, recursive: true); }
                            catch { /* 尽力而为 */ }
                        }
                    }
                }
            }

            // 6. dotnet publish
            Log($"正在执行 dotnet publish (RID: {platform.RuntimeIdentifier})...");

            var publishArgs = $"publish \"{csprojFile}\" -c {project.Build.Configuration} -r {platform.RuntimeIdentifier}";
            // 暂时注释，因为项目自身配置
            //if (project.Build.SelfContained)
            //    publishArgs += " --self-contained";
            //if (!string.IsNullOrEmpty(project.Build.TrimMode))
            //    publishArgs += $" -p:TrimMode={project.Build.TrimMode}";
            //if (project.Build.PublishAot && platform.SupportsAot)
            //    publishArgs += " -p:PublishAot=true";

            publishArgs += $" -o \"{outputPath}\"";
            // 排除调试符号（反编译防护）
            publishArgs += " -p:DebugType=none -p:DebugSymbols=false";

            Log($"执行: dotnet {publishArgs}");

            var exitCode = await ProcessHelper.RunDotNetAsync(publishArgs, csprojDir, progress);
            if (exitCode != 0)
            {
                result.Success = false;
                result.ErrorMessage = $"dotnet publish 失败，退出码: {exitCode}";
                Log($"构建失败！退出码: {exitCode}");
                return result;
            }

            // 7. 即解即用加密：publish 后原地加密选定文件
            if (key != null && project.Encryption is { Enabled: true } encConfig && project.Build.EncryptResources)
            {
                Log("正在加密资源（即解即用模式）...");

                var encryptTypes = encConfig.EncryptFileTypes;
                if (encryptTypes == null || encryptTypes.Count == 0)
                {
                    encryptTypes = EncryptionConfig.AllEncryptableTypes.ToList();
                }
                var extSet = new HashSet<string>(encryptTypes.Select(e => e.ToLowerInvariant()));

                var manifest = new ManifestData();
                var encryptedCount = 0;

                // 扫描输出目录下所有资源子目录（排除已知非资源目录）
                var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "runtimes", "ref", "refs", "bin", "obj" };
                foreach (var subDir in Directory.GetDirectories(outputPath))
                {
                    var dirName = Path.GetFileName(subDir);
                    if (excludeDirs.Contains(dirName)) continue;

                    encryptedCount += await EncryptFilesInPlaceAsync(subDir, outputPath, extSet, key, manifest, Log);
                }

                Log($"已加密 {encryptedCount} 个文件");

                // 生成清单
                if (encConfig.GenerateManifest && manifest.Files.Count > 0)
                {
                    var manifestPath = Path.Combine(outputPath, "manifest.lfmanifest");
                    var manifestJson = SerializeManifest(manifest);

                    if (encConfig.EncryptManifest)
                    {
                        var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);
                        var encryptedManifest = AesEncryptor.Encrypt(manifestBytes, key);
                        await File.WriteAllBytesAsync(manifestPath, encryptedManifest);
                        Log("清单已加密生成");
                    }
                    else
                    {
                        await File.WriteAllTextAsync(manifestPath, manifestJson);
                        Log("清单已生成（未加密）");
                    }
                }
            }

            result.Success = true;
            result.OutputPath = outputPath;
            Log($"构建成功！输出路径: {outputPath}");
        }
        catch (System.Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            Log($"构建异常: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// 原地加密目录中匹配扩展名的文件
    /// </summary>
    /// <param name="dir">要扫描的子目录</param>
    /// <param name="outputRoot">输出根目录（清单路径相对于此）</param>
    private static async Task<int> EncryptFilesInPlaceAsync(
        string dir, string outputRoot, HashSet<string> extSet, byte[] key, ManifestData manifest, Action<string> log)
    {
        var count = 0;
        var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (!extSet.Contains(ext)) continue;

            // 读取文件内容
            var data = await File.ReadAllBytesAsync(file);

            // 已加密文件：用当前密钥解密后重新加密（处理密钥轮换 + 确保内容最新）
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

            // 原地加密
            var encrypted = AesEncryptor.Encrypt(data, key);
            await File.WriteAllBytesAsync(file, encrypted);

            // 记录到清单（路径相对于输出根目录）
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

    /// <inheritdoc/>
    public async Task<List<BuildResult>> BuildAllAsync(
        ProjectConfig project, IProgress<string>? progress = null)
    {
        var results = new List<BuildResult>();

        foreach (var platform in project.TargetPlatforms)
        {
            var result = await BuildAsync(project, platform, progress);
            results.Add(result);
        }

        return results;
    }

    /// <inheritdoc/>
    public IReadOnlyList<PlatformConfig> GetSupportedPlatforms()
    {
        return PlatformConfig.DesktopPlatforms;
    }

    /// <summary>
    /// 手写 JSON 序列化清单（避免 AOT 警告）
    /// </summary>
    private static string SerializeManifest(ManifestData manifest)
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

    /// <summary>简易 JSON 字符串转义</summary>
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

    /// <summary>根据后缀名递归查找 .csproj 文件</summary>
    private static string? FindCsprojBySuffix(string projectDir, string suffix)
    {
        foreach (var file in Directory.GetFiles(projectDir, "*.csproj", SearchOption.AllDirectories))
        {
            if (file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return file;
        }
        return null;
    }

    /// <summary>查找核心项目 .csproj（名称恰好为 {projectName}.csproj，无平台后缀）</summary>
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

    /// <summary>
    /// 将 SDK 自带的最引引擎 DLL 复制到项目的 DLL/ 目录，确保 publish 编译使用最新引擎代码。
    /// </summary>
    private static void UpdateEngineDlls(string coreDir, Action<string> log)
    {
        try
        {
            // SDK 的 DLL 目录位于运行目录下的 DLL/ 文件夹
            var sdkDllDir = Path.Combine(AppContext.BaseDirectory, "DLL");
            if (!Directory.Exists(sdkDllDir)) return;

            // 项目的 DLL 目录（核心项目同级）
            var projectDllDir = Path.Combine(coreDir, "DLL");
            if (!Directory.Exists(projectDllDir))
                Directory.CreateDirectory(projectDllDir);

            var engineDlls = new[] { "LingFanEngine.dll", "LingFanEngine.Abstractions.dll", "LingFanEngine.DslCore.dll", "Pidgin.dll" };
            var updated = 0;
            foreach (var dllName in engineDlls)
            {
                var srcPath = Path.Combine(sdkDllDir, dllName);
                if (!File.Exists(srcPath)) continue;
                var destPath = Path.Combine(projectDllDir, dllName);
                File.Copy(srcPath, destPath, overwrite: true);
                updated++;
            }
            if (updated > 0)
                log($"已更新 {updated} 个引擎 DLL");
        }
        catch (Exception ex)
        {
            log($"警告: 更新引擎 DLL 失败: {ex.Message}");
        }
    }

    /// <summary>杀掉从项目目录运行的游戏进程（防止 DLL/PDB 文件锁定）</summary>
    private static void KillRunningProcesses(string projectDir, string projectName)
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
        catch { }
    }
}
internal class ManifestData
{
    public List<ManifestEntry> Files { get; set; } = [];
}

/// <summary>清单条目</summary>
internal class ManifestEntry
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}
