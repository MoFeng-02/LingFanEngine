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
        services.AddSingleton(sp =>
        {
            var registry = sp.GetRequiredService<IDialogTemplateRegistry>();

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
