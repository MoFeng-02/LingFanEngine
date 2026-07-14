using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 场景状态回放辅助器——从命令列表回放重建场景元素和运行时元素
/// <para>用于存档/读档恢复和 Menu→Game 返回时重建场景视觉状态。</para>
/// <para>处理 ShowElementCommand（场景元素）、ShowHideCommand（背景/图片）、
/// BgSwitchCommand（背景切换）、SpriteCommand（立绘）、Live2DCommand（Live2D 角色）五类命令。</para>
/// </summary>
internal static class SceneReplayHelper
{
    /// <summary>
    /// 回放命令列表 [0, upToIndex) 区间的场景构建命令，重建场景元素和运行时元素
    /// </summary>
    /// <param name="commands">DSL 编译后的命令列表</param>
    /// <param name="upToIndex">回放到此索引（不含）</param>
    /// <param name="state">状态容器（写入 Scene.Elements / Scene.RuntimeElements / Scene.CurrentBackground）</param>
    /// <returns>回放的场景元素数量（用于日志）</returns>
    public static int ReplaySceneState(IReadOnlyList<ICommand> commands, int upToIndex, IStateContainer state)
    {
        var sceneElements = new List<UIElementEntity>();
        var runtimeElements = new List<UIElementEntity>();
        string? currentBg = null;

        var limit = Math.Min(upToIndex, commands.Count);
        for (int i = 0; i < limit; i++)
        {
            var cmd = commands[i];

            switch (cmd)
            {
                // 场景定义元素（scene 块内的 image/text/button 等）
                case ShowElementCommand se:
                    sceneElements.Add(se.Element);
                    break;

                // background 命令——设置背景（替换旧背景）
                case ShowHideCommand sh when sh.IsBackground && sh.IsShow:
                    currentBg = sh.Target;
                    // 移除旧背景元素，添加新的（与 ShowHideHandler 行为一致）
                    runtimeElements.RemoveAll(e => e.ElementType == "background");
                    runtimeElements.Add(new UIElementEntity
                    {
                        ElementType = "background",
                        Properties = new Dictionary<string, object>
                        {
                            ["source"] = sh.Target,
                            ["x"] = sh.X,
                            ["y"] = sh.Y,
                            [StateKeys.UiTags.Tag] = sh.Tag ?? "background"
                        },
                        Order = -1000
                    });
                    break;

                // show 命令（非背景）——添加图片到运行时元素
                case ShowHideCommand sh2 when sh2.IsShow && !sh2.IsBackground:
                    runtimeElements.Add(new UIElementEntity
                    {
                        ElementType = "image",
                        Properties = new Dictionary<string, object>
                        {
                            ["source"] = sh2.Target,
                            ["x"] = sh2.X,
                            ["y"] = sh2.Y,
                            [StateKeys.UiTags.Tag] = sh2.Tag ?? ""
                        },
                        Order = runtimeElements.Count
                    });
                    break;

                // hide 命令——从运行时元素移除匹配的图片
                case ShowHideCommand sh3 when !sh3.IsShow:
                    runtimeElements.RemoveAll(e =>
                        (e.Properties.TryGetValue("source", out var src) && src?.ToString() == sh3.Target) ||
                        (!string.IsNullOrEmpty(sh3.Tag) &&
                         e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) &&
                         t?.ToString() == sh3.Tag));
                    break;

                // bg_switch 命令——切换背景（替换旧背景）
                case BgSwitchCommand bs:
                    currentBg = bs.Path;
                    runtimeElements.RemoveAll(e => e.ElementType == "background");
                    runtimeElements.Add(new UIElementEntity
                    {
                        ElementType = "background",
                        Properties = new Dictionary<string, object>
                        {
                            ["source"] = bs.Path,
                            ["x"] = 0.0,
                            ["y"] = 0.0,
                            [StateKeys.UiTags.Tag] = "background"
                        },
                        Order = -1000
                    });
                    break;

                // sprite show 命令——添加立绘到运行时元素
                case SpriteCommand sp when sp.Operation == "show":
                    // 移除同 ID 旧立绘（与 SpriteHandler 行为一致）
                    runtimeElements.RemoveAll(e =>
                        e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) &&
                        t?.ToString() == sp.Id);
                    runtimeElements.Add(new UIElementEntity
                    {
                        ElementType = "image",
                        Properties = new Dictionary<string, object>
                        {
                            ["source"] = sp.Source ?? "",
                            ["x"] = sp.X ?? 0,
                            ["y"] = sp.Y ?? 0,
                            [StateKeys.UiTags.Tag] = sp.Id
                        },
                        Order = runtimeElements.Count
                    });
                    break;

                // sprite hide 命令——移除指定 ID 的立绘
                case SpriteCommand spHide when spHide.Operation == "hide":
                    runtimeElements.RemoveAll(e =>
                        e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) &&
                        t?.ToString() == spHide.Id);
                    break;

