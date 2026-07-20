# 如何做 Live2D 立绘

Live2D 让 2D 立绘动起来——呼吸、眨眼、表情切换、头部跟随。本章展示灵泛引擎的 Live2D 集成。

## 前置准备

1. 准备 Live2D 模型文件（`.moc3` + 纹理 + 动作文件）
2. 将模型放在 `Live2D/` 目录：

```
Live2D/
├── hero/
│   ├── hero.moc3
│   ├── hero.model3.json
│   ├── hero.physics3.json
│   ├── textures/
│   │   └── hero_texture.png
│   └── motions/
│       ├── idle.motion3.json
│       ├── smile.motion3.json
│       └── sad.motion3.json
```

## 显示 Live2D

```dsl
live2d_char "hero" src="Live2D/hero/hero.model3.json" x=30 y=80 height=600
live2d_show "hero"
```

| 参数 | 说明 |
|:---|:---|
| id | 模型标识 |
| `src` | 模型配置文件路径 |
| `x` / `y` | 位置（数字） |
| `height` | 高度（可选） |
| `fade` | 淡入时长（秒，可选） |

## 表情切换

```dsl
live2d_expr "hero" name="smile"
say "太好了！" speaker="hero"

live2d_expr "hero" name="sad"
say "但是..." speaker="hero"

live2d_expr "hero" name="surprised"
say "什么？！" speaker="hero"
```

## 动作播放

```dsl
live2d_motion "hero" name="wave"
say "你好！" speaker="hero"

live2d_motion "hero" name="nod"
say "我明白了。" speaker="hero"
```

::: tip 表情 vs 动作
- **表情（`live2d_expr`）**——持续的面部表情状态
- **动作（`live2d_motion`）**——一次性的肢体动作

可以同时设置表情和动作。
:::

## 隐藏 Live2D

```dsl
live2d_hide "hero" fade=0.5
```

## 多角色场景

```dsl
live2d_char "hero" src="Live2D/hero/hero.model3.json" x=25 y=80 height=600
live2d_char "heroine" src="Live2D/heroine/heroine.model3.json" x=75 y=80 height=600
live2d_show "hero"
live2d_show "heroine"

live2d_expr "hero" name="smile"
say "你来了。" speaker="勇者"

live2d_expr "heroine" name="shy"
say "嗯..." speaker="女主角"
```

## 配合对话

```dsl
character "hero" name="勇者" color="#FFD700"

label dialog_with_live2d:
  live2d_char "hero" src="Live2D/hero/hero.model3.json" x=30 y=80 height=600
  live2d_show "hero"

  live2d_expr "hero" name="neutral"
  say "你好。" speaker="hero"

  live2d_expr "hero" name="happy"
  live2d_motion "hero" name="wave"
  say "很高兴见到你！" speaker="hero"

  live2d_expr "hero" name="thinking"
  say "让我想想..." speaker="hero"

  live2d_expr "hero" name="surprised"
  say "对了！" speaker="hero"

  live2d_hide "hero"
```

## 参数控制

通过 `live2d_param` 可以直接控制模型的参数（如头部角度）：

```dsl
live2d_param "hero" param="BodyAngleX" value=-8 weight=0.6
live2d_param "hero" param="EyeLOpen" value=0.5
```

## 暂停与恢复

```dsl
live2d_pause "hero"
// 暂停期间模型静止
live2d_resume "hero"
```

## 自动呼吸和眨眼

Live2D 模型默认启用自动呼吸和眨眼（如果模型支持）。无需手动控制。

## 头部跟随

部分模型支持头部跟随鼠标。引擎会自动处理，无需 DSL 控制。

## C# 控制

```csharp
public class Live2DScene : StoryScript
{
    public override string SceneName => "cs_live2d";

    public override async Task RunAsync()
    {
        // 创建并显示 Live2D
        await Pipeline.SendAsync(new Live2DCommand
        {
            Operation = "char",
            Id = "hero",
            Config = new Dictionary<string, object?>
            {
                ["src"] = "Live2D/hero/hero.model3.json",
                ["x"] = 30.0,
                ["y"] = 80.0,
                ["height"] = 600.0
            }
        });

        await Pipeline.SendAsync(new Live2DCommand
        {
            Operation = "show",
            Id = "hero"
        });

        await SayAsync("你好。", speaker: "勇者");

        // 切换表情
        await Pipeline.SendAsync(new Live2DCommand
        {
            Operation = "expr",
            Id = "hero",
            Name = "smile"
        });

        await SayAsync("很高兴见到你！", speaker: "勇者");

        // 隐藏
        await Pipeline.SendAsync(new Live2DCommand
        {
            Operation = "hide",
            Id = "hero",
            Fade = 0.5
        });
    }
}
```

## 性能优化

- **模型数量**——同屏 Live2D 模型建议不超过 2-3 个
- **纹理大小**——纹理建议不超过 2048×2048
- **动作预加载**——频繁切换的动作可以预加载

## 跨平台注意

| 平台 | Live2D 支持 |
|:---|:---|
| Windows | ✅ 完整支持 |
| macOS | ✅ 完整支持 |
| Linux | ✅ 完整支持 |
| Android | ✅ 支持（注意内存） |
| iOS | ✅ 支持（注意内存） |
| Browser | ⚠️ 依赖 WASM 支持 |

::: warning 移动端内存
移动端设备内存有限，建议使用较小尺寸的 Live2D 模型，并及时隐藏不需要的模型。
:::
