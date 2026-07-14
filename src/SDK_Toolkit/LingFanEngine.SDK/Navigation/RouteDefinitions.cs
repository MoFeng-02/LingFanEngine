using System.Collections.Generic;
using LingFanEngine.SDK.ViewModels;
using LingFanEngine.SDK.Views.Pages;
using MFToolkit.Routing.Entities;

namespace LingFanEngine.SDK.Navigation;

/// <summary>
/// 路由注册定义。
/// <para>WorkspaceWindow 通过 IRouter.NavigateAsync 导航，订阅 Navigated 事件切换 UI。</para>
/// <para>每个路由对应一个工作台页面，IsKeepalive=true 保持页面缓存。</para>
/// <para>使用泛型 RouteEntity&lt;TPage, TViewModel&gt; 指定页面和 ViewModel 类型，Router 自动创建两者实例。</para>
/// </summary>
public static class RouteDefinitions
{
    /// <summary>获取所有路由实体</summary>
    public static IReadOnlyList<RouteEntity> GetAllRoutes()
    {
        return
        [
            // 故事编辑器（KeepAlive 缓存，切换活动页时保留状态）
            new RouteEntity<StoryEditorPage, StoryEditorViewModel>("/editor")
            {
                IsKeepalive = true,
            },

            // 资源管理
            new RouteEntity<AssetManagerPage, AssetManagerViewModel>("/assets")
            {
                IsKeepalive = true,
            },

            // 构建发布
            new RouteEntity<BuildPage, BuildViewModel>("/build")
            {
                IsKeepalive = true,
            },

            // 设置
            new RouteEntity<SettingsPage, SettingsViewModel>("/settings")
            {
                IsKeepalive = true,
            },
        ];
    }
}