                // sprite move 命令——更新立绘位置
                case SpriteCommand spMove when spMove.Operation == "move":
                    for (int j = 0; j < runtimeElements.Count; j++)
                    {
                        var e = runtimeElements[j];
                        if (e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) &&
                            t?.ToString() == spMove.Id)
                        {
                            var newProps = new Dictionary<string, object>(e.Properties);
                            if (spMove.X.HasValue) newProps["x"] = spMove.X.Value;
                            if (spMove.Y.HasValue) newProps["y"] = spMove.Y.Value;
                            runtimeElements[j] = new UIElementEntity
                            {
                                Id = e.Id,
                                ElementType = e.ElementType,
                                InCustom = e.InCustom,
                                CustomElement = e.CustomElement,
                                Properties = newProps,
                                Children = e.Children,
                                Order = e.Order,
                                Command = e.Command,
                                CommandValue = e.CommandValue
                            };
                        }
                    }
                    break;

                // sprite state 命令——更新立绘表情
                case SpriteCommand spState when spState.Operation == "state":
                    for (int j = 0; j < runtimeElements.Count; j++)
                    {
                        var e = runtimeElements[j];
                        if (e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) &&
                            t?.ToString() == spState.Id)
                        {
                            var newProps = new Dictionary<string, object>(e.Properties);
                            if (!string.IsNullOrEmpty(spState.Emotion))
                                newProps["source"] = spState.Emotion;
                            runtimeElements[j] = new UIElementEntity
                            {
                                Id = e.Id,
                                ElementType = e.ElementType,
                                InCustom = e.InCustom,
                                CustomElement = e.CustomElement,
                                Properties = newProps,
                                Children = e.Children,
                                Order = e.Order,
                                Command = e.Command,
                                CommandValue = e.CommandValue
                            };
                        }
                    }
                    break;

                // live2d show 命令——添加 Live2D 角色到运行时元素（与 Live2DHandler 行为一致）
                case Live2DCommand l2d when l2d.Operation == "show":
                    runtimeElements.RemoveAll(e =>
                        e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) &&
                        t?.ToString() == l2d.Id);
                    runtimeElements.Add(new UIElementEntity
                    {
                        ElementType = "Live2D",
                        Properties = new Dictionary<string, object>
                        {
                            ["modelId"] = l2d.Id,
                            [StateKeys.UiTags.Tag] = l2d.Id
                        },
                        Order = runtimeElements.Count
                    });
                    break;

                // live2d hide 命令——移除指定 ID 的 Live2D 元素
                case Live2DCommand l2dHide when l2dHide.Operation == "hide":
                    runtimeElements.RemoveAll(e =>
                        e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) &&
                        t?.ToString() == l2dHide.Id);
                    break;
            }
        }

        // 写入状态容器
        state.Set(StateKeys.Scene.Elements, sceneElements);
        state.Set(StateKeys.Scene.RuntimeElements, runtimeElements);
        if (currentBg != null)
            state.Set(StateKeys.Scene.CurrentBackground, currentBg);
        state.Set(StateKeys.Scene.Dirty, true);

        return sceneElements.Count;
    }
}
