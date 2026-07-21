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
/// <para>占位符列表：</para>
/// <list type="bullet">
/// <item>{LingFanEngineTemplateTitle} → projectTitle（文件内容）</item>
/// <item>_LingFanEngineTemplateTitle_ → projectName（文件内容 + 文件/目录名）</item>
/// <item>{Version} → version</item>
/// <item>{Description} → description</item>
/// <item>{Authors} → author</item>
/// <item>{Copyright} → "Copyright (c) {year} {author}"</item>
/// </list>
/// </summary>
public class TemplateService : ITemplateService
{
    private readonly IEngineUpdateService _engineUpdateService;
    private readonly ITemplateUpdateService _templateUpdateService;

    public TemplateService(IEngineUpdateService engineUpdateService, ITemplateUpdateService templateUpdateService)
    {
        _engineUpdateService = engineUpdateService;
        _templateUpdateService = templateUpdateService;
    }

    private const string TitlePlaceholder = "{LingFanEngineTemplateTitle}";
    private const string NamespacePlaceholder = "_LingFanEngineTemplateTitle_";
    private const string VersionPlaceholder = "{Version}";
    private const string DescriptionPlaceholder = "{Description}";
    private const string AuthorsPlaceholder = "{Authors}";
    private const string CopyrightPlaceholder = "{Copyright}";
    private const string EmbeddedResourceName = "template.zip";

