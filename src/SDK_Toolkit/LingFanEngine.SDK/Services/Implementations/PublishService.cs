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
            // Phase 57: 传入 projectDir 而非 coreDir——.csproj 的 HintPath 指向 projectDir/DLL/
            UpdateEngineDlls(projectDir, Log);

            // 2.6 修复旧项目的 .csproj 资源路径（Images/Audio/Video 从引擎仓库路径改为项目根目录路径）
            EnsureCsprojResourcePaths(projectDir, Log);

            // 2.7 解密源目录中可能已加密的文件（上一次构建可能意外加密了源文件）
            // 源文件必须始终是明文——dotnet publish 从源目录复制文件到输出目录，加密只在输出目录原地执行
            DecryptSourceFilesIfNeeded(projectDir, project.ProjectPath, Log);

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

                // 确保用户项目的 DI 注册中包含 GeneratedKeyProvider（旧项目可能缺少此注册）
                EnsureKeyProviderRegistered(projectDir, project.Name, Log);
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
    /// 将 SDK 输出目录中的引擎 DLL 复制到用户项目的 DLL/ 目录，确保 publish 编译使用最新引擎代码。
    /// <para>Phase 57 修复：传入 projectRootDir 而非 coreDir——
    /// .csproj 的 HintPath 为 ..\DLL\xxx.dll（指向项目根的 DLL/），
    /// 因此 DLL 必须复制到 projectRootDir/DLL/ 而非 coreDir/DLL/。</para>
    /// <para>Phase 58：SDK 与引擎核心分离——SDK/DLL/ 只含 3 个 DLL（Abstractions + DslCore + Pidgin），
    /// LingFanEngine.dll 由项目模板的 DLL/ 目录提供，不在 SDK 层分发。
    /// 用户从模板创建项目时即获得最新 LingFanEngine.dll，无需运行时更新。</para>
    /// </summary>
    /// <param name="projectRootDir">用户项目根目录（.lfproj 所在目录，DLL/ 的父目录）</param>
    /// <param name="log">日志回调</param>
    private static void UpdateEngineDlls(string projectRootDir, Action<string> log)
    {
        try
        {
            // SDK 输出目录下的 DLL/ 子目录（由 .csproj 的 <None Include="DLL\*.dll" CopyToOutputDirectory="PreserveNewest" /> 复制）
            var sdkDllDir = Path.Combine(AppContext.BaseDirectory, "DLL");
            if (!Directory.Exists(sdkDllDir))
            {
                log($"警告: SDK DLL 目录不存在: {sdkDllDir}，跳过引擎 DLL 更新");
                return;
            }

            // 用户项目的 DLL 目录（项目根/DLL/——.csproj HintPath 指向此处）
            var projectDllDir = Path.Combine(projectRootDir, "DLL");
            if (!Directory.Exists(projectDllDir))
                Directory.CreateDirectory(projectDllDir);

            // Phase 58: SDK 与引擎核心分离，只更新 3 个 DLL
            // LingFanEngine.dll 由模板提供（模板 DLL/ 目录在引擎编译时自动同步）
            var engineDlls = new[] { "LingFanEngine.Abstractions.dll", "LingFanEngine.DslCore.dll", "Pidgin.dll" };
            var updated = 0;
            var missing = 0;
            foreach (var dllName in engineDlls)
            {
                var srcPath = Path.Combine(sdkDllDir, dllName);
                if (!File.Exists(srcPath))
                {
                    log($"  警告: 源 DLL 不存在: {dllName}");
                    missing++;
                    continue;
                }
                var destPath = Path.Combine(projectDllDir, dllName);
                File.Copy(srcPath, destPath, overwrite: true);
                log($"  已复制: {dllName}");
                updated++;
            }

            // 检查 LingFanEngine.dll 是否存在于用户项目（由模板提供）
            var engineCorePath = Path.Combine(projectDllDir, "LingFanEngine.dll");
            if (!File.Exists(engineCorePath))
                log($"  警告: LingFanEngine.dll 不存在于项目 DLL/ 目录——请从模板复制或重新创建项目");

            log($"引擎 DLL 更新完成: {updated} 个成功, {missing} 个缺失");
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

    /// <summary>
    /// 解密源目录中可能已加密的文件。
    /// <para>上一次构建可能意外加密了源文件（旧版 bug 或手动操作）。
    /// 源文件必须始终是明文——dotnet publish 从源目录复制到输出目录，
    /// 加密只在输出目录原地执行。</para>
    /// <para>此方法扫描项目根目录下的资源子目录，检测 LFEN 加密文件并解密。
    /// 解密需要密钥——从 KeyManager 加载，如果密钥不存在则跳过。</para>
    /// </summary>
    private static void DecryptSourceFilesIfNeeded(string projectDir, string projectPath, Action<string> log)
    {
        try
        {
            // 尝试加载已有密钥（如果用户从未加密过则无密钥，跳过）
            var key = KeyManager.LoadKey(projectPath);
            if (key == null) return; // 无密钥 = 源文件从未被加密，无需处理

            var resourceDirs = new[] { "Stories", "Media", "Images", "Audio", "Video", "Live2D", "Mods", "Lang" };
            var decryptedCount = 0;

            foreach (var dirName in resourceDirs)
            {
                var dirPath = Path.Combine(projectDir, dirName);
                if (!Directory.Exists(dirPath)) continue;

                var files = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var data = File.ReadAllBytes(file);
                        if (!AesEncryptor.IsEncrypted(data)) continue;

                        // 解密并写回明文
                        var plain = AesEncryptor.Decrypt(data, key);
                        File.WriteAllBytes(file, plain);
                        decryptedCount++;
                        log($"  已解密源文件: {Path.GetRelativePath(projectDir, file)}");
                    }
                    catch (Exception ex)
                    {
                        log($"  ⚠ 无法解密源文件: {Path.GetRelativePath(projectDir, file)} — {ex.Message}");
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

    /// <summary>
    /// 修复旧项目的 .csproj 资源路径。
    /// <para>旧模板中 Images/Audio/Video 使用 ..\..\..\..\Resources\ 路径（指向引擎仓库），
    /// 新模板统一改为 ..\（指向项目根目录）。</para>
    /// <para>此方法扫描核心 .csproj 文件，将旧路径替换为新路径。</para>
    /// </summary>
    private static void EnsureCsprojResourcePaths(string projectDir, Action<string> log)
    {
        try
        {
            var csprojFiles = Directory.GetFiles(projectDir, "*.csproj", SearchOption.AllDirectories);
            var patched = 0;

            foreach (var csproj in csprojFiles)
            {
                // 跳过 obj/bin 目录
                if (csproj.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    csproj.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;

                var content = File.ReadAllText(csproj);
                var changed = false;

                // 旧路径模式：..\..\..\..\Resources\Images\** → 新路径：..\Images\**
                var oldPatterns = new[]
                {
                    ("..\\..\\..\\..\\Resources\\Images\\", "..\\Images\\"),
                    ("..\\..\\..\\..\\Resources\\Audio\\", "..\\Audio\\"),
                    ("..\\..\\..\\..\\Resources\\Video\\", "..\\Video\\"),
                    ("../../../../Resources/Images/", "../Images/"),
                    ("../../../../Resources/Audio/", "../Audio/"),
                    ("../../../../Resources/Video/", "../Video/"),
                };

                foreach (var (oldPath, newPath) in oldPatterns)
                {
                    if (content.Contains(oldPath))
                    {
                        content = content.Replace(oldPath, newPath);
                        changed = true;
                    }
                }

                if (changed)
                {
                    File.WriteAllText(csproj, content);
                    patched++;
                    log($"  已修复 .csproj 资源路径: {Path.GetRelativePath(projectDir, csproj)}");
                }
            }

            if (patched > 0)
                log($"  .csproj 资源路径修复完成: {patched} 个文件");
        }
        catch (Exception ex)
        {
            log($"  警告: 修复 .csproj 资源路径失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 确保用户项目的 DI 注册中包含 GeneratedKeyProvider。
    /// <para>扫描所有 .cs 文件，找到调用 AddLingFanEngine 的文件，
    /// 如果该文件未注册 GeneratedKeyProvider，则自动注入注册代码。</para>
    /// <para>兼容旧项目（模板更新前创建的项目缺少此注册行）。</para>
    /// </summary>
    private static void EnsureKeyProviderRegistered(string projectDir, string projectName, Action<string> log)
    {
        try
        {
            var securityNamespace = $"{projectName}.Security";
            var csFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories);
            string? patchedFile = null;

            foreach (var csFile in csFiles)
            {
                // 跳过 obj/bin 目录
                if (csFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    csFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;

                var content = File.ReadAllText(csFile);

                // 必须包含 AddLingFanEngine 调用
                if (!content.Contains("AddLingFanEngine")) continue;

                // 已注册 GeneratedKeyProvider → 无需处理
                if (content.Contains("GeneratedKeyProvider")) continue;

                // 注入注册行：在 AddLingFanEngine 之前插入
                var idx = content.IndexOf("AddLingFanEngine");
                if (idx < 0) continue;

                // 向前查找所属行的缩进
                var lineStart = content.LastIndexOf('\n', idx - 1);
                if (lineStart < 0) lineStart = 0;
                var indent = "";
                for (var i = lineStart + 1; i < idx && i < content.Length; i++)
                {
                    if (content[i] == ' ' || content[i] == '\t')
                        indent += content[i];
                    else
                        break;
                }

                // 确保有 using 引用 Security 命名空间 + IEncryptionKeyProvider 命名空间
                var usingsToAdd = new List<string>();
                if (!content.Contains($"using {securityNamespace};"))
                    usingsToAdd.Add($"using {securityNamespace};");
                if (!content.Contains("using LingFanEngine.Abstractions.Interfaces.Core;"))
                    usingsToAdd.Add("using LingFanEngine.Abstractions.Interfaces.Core;");

                if (usingsToAdd.Count > 0)
                {
                    // 在第一个 using 之后插入
                    var firstUsingEnd = content.IndexOf(';');
                    if (firstUsingEnd >= 0)
                    {
                        var lineEnd = content.IndexOf('\n', firstUsingEnd);
                        if (lineEnd < 0) lineEnd = content.Length;
                        var insertBlock = string.Join("\n", usingsToAdd) + "\n";
                        content = content[..(lineEnd + 1)] + insertBlock + content[(lineEnd + 1)..];
                        // 重新定位 AddLingFanEngine
                        idx = content.IndexOf("AddLingFanEngine");
                    }
                }

                // 在 AddLingFanEngine 行之前插入注册行
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
                break; // 只需处理第一个匹配的文件
            }

            if (patchedFile == null)
            {
                // 所有含 AddLingFanEngine 的文件都已注册，或者没找到
                log("  GeneratedKeyProvider 注册检查完成（已存在或无需注入）");
            }
        }
        catch (Exception ex)
        {
            log($"  警告: 自动注入 GeneratedKeyProvider 失败: {ex.Message}");
        }
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
