using System.Threading.Tasks;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>模板服务</summary>
public interface ITemplateService
{
    /// <summary>从模板创建项目（自动选择开发模式或分发模式）</summary>
    /// <param name="outputDir">输出目录（项目将创建在此目录下，子目录名=projectName）</param>
    /// <param name="projectName">项目名（合法 C# 标识符，用于命名空间）</param>
    /// <param name="projectTitle">项目显示标题</param>
    Task CreateProjectFromTemplateAsync(string outputDir, string projectName, string projectTitle);

    /// <summary>获取模板路径（开发模式返回目录路径，分发模式返回 null）</summary>
    string? GetTemplatePath();

    /// <summary>验证项目名是否合法（C# 标识符规则）</summary>
    bool IsValidProjectName(string name);
}
