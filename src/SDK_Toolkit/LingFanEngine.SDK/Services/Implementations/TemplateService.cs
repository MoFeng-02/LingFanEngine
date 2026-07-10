using System.IO;
using System.Threading.Tasks;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>模板服务实现</summary>
public class TemplateService : ITemplateService
{
    private const string TitlePlaceholder = "{LingFanEngineTemplateTitle}";
    private const string NamespacePlaceholder = "_LingFanEngineTemplateTitle_";

    /// <inheritdoc/>
    public string? GetTemplatePath()
    {
        return PathHelper.GetTemplatePath();
    }

    /// <inheritdoc/>
    public async Task CreateProjectFromTemplateAsync(
        string templatePath, string outputDir, string projectName, string projectTitle)
    {
        // 复制模板目录
        await FileHelper.CopyDirectoryAsync(templatePath, outputDir);

        // 替换所有文件中的占位符
        await ReplacePlaceholdersInDirectory(outputDir, projectName, projectTitle);

        // 重命名文件/文件夹中的占位符
        RenamePlaceholdersInDirectory(outputDir, projectName);
    }

    /// <summary>替换目录中所有文本文件的占位符</summary>
    private static async Task ReplacePlaceholdersInDirectory(
        string dir, string projectName, string projectTitle)
    {
        foreach (var file in Directory.GetFiles(dir))
        {
            if (!FileHelper.IsTextFile(file))
                continue;

            var content = await FileHelper.ReadAllTextAsync(file);
            content = content.Replace(TitlePlaceholder, projectTitle);
            content = content.Replace(NamespacePlaceholder, projectName);
            await FileHelper.WriteAllTextAsync(file, content);
        }

        foreach (var subDir in Directory.GetDirectories(dir))
        {
            await ReplacePlaceholdersInDirectory(subDir, projectName, projectTitle);
        }
    }

    /// <summary>重命名文件/文件夹中的占位符</summary>
    private static void RenamePlaceholdersInDirectory(string dir, string projectName)
    {
        // 重命名子目录
        foreach (var subDir in Directory.GetDirectories(dir))
        {
            var dirName = Path.GetFileName(subDir);
            if (dirName.Contains(TitlePlaceholder))
            {
                var newName = dirName.Replace(TitlePlaceholder, projectName);
                var newPath = Path.Combine(dir, newName);
                Directory.Move(subDir, newPath);
                RenamePlaceholdersInDirectory(newPath, projectName);
            }
            else
            {
                RenamePlaceholdersInDirectory(subDir, projectName);
            }
        }

        // 重命名文件
        foreach (var file in Directory.GetFiles(dir))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Contains(TitlePlaceholder))
            {
                var newName = fileName.Replace(TitlePlaceholder, projectName);
                var newPath = Path.Combine(dir, newName);
                File.Move(file, newPath);
            }
        }
    }
}
