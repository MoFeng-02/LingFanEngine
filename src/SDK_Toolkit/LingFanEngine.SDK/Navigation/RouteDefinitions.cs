using System.Collections.Generic;
using LingFanEngine.SDK.Views.Pages;
using MFToolkit.Routing.Entities;

namespace LingFanEngine.SDK.Navigation;

/// <summary>路由注册定义</summary>
public static class RouteDefinitions
{
    /// <summary>获取所有路由实体</summary>
    public static IReadOnlyList<RouteEntity> GetAllRoutes()
    {
        return
        [
            // 项目管理（顶级路由，KeepAlive 缓存）
            new RouteEntity<ProjectPage>("/project") { IsTop = true, IsKeepalive = true },

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
