# 09 · NVL 模式

NVL（Novel）模式让多句对话的文本累积显示在同一个文本框中，适合长段叙事和内心独白。

## NVL vs ADV

| 模式 | 全称 | 特点 | 适用 |
|:---|:---|:---|:---|
| ADV | Advance | 每句对话独立显示，底部对话框 | 角色对话 |
| NVL | Novel | 多句累积显示，全屏文本框 | 长段叙事、旁白、内心独白 |

## 进入 NVL 模式

```dsl
nvl
say "第一句。" speaker="吟游诗人"
say "第二句追加到后面。" speaker="吟游诗人"
say "第三句继续累积。" speaker="吟游诗人"
```

进入 NVL 模式后，`say` 的文本会**逐句追加**到同一个文本框中，打字机只推进新增的部分。

## nvl clear 清空

```dsl
nvl
say "第一段，句一。" speaker="旁白"
say "第一段，句二。" speaker="旁白"
nvl clear
say "第二段开始，旧的文本已清空。" speaker="旁白"
```

`nvl clear` 清空累积文本，但**仍在 NVL 模式中**。新的对话继续累积。

## nvl exit 退出

```dsl
nvl
say "NVL 叙事段落。" speaker="旁白"
say "结束。" speaker="旁白"
nvl exit
say "回到 ADV 模式。" speaker="旁白"   // 底部条对话框
```

`nvl exit` 退出 NVL 模式，恢复 ADV 模式（底部条对话框），并清空累积文本。

## 三态总结

| 命令 | 动作 |
|:---|:---|
| `nvl` | 进入 NVL 模式 |
| `nvl clear` | 清空文本，仍在 NVL 模式 |
| `nvl exit` | 退出 NVL 模式，恢复 ADV |

## NVL 与模板

NVL 模式通常配合 `fullscreen` 模板使用：

```dsl
nvl
say "迷雾笼罩着小镇。" speaker="吟游诗人" template="fullscreen"
say "旅人踏着暮色而来。" speaker="吟游诗人" template="fullscreen"
say "这便是{b}{color=#FFD700}故事{/color}{/b}的开始。" speaker="吟游诗人" template="fullscreen"
nvl exit
```

::: tip NVL 与 fullscreen 的关系
- **NVL**——文本累积模式（逻辑层）
- **fullscreen**——全屏对话框外观（视觉层）

两者可以组合，也可以分开。NVL 模式下用 `bottom` 模板也能累积，只是视觉上不理想。推荐 NVL + fullscreen 组合。
:::

## 累积检测

引擎会自动检测文本是否是"追加"：

- 新文本以旧文本为前缀 → 跳过已有部分，只打字机新增内容
- 新文本完全不同 → 从零开始打字机

```dsl
nvl
// 假设当前累积文本为 "第一句。"
say "第一句。第二句。" speaker="旁白"
// 引擎检测到 "第一句。第二句。" 以 "第一句。" 为前缀
// 只对 "第二句。" 做打字机效果
```

## 场景切换重置

场景切换时，NVL 状态自动重置（累积文本清空，退出 NVL 模式）。

## 完整示例

```dsl
label prologue:
  transition "fade" duration=1.5
  bgm "Media/bgm_main.wav" volume=0.6

  nvl
  say "雨夜。" speaker="narrator" template="fullscreen"
  say "你沿着泥泞的小路前行。" speaker="narrator" template="fullscreen"
  say "远处灯塔的光芒在雨幕中若隐若现。" speaker="narrator" template="fullscreen"
  say "这便是{b}{color=#FFD700}灯塔守望者{/color}{/b}故事的开始。" speaker="narrator" template="fullscreen"
  nvl exit

  transition "fade" duration=1.0
  say "你站在灯塔门前。" speaker="narrator"   // ADV 模式
  navigate "lighthouse_door"
```

## window 对话框控制

`window` 命令控制对话框的显示/隐藏模式：

```dsl
window show    // 强制显示对话框
window hide    // 强制隐藏对话框
window auto    // 自动模式（有文本时显示，无文本时隐藏）
```

适用于"对话框不应该出现"的过场画面：

```dsl
window hide
transition "fade" duration=1.0
bg_switch "Images/mountain.jpg"
// 没有对话框的画面
window auto
say "你来到了山顶。" speaker="旁白"
```

## 动手练习

用 NVL 模式写一段开场叙事：

```dsl
character "poet" name="吟游诗人" color="#88FF88" screen="fullscreen"

label nvl_practice:
  nvl
  say "很久以前，" speaker="poet"
  say "有一座建在悬崖边的灯塔。" speaker="poet"
  say "灯塔守护者为迷途的船只指路，" speaker="poet"
  say "日复一日，年复一年。" speaker="poet"
  say "{p}直到有一天，" speaker="poet"
  say "一个旅人敲响了灯塔的门{w}..." speaker="poet"
  nvl clear
  say "这是那个旅人的故事。" speaker="poet"
  say "也可能是{b}{color=#FFD700}你的{/color}{/b}故事。" speaker="poet"
  nvl exit
  say "故事结束。" speaker="旁白"
```

## 下一步

NVL 模式掌握了！下一章学习[存档与回溯](./10-存档与回溯)——让玩家随时保存和回看。
