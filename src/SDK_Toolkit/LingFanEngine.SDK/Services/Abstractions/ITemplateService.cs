using System.Threading.Tasks;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>模板服务</summary>
public interface ITemplateService
{
    /// <summary>从模板创建项目</summary>
    Task CreateProjectFromTemplateAsync(string templatePath, string outputDir, string projectName, string projectTitle);

    /// <summary>获取模板路径</summary>
    string? GetTemplatePath();
}
