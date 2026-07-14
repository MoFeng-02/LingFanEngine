using System.Threading.Tasks;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>模板服务</summary>
public interface ITemplateService
{
    /// <summary>从模板创建项目（自动选择开发模式或分发模式）</summary>
    /// <param name="outputDir">输出目录（项目将创建在此目录下，子目录名=项目名称）</param>
    /// <param name="projectName">项目名称（合法 C# 标识符，用于命名空间）</param>
    /// <param name="projectTitle">游戏名称（面向玩家的显示标题）</param>
    /// <param name="version">版本号（如 "1.0.0"）</param>
    /// <param name="author">作者</param>
    /// <param name="description">项目描述（可选，默认使用 projectTitle）</param>
    Task CreateProjectFromTemplateAsync(
        string outputDir, string projectName, string projectTitle,
        string version = "1.0.0", string author = "", string description = "");

    /// <summary>获取模板路径（开发模式返回目录路径，分发模式返回 null）</summary>
    string? GetTemplatePath();

    /// <summary>验证项目名是否合法（C# 标识符规则）</summary>
    bool IsValidProjectName(string name);
}
