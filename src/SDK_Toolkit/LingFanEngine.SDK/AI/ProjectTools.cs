using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Constants;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.AI;

/// <summary>
/// 项目级 Agent 工具（5.4）——供 Agent 经 tool calling 调用的通用工具：
/// <list type="bullet">
/// <item><c>build.check</c>：只读，验证构建前置（引擎 DLL / Resources 目录），返回 JSON。</item>
/// <item><c>asset.organize</c>：产出媒体归类计划（不执行；执行/审批宿主为二期集成）。</item>
/// <item><c>rename_batch</c>：批量重命名计划（不执行；执行/审批宿主为二期集成）。</item>
/// </list>
/// </summary>
public static class ProjectTools
{
    /// <summary>构建项目级工具集。</summary>
    public static IReadOnlyList<AgentTool> Build() =>
    [
        new AgentTool
        {
            Name = "build.check",
            Description = "只读：验证项目构建前置——引擎 DLL(目录) 与 Resources 目录是否存在。入参 {projectDir}。返回 JSON {\"ok\":bool,\"missing\":[...]}。",
            ParametersJson = "{\"type\":\"object\",\"properties\":{\"projectDir\":{\"type\":\"string\"}},\"required\":[\"projectDir\"]}",
            Execute = (args, _) => BuildCheck(args),
        },
        new AgentTool
        {
            Name = "asset.organize",
            Description = "产出媒体归类计划：把 Resources 下散落的图片/音频/视频归入 Media/{Images,Audio,Video}（仅规划不执行）。",
            ParametersJson = "{\"type\":\"object\",\"properties\":{\"projectDir\":{\"type\":\"string\"}},\"required\":[\"projectDir\"]}",
            Execute = (args, _) => OrganizePlan(args),
        },
        new AgentTool
        {
            Name = "rename_batch",
            Description = "批量重命名资源计划。入参 {projectDir, items:[{old,new}]}，old/new 为相对项目根的路径（仅规划不执行）。",
            ParametersJson = "{\"type\":\"object\",\"properties\":{\"projectDir\":{\"type\":\"string\"},\"items\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"old\":{\"type\":\"string\"},\"new\":{\"type\":\"string\"}}}}},\"required\":[\"projectDir\",\"items\"]}",
            Execute = (args, _) => RenamePlan(args),
        },
    ];

    private static Task<string> BuildCheck(JsonElement args)
    {
        var projectDir = GetString(args, "projectDir");
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            return Task.FromResult("{\"ok\":false,\"missing\":[\"项目目录不存在\"]}");

        var missing = new List<string>();
        // 引擎经 NuGet 分发：检查项目 csproj 内 LingFanEngine PackageReference 是否存在
        if (EnginePackageHelper.GetEnginePackageVersion(projectDir) == null)
            missing.Add("PackageReference: LingFanEngine");

        var resourcesDir = Path.Combine(projectDir, ProjectConstants.ResourcesDir);
        if (!Directory.Exists(resourcesDir))
            missing.Add($"{ProjectConstants.ResourcesDir}/");

        return Task.FromResult(missing.Count == 0
            ? "{\"ok\":true,\"missing\":[]}"
            : "{\"ok\":false,\"missing\":[" + string.Join(',', missing.Select(Miss)) + "]}");
    }

    private static Task<string> OrganizePlan(JsonElement args)
    {
        var projectDir = GetString(args, "projectDir");
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            return Task.FromResult("{\"error\":\"项目目录不存在\"}");

        var resourcesDir = Path.Combine(projectDir, ProjectConstants.ResourcesDir);
        var mediaDir = Path.Combine(resourcesDir, "Media");
        var plan = new List<string>();

        foreach (var (sub, exts) in new[]
        {
            ("Images", ProjectConstants.ImageExtensions),
            ("Audio", ProjectConstants.AudioExtensions),
            ("Video", ProjectConstants.VideoExtensions),
        })
        {
            var targetDir = Path.Combine(mediaDir, sub);
            var loose = Directory.GetFiles(resourcesDir).Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
            if (loose.Count > 0)
                plan.Add($"{loose.Count} 个文件 → Media/{sub}/");
        }

        return plan.Count == 0
            ? Task.FromResult("{\"plan\":\"无需归类：Resources 根目录没有散落媒体文件\"}")
            : Task.FromResult("{\"plan\":\"" + string.Join(", ", plan) + "\"}");
    }

    private static Task<string> RenamePlan(JsonElement args)
    {
        var projectDir = GetString(args, "projectDir");
        var itemsOk = args.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array;
        if (string.IsNullOrWhiteSpace(projectDir) || !itemsOk)
            return Task.FromResult("{\"error\":\"参数缺失：projectDir + items\"}");

        var count = 0;
        var same = 0;
        foreach (var item in items.EnumerateArray())
        {
            var oldPath = GetString(item, "old");
            var newPath = GetString(item, "new");
            if (string.IsNullOrWhiteSpace(oldPath)) continue;
            count++;
            if (string.Equals(oldPath, newPath, StringComparison.Ordinal)) same++;
        }

        return Task.FromResult("{\"plan\":\"" + count + " 个重命名（其中 " + same + " 个为无变更）\"}");
    }

    private static string GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Miss(string s)
        => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"";
}