using System.Collections.Generic;
using LingFanEngine.SDK.Views.Pages;
using MFToolkit.Routing.Entities;

namespace LingFanEngine.SDK.Navigation;

/// <summary>
/// 路由注册定义
/// <para>P2-2: WorkspaceWindow 使用 MFToolkit.Routing 导航。</para>
/// <para>每个路由对应一个工作台页面，路由路径用于活动栏导航。</para>
/// </summary>
public static class RouteDefinitions
{
    /// <summary>获取所有路由实体</summary>
    public static IReadOnlyList<RouteEntity> GetAllRoutes()
    {
        return
        [
            // 故事编辑器
            new RouteEntity<StoryEditorPage>("/editor"),

            // 资源管理
            new RouteEntity<AssetManagerPage>("/assets"),

            // 构建发布
            new RouteEntity<BuildPage>("/build"),

            // 设置
            new RouteEntity<SettingsPage>("/settings"),
        ];
    }
}