    /// <summary>需要跳过的文件名（模板残留文件）</summary>
    private static readonly HashSet<string> s_skipFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store",
        "Thumbs.db",
    };

    /// <summary>需要跳过的文件扩展名（模板残留文件）</summary>
    private static readonly HashSet<string> s_skipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp",
        ".bak",
        ".zip",
    };

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
        string outputDir, string projectName, string projectTitle,
        string version = "1.0.0", string author = "", string description = "")
    {
        if (!IsValidProjectName(projectName))
            throw new ArgumentException($"项目名 '{projectName}' 不是合法的 C# 标识符", nameof(projectName));

        // description 默认使用 projectTitle
        if (string.IsNullOrWhiteSpace(description))
            description = projectTitle;

        // copyright 格式
        var copyright = string.IsNullOrWhiteSpace(author)
            ? $"Copyright (c) {DateTime.Now.Year}"
            : $"Copyright (c) {DateTime.Now.Year} {author}";

        var projectDir = Path.Combine(outputDir, projectName);
        PathHelper.EnsureDirectory(projectDir);

        // 尝试开发模式（目录复制），失败则用分发模式（优先下载缓存，否则内置 ZIP 解压）
        var templatePath = GetTemplatePath();
        if (templatePath != null && Directory.Exists(templatePath))
        {
            await CopyTemplateDirectoryAsync(templatePath, projectDir);
        }
        else
        {
            // 分发模式：若已从 Release 下载且版本高于内置，则优先用缓存；否则回退内置嵌入 zip
            var cached = _templateUpdateService?.GetCachedTemplateDir();
            if (cached != null)
            {
                await CopyTemplateDirectoryAsync(cached, projectDir);
            }
            else
            {
                ExtractEmbeddedTemplate(projectDir);
            }
        }

        // 替换所有文件中的占位符
        await ReplacePlaceholdersInDirectory(projectDir, projectName, projectTitle, version, author, description, copyright);

        // 重命名文件/文件夹中的占位符
        RenamePlaceholdersInDirectory(projectDir, projectName);

        // 将 Directory.Build.props.temp 重命名为 Directory.Build.props
        RenameTempPropsFiles(projectDir);

        // 为新建项目播种引擎 DLL（4 个齐全，离线也基于缓存）：DLL/ + engine.lock.json
        await _engineUpdateService.SeedNewProjectEngineAsync(projectDir);
    }

    /// <summary>复制模板目录（排除 bin/obj/.vs 和残留文件）</summary>
    private static async Task CopyTemplateDirectoryAsync(string srcDir, string destDir)
    {
        PathHelper.EnsureDirectory(destDir);

        foreach (var file in Directory.GetFiles(srcDir))
        {
            var fileName = Path.GetFileName(file);

            // 跳过残留文件
            if (s_skipFiles.Contains(fileName))
                continue;
            var ext = Path.GetExtension(fileName);
            if (s_skipExtensions.Contains(ext))
                continue;
            // 跳过 .csproj.Backup.tmp 等
            if (fileName.Contains(".Backup.", StringComparison.OrdinalIgnoreCase))
                continue;

            var destPath = Path.Combine(destDir, fileName);
            await FileHelper.CopyFileAsync(file, destPath);
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

            // 跳过残留文件
            var entryName = entry.Name;
            if (s_skipFiles.Contains(entryName))
                continue;
            var ext = Path.GetExtension(entryName);
            if (s_skipExtensions.Contains(ext))
                continue;
            if (entryName.Contains(".Backup.", StringComparison.OrdinalIgnoreCase))
                continue;
            // 跳过 bin/obj 目录下的文件
            if (entry.FullName.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
                continue;

            var destPath = Path.Combine(destDir, entry.FullName);
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir))
                PathHelper.EnsureDirectory(dir);

            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    /// <summary>替换目录中所有文本文件的占位符</summary>
    private static async Task ReplacePlaceholdersInDirectory(
        string dir, string projectName, string projectTitle,
        string version, string author, string description, string copyright)
    {
        foreach (var file in Directory.GetFiles(dir))
        {
            if (!FileHelper.IsTextFile(file))
                continue;

            var content = await FileHelper.ReadAllTextAsync(file);
            var changed = false;

            if (content.Contains(TitlePlaceholder))
            {
                content = content.Replace(TitlePlaceholder, projectTitle);
                changed = true;
            }
            if (content.Contains(NamespacePlaceholder))
            {
                content = content.Replace(NamespacePlaceholder, projectName);
                changed = true;
            }
            if (content.Contains(VersionPlaceholder))
            {
                content = content.Replace(VersionPlaceholder, version);
                changed = true;
            }
            if (content.Contains(DescriptionPlaceholder))
            {
                content = content.Replace(DescriptionPlaceholder, description);
                changed = true;
            }
            if (content.Contains(AuthorsPlaceholder))
            {
                content = content.Replace(AuthorsPlaceholder, author);
                changed = true;
            }
            if (content.Contains(CopyrightPlaceholder))
            {
                content = content.Replace(CopyrightPlaceholder, copyright);
                changed = true;
            }

            if (changed)
                await FileHelper.WriteAllTextAsync(file, content);
        }

        foreach (var subDir in Directory.GetDirectories(dir))
        {
            await ReplacePlaceholdersInDirectory(subDir, projectName, projectTitle, version, author, description, copyright);
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
            else if (dirName.Contains(NamespacePlaceholder))
            {
                var newName = dirName.Replace(NamespacePlaceholder, projectName);
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
            if (fileName.Contains(TitlePlaceholder) || fileName.Contains(NamespacePlaceholder))
            {
                var newName = fileName.Replace(TitlePlaceholder, projectName)
                                      .Replace(NamespacePlaceholder, projectName);
                var newPath = Path.Combine(dir, newName);
                File.Move(file, newPath);
            }
        }
    }

    /// <summary>将 Directory.Build.props.temp 重命名为 Directory.Build.props</summary>
    private static void RenameTempPropsFiles(string dir)
    {
        var tempFile = Path.Combine(dir, "Directory.Build.props.temp");
        if (File.Exists(tempFile))
        {
            var propsFile = Path.Combine(dir, "Directory.Build.props");
            if (File.Exists(propsFile))
                File.Delete(propsFile);
            File.Move(tempFile, propsFile);
        }

        // 递归处理子目录
        foreach (var subDir in Directory.GetDirectories(dir))
        {
            RenameTempPropsFiles(subDir);
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
