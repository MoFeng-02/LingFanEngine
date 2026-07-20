using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Abstractions.Interfaces.Media;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;

namespace _LingFanEngineTemplateTitle_;

/// <summary>
/// 启动层入口 — 注册 C# 命令
/// <para>引擎层不做游戏逻辑，所有游戏级配置在此定义</para>
/// <para>DSL button cmd="xxx" 在此注册对应的 C# 处理逻辑</para>
/// </summary>
public static class CSharpScripts
{
    public static void RegisterAll(IStateContainer state, ISceneRegistry sceneRegistry,
        IGameController ctrl, ICommandPipeline pipeline, ICommandService? cmdService,
        IAudioManager? audio = null,
        GameLoop? gameLoop = null,
        UI.OverlayManager? overlay = null,
        ISaveService? saveService = null)
    {
        // ===== 命令注册（DSL .story 中的 cmd="xxx" 依赖这些 C# 命令）=====
        if (cmdService == null) return;

        // ========== Overlay UI 桥接命令 ==========

        cmdService.RegisterCommand("open_save", (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => overlay?.ShowSavePanel());
            return Task.CompletedTask;
        });
        cmdService.RegisterCommand("open_load", (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => overlay?.ShowLoadPanel());
            return Task.CompletedTask;
        });
        cmdService.RegisterCommand("open_settings", (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => overlay?.ShowSettingsPanel());
            return Task.CompletedTask;
        });
        cmdService.RegisterCommand("open_gallery", (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => overlay?.ShowGalleryPanel());
            return Task.CompletedTask;
        });
        cmdService.RegisterCommand("open_history", (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => overlay?.ShowHistoryPanel());
            return Task.CompletedTask;
        });

        // 继续游戏（从 quick_save 读取）
        cmdService.RegisterCommand("continue_game", async (_, _) =>
        {
            if (saveService != null)
            {
                var exists = await saveService.ExistsAsync("quick_save");
                if (exists)
                {
                    ctrl.Load("quick_save");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => overlay?.HideAll());
                }
                else
                {
                    state.Set(StateKeys.Notify.Text, "没有找到快速存档");
                    state.Set(StateKeys.Notify.Type, "warning");
                }
            }
            else
            {
                state.Set(StateKeys.Notify.Text, "存档服务不可用");
                state.Set(StateKeys.Notify.Type, "error");
            }
        });

        // 返回标题
        cmdService.RegisterCommand("return_title", (_, _) =>
        {
            ctrl.Navigate("back_title");
            return Task.CompletedTask;
        });

        // 退出游戏
        cmdService.RegisterCommand("do_exit", (_, _) =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime)
                Environment.Exit(0);
            return Task.CompletedTask;
        });

        // 快速存档/读档
        cmdService.RegisterCommand("do_save", (_, _) =>
        {
            pipeline.SendAsync(new SaveLoadCommand { SlotId = "quick_save", IsSave = true });
            return Task.CompletedTask;
        });
        cmdService.RegisterCommand("do_load", (_, _) =>
        {
            pipeline.SendAsync(new SaveLoadCommand { SlotId = "quick_save", IsSave = false });
            return Task.CompletedTask;
        });

        // ========== 通知命令（DSL 可调用）==========

        cmdService.RegisterCommand("notify_info", (arg, _) =>
        {
            var msg = arg?.ToString() ?? "";
            state.Set(StateKeys.Notify.Text, msg);
            state.Set(StateKeys.Notify.Type, "info");
            return Task.CompletedTask;
        });
    }
}
