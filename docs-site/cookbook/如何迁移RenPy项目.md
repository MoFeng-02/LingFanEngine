# 如何迁移 Ren'Py 项目

本章为从 Ren'Py 迁移到灵泛引擎的开发者提供对照参考。

## 核心概念对照

| 概念 | Ren'Py | 灵泛引擎 |
|:---|:---|:---|
| 脚本文件 | `.rpy` | `.story` |
| 标签 | `label name:` | `label name:` |
| 跳转 | `jump label` | `navigate "label"` 或 `jump "label"` |
| 对话 | `character "text"` | `say "text" speaker="name"` |
| 菜单 | `menu:` + 缩进选项 | `menu "prompt"` + `->` 选项 |
| 变量 | Python 变量 | `define` / `set` + `{}` 表达式 |
| 场景 | `scene` + `show` | `scene` + `image` / `bg_switch` |
| 过渡 | `with fade` | `transition "fade"` |
| 音频 | `play music` | `bgm "path"` |
| 存档 | 自动 | `save "slot"` / 自动 |

## 对话迁移

### Ren'Py

```renpy
define e = Character("Eileen", color="#3333ff")

label start:
    e "你好，世界。"
    e "这是对话。"
```

### 灵泛引擎

```dsl
character "eileen" name="Eileen" color="#3333ff"

label start:
  say "你好，世界。" speaker="eileen"
  say "这是对话。" speaker="eileen"
```

## 菜单迁移

### Ren'Py

```renpy
menu:
    "选项 A":
        jump choice_a
    "选项 B":
        jump choice_b
```

### 灵泛引擎

```dsl
menu
  "选项 A" -> choice_a
  "选项 B" -> choice_b
```

## 场景和图片迁移

### Ren'Py

```renpy
scene bg classroom
show eileen happy at left
with fade
```

### 灵泛引擎

```dsl
bg_switch "Images/bg_classroom.jpg" transition=fade duration=1.0
sprite "eileen" src="Images/eileen_happy.png" x=25 y=70
```

## 变量迁移

### Ren'Py

```renpy
$ points = 0
$ points += 10
if points >= 20:
    jump good_ending
```

### 灵泛引擎

```dsl
define "player.points" 0 once
set "player.points" {player.points + 10}
if {player.points >= 20}
  navigate "good_ending"
```

## 音频迁移

### Ren'Py

```renpy
play music "audio/bgm.mp3" fadeout 1.0
play sound "audio/click.wav"
stop music
```

### 灵泛引擎

```dsl
bgm "Media/bgm.mp3" volume=0.7
se "Media/click.wav"
// 停止 BGM 需用 C# API: gameController.StopBgm()
```

## 过渡迁移

### Ren'Py

```renpy
scene bg night
with dissolve

show eileen sad
with fade
```

### 灵泛引擎

```dsl
bg_switch "Images/bg_night.jpg" transition=dissolve duration=1.0
sprite "eileen" src="Images/eileen_sad.png" x=50 y=70
transition "fade" duration=0.5
```

## NVL 模式迁移

### Ren'Py

```renpy
define n = Character(None, kind=nvl)

n "第一句。"
n "第二句。"
n "第三句。"

nvl clear
```

### 灵泛引擎

```dsl
character "n" name="旁白" color="#AAAAAA" screen="fullscreen"

nvl
say "第一句。" speaker="n" template="fullscreen"
say "第二句。" speaker="n" template="fullscreen"
say "第三句。" speaker="n" template="fullscreen"
nvl clear
```

## 内联标记迁移

### Ren'Py

```renpy
e "这是 {b}粗体{/b}，{i}斜体{/i}，{color=#ff0000}红色{/color}。"
```

### 灵泛引擎

```dsl
say "这是 {b}粗体{/b}，{i}斜体{/i}，{color=#ff0000}红色{/color}。" speaker="eileen"
```

::: tip 标记语法几乎相同
Ren'Py 和灵泛引擎的内联标记语法非常相似，大部分可以直接复制。
:::

## 群组对话

### Ren'Py

```renpy
menu:
    "你好":
        e "你好！"
    "再见":
        e "再见！"
```

### 灵泛引擎

```dsl
menu "你要说什么？"
  "你好" -> say_hello
  "再见" -> say_bye

label say_hello:
  say "你好！" speaker="eileen"
  navigate "next"

label say_bye:
  say "再见！" speaker="eileen"
  navigate "next"
```

## 屏幕迁移

### Ren'Py

```renpy
screen main_menu:
    textbutton "开始" action Start()
    textbutton "退出" action Quit()
```

### 灵泛引擎

```dsl
scene "title_main" type=menu
  button "开始" x=50% y=45% nav="chapter1" halign=center
  button "退出" x=50% y=60% cmd="do_exit" halign=center
```

## Python 逻辑迁移

### Ren'Py（Python）

```renpy
init python:
    def calculate_damage(attacker, defender):
        return max(1, attacker.attack - defender.defense)
```

### 灵泛引擎（C#）

```csharp
public static class BattleHelper
{
    public static int CalculateDamage(int attack, int defense)
        => Math.Max(1, attack - defense);
}
```

## 迁移建议

1. **文本优先**——先把对话文本迁移过来，再调整语法
2. **变量统一**——把 Python 变量统一改为 `define` / `set` 模式
3. **标签保留**——`label` 名字可以直接保留，减少跳转修改
4. **图片路径**——调整图片路径格式
5. **音频路径**——调整音频路径格式
6. **复杂逻辑**——Python 复杂逻辑用 C# 重写

## 不需要迁移的

- **存档系统**——灵泛引擎内置，不需要 Ren'Py 的存档槽配置
- **回溯系统**——灵泛引擎内置滚轮回溯
- **历史记录**——灵泛引擎自动记录
- **自动模式 / Skip**——灵泛引擎内置

## 迁移检查清单

- [ ] `.rpy` → `.story` 文件转换
- [ ] `label` 保留，跳转改为 `navigate`
- [ ] `Character` → `character`
- [ ] 对话改为 `say "..." speaker="..."`
- [ ] `menu` 选项改为 `"..." -> label`
- [ ] `scene` + `show` → `bg_switch` + `sprite`
- [ ] `play music` → `bgm`
- [ ] `play sound` → `se`
- [ ] Python 变量 → `define` / `set`
- [ ] Python 逻辑 → C# 场景
- [ ] `screen` → `scene type=menu` + UI 元素
- [ ] 内联标记（大部分可直接复制）
