using LingFanEngine.Extensions;
using LingFanEngine.Views;
using LingFanEngine.Entry.UI.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace LingFanEngine.Entry.Extensions;

public static class ServiceExtensions
{
    /// <summary>注册对话框模板到 IDialogTemplateRegistry</summary>
    public static IServiceCollection AddDialogTemplates(this IServiceCollection services)
    {
        services.AddSingleton<IDialogTemplateRegistry>(sp =>
        {
            // 注意：绝不能 sp.GetRequiredService<IDialogTemplateRegistry>() —— 本工厂自身就是
            // IDialogTemplateRegistry 的最后一个注册项，自我解析会无限递归直到 StackOverflow。
            // 直接 new 出具体类型，由其作为单例缓存返回。引擎层 TryAddSingleton 仍作为无 UI 模板时的兜底。
            var registry = new DialogTemplateRegistry();

            // 底部条状（默认）——引擎层 DialogBox
            registry.Register("bottom", new DefaultDialogBoxFactory());

            // 中央气泡——UI 层自定义控件
            registry.Register("center", new CenterBubbleDialogBoxFactory());

            // 全屏 NVL——UI 层自定义控件
            registry.Register("fullscreen", new FullScreenNvlDialogBoxFactory());

            registry.SetDefault("bottom");
            return registry;
        });
        return services;
    }
}
