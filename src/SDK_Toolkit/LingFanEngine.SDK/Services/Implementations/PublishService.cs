using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LingFanEngine.SDK.Cryptography;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>构建发布服务实现</summary>
public class PublishService : IPublishService
{
    private static readonly string[] s_encryptedExtensions =
        [".story", ".png", ".jpg", ".jpeg", ".mp3", ".ogg", ".wav", ".mp4", ".json"];

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

            // 1. 加密资源
            if (project.Encryption is { Enabled: true })
            {
                Log("正在加密资源...");

                var key = KeyManager.GetOrCreateKey(project.ProjectPath);
                var storiesDir = Path.Combine(projectDir, "Stories");
                var mediaDir = Path.Combine(projectDir, "Media");

                var encryptor = new ResourceEncryptor();

                if (Directory.Exists(storiesDir))
                    await encryptor.EncryptDirectoryAsync(storiesDir, storiesDir + ".enc", key, s_encryptedExtensions);

                if (Directory.Exists(mediaDir))
                    await encryptor.EncryptDirectoryAsync(mediaDir, mediaDir + ".enc", key, s_encryptedExtensions);

                // 2. 生成 GeneratedKeys.cs
                Log("正在生成密钥代码...");
                var namespaceName = $"{project.Name}.Security";
                var keyCode = KeyInjector.GenerateKeyFile(namespaceName, key, project.Encryption.KeyShardCount);
                var keyFilePath = Path.Combine(projectDir, "Security", "GeneratedKeys.cs");
                await FileHelper.WriteAllTextAsync(keyFilePath, keyCode);

                Log("密钥代码已生成");
            }

            // 3. 调用 dotnet publish
            Log($"正在执行 dotnet publish (RID: {platform.RuntimeIdentifier})...");

            var outputPath = Path.Combine(projectDir, project.Build.OutputPath, platform.RuntimeIdentifier);
            var publishArgs = $"publish \"{projectDir}\" -c {project.Build.Configuration} -r {platform.RuntimeIdentifier}";

            if (project.Build.SelfContained)
                publishArgs += " --self-contained";
            if (!string.IsNullOrEmpty(project.Build.TrimMode))
                publishArgs += $" -p:TrimMode={project.Build.TrimMode}";
            if (project.Build.PublishAot && platform.SupportsAot)
                publishArgs += " -p:PublishAot=true";

            publishArgs += $" -o \"{outputPath}\"";

            Log($"执行: dotnet {publishArgs}");

            var exitCode = await ProcessHelper.RunDotNetAsync(publishArgs, projectDir, progress);
            if (exitCode != 0)
            {
                result.Success = false;
                result.ErrorMessage = $"dotnet publish 失败，退出码: {exitCode}";
                Log($"构建失败！退出码: {exitCode}");
                return result;
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
}
