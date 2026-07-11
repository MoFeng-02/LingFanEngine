using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 模板服务实现——双模式模板分发
/// <para>开发模式：从 src/Template/V1 目录复制（排除 bin/obj）</para>
/// <para>分发模式：从嵌入资源 template.zip 解压</para>
/// <para>两种模式后续的字符串替换 + 文件重命名逻辑完全一致</para>
/// </summary>
public class TemplateService : ITemplateService
{
    private const string TitlePlaceholder = "{LingFanEngineTemplateTitle}";
    private const string NamespacePlaceholder = "_LingFanEngineTemplateTitle_";
    private const string EmbeddedResourceName = "template.zip";

    // C# 标识符正则：字母/下划线开头，后跟字母/数字/下划线
    private static readonly Regex s_validIdentifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <inheritdoc/>
    public bool IsValidProjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (!s_validIdentifier.IsMatch(name))
            return false;
        // C# 关键字检查
        return !s_csharpKeywords.Contains(name);
    }

    /// <inheritdoc/>
    public string? GetTemplatePath()
    {
        return PathHelper.GetTemplatePath();
    }

    /// <inheritdoc/>
    public async Task CreateProjectFromTemplateAsync(
        string outputDir, string projectName, string projectTitle)
    {
        if (!IsValidProjectName(projectName))
            throw new ArgumentException($"项目名 '{projectName}' 不是合法的 C# 标识符", nameof(projectName));

        var projectDir = Path.Combine(outputDir, projectName);
        PathHelper.EnsureDirectory(projectDir);

        // 尝试开发模式（目录复制），失败则用分发模式（ZIP 解压）
        var templatePath = GetTemplatePath();
        if (templatePath != null && Directory.Exists(templatePath))
        {
            await CopyTemplateDirectoryAsync(templatePath, projectDir);
        }
        else
        {
            ExtractEmbeddedTemplate(projectDir);
        }

        // 替换所有文件中的占位符
        await ReplacePlaceholdersInDirectory(projectDir, projectName, projectTitle);

        // 重命名文件/文件夹中的占位符
        RenamePlaceholdersInDirectory(projectDir, projectName);
    }

    /// <summary>复制模板目录（排除 bin/obj/.vs）</summary>
    private static async Task CopyTemplateDirectoryAsync(string srcDir, string destDir)
    {
        PathHelper.EnsureDirectory(destDir);

        foreach (var file in Directory.GetFiles(srcDir))
        {
            var fileName = Path.GetFileName(file);
            var destPath = Path.Combine(destDir, fileName);
            await Task.Run(() => File.Copy(file, destPath, overwrite: true));
        }

        foreach (var subDir in Directory.GetDirectories(srcDir))
        {
            var subDirName = Path.GetFileName(subDir);
            // 排除构建产物
            if (subDirName is "bin" or "obj" or ".vs" or ".git")
                continue;
            var destSubDir = Path.Combine(destDir, subDirName);
            await CopyTemplateDirectoryAsync(subDir, destSubDir);
        }
    }

    /// <summary>从嵌入资源解压模板 ZIP</summary>
    private static void ExtractEmbeddedTemplate(string destDir)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();
        var zipResourceName = resourceNames.FirstOrDefault(n => n.EndsWith(EmbeddedResourceName, StringComparison.OrdinalIgnoreCase));

        if (zipResourceName == null)
            throw new InvalidOperationException(
                "模板未找到：既无法访问源码目录 src/Template/V1，也未找到嵌入资源 template.zip。" +
                "如果是开发环境，请确保从引擎源码树运行；如果是分发环境，请联系 SDK 提供者。");

        using var stream = assembly.GetManifestResourceStream(zipResourceName)
            ?? throw new InvalidOperationException("无法读取嵌入资源 template.zip");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                // 目录条目
                var dirPath = Path.Combine(destDir, entry.FullName);
                PathHelper.EnsureDirectory(dirPath);
                continue;
            }

            var destPath = Path.Combine(destDir, entry.FullName);
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir))
                PathHelper.EnsureDirectory(dir);

            entry.ExtractToFile(destPath, overwrite: true);
        }
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

    /// <summary>C# 关键字集合（项目名不能使用）</summary>
    private static readonly HashSet<string> s_csharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate",
        "do", "double", "else", "enum", "event", "explicit", "extern", "false",
        "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
        "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
        "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    };
}
