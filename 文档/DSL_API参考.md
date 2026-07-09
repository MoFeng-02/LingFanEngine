# 灵泛引擎 DSL API 参考

> 更新日期：2026-07-09 | 基于 LingFanDslEngine 当前实现（含 Phase 15-27 全部语法）

---

## 目录

1. [文件格式与目录结构](#一文件格式与目录结构)
2. [变量定义（define / let）](#二变量定义define--let)
3. [角色定义（character）](#三角色定义character)
4. [场景块（scene block）](#四场景块scene-block)
5. [流程命令](#五流程命令)
6. [控制流](#六控制流)
7. [变量表达式](#七变量表达式)
8. [内联标记（Inline Markup）](#八内联标记inline-markup)
9. [通用交互属性](#九通用交互属性)
10. [变量命名约定](#十变量命名约定)
11. [回溯与前进](#十一回溯与前进)
12. [视频命令](#十二视频命令)
13. [DSL ↔ C# 命令映射表](#十三dsl--c-命令映射表)
14. [编译错误信息](#十四编译错误信息)
15. [完整示例](#十五完整示例)

---

## 一、文件格式与目录结构

### 1.1 纯 DSL 格式（推荐）

文件扩展名 `.story`，UTF-8 编码：

```
// 注释（// 或 # 开头）
define "player.gold" 100 once

scene "title_main"
  text "标题" at (640, 120) size=36 color="#FFD700" align=center
  button "开始" at (640, 300) size=(200, 42) color="#88CCFF" nav="chapter1"

label start:
  transition "fade" duration=0.5
  say "欢迎来到灵泛引擎。" speaker="旁白"
  navigate "chapter1"
```

### 1.2 JSON 格式

```json
{
  "id": "chapter3",
  "title": "第三章",
  "lang": "zh-CN",
  "defines": {
    "npc.merchant.name": "老张",
    "npc.merchant.favorability": 0
  },
  "script": "scene \"chapter3_meet\"\n  text \"相遇\" at (100, 150)\n  ..."
}
```

### 1.3 目录结构

```
Stories/
├── title/
│   └── title_main.story              # 入口
├── chapter1/
│   ├── chapter1_intro.story
│   └── path.story
├── chapter2/
│   └── battle.story
└── system/
    └── sandbox.story
```

`StoryRegistry.Scan()` 启动时扫描所有子目录，建立 "场景名 → 文件路径" 映射。实际文件在第一次 `navigate` / `scene` 指令时才懒加载编译。支持热重载（`ReloadFile` / `ReloadAll`）。

---

## 二、变量定义（define / let）

### define — 全局变量定义

```
define "key" value once
```

- `once`：只在变量不存在时设置（全局一次性）
- 支持类型：数字、布尔、字符串、JSON 对象/数组
- 启动时由 `RegisterAllDefines()` 统一注册，不依赖 DSL 执行器

```
define "player.name" "旅人" once
define "player.gold" 100 once
define "player.hp" 50 once
define "npc.merchant" { "name": "老张", "favorability": 0 } once
```

### let — 局部变量定义

```
let "key" value once
```

- 语法同 `define`，但键自动加 `_local_` 前缀
- 不参与存档，场景切换时自动清除

```
let "temp_counter" 0 once
let "dialog_state" "intro" once
```

---

## 三、角色定义（character）

### character — 定义角色对话样式

```
character "key" name="显示名" color="#FF4444" font="SimHei" text_color="#FFFFFF" text_font="KaiTi" side="Images/hero_side.png"
```

| 参数 | 说明 |
|------|------|
| `key` | 角色标识符（say 的 speaker 匹配此值） |
| `name` | 显示名（不提供则用 key 作为显示名） |
| `color` | 说话者名字颜色，如 `#FF4444` |
| `font` | 说话者字体 |
| `text_color` | 对话文本颜色 |
| `text_font` | 对话文本字体 |
| `side` | 侧脸图路径（Phase 24，对标 Ren'Py Character side_image） |

- 定义后存储到 `__char_{key}` 字典
- `say` 的 `speaker` 匹配 `key` 时自动应用样式
- `say` 显式参数（`speaker_color=` 等）覆盖角色定义
- `side` 侧脸图在对话框左侧显示（120px 区域）

```
character "boss" name="魔王" color="#FF4444" font="SimHei" side="Images/boss_side.png"
character "hero" name="勇者" color="#44AAFF"

say "你来了..." speaker="boss"
// 自动使用红色名字 "魔王" + 侧脸图

say "我来战斗！" speaker="hero" speaker_color="#88FF88"
// 显式参数覆盖：绿色名字而非蓝色
```

---

## 四、场景块（scene block）

```
scene "scene_name"
  text "内容" at (x, y) [参数...]
  button "文本" at (x, y) size=(w, h) [参数...]
  image "路径" at (x, y) [参数...]
  [内联命令...]
```

- `scene` 块必须位于文件顶层（或 label 内缩进）
- 元素采用缩进格式
- 非元素行（如 `set` / `say` / `transition`）被提取为场景入口脚本（EntryCommands），场景显示后自动按序执行

### 4.1 text 元素

```
text "内容" at (x, y) [size=N] [color="#xxx"] [align=center|left|right] [max=W] [font="name"]
```

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `at (x, y)` | 必填 | 坐标，支持像素值或百分比（如 `50%`） |
| `size` | 16 | 字号 |
| `color` | `#FFFFFF` | 颜色 |
| `align` | `left` | 文字对齐 |
| `max` | — | 最大宽度（自动换行阈值），支持百分比 |
| `font` | `Microsoft YaHei` | 字体名 |

内容支持 `{player.gold}` 变量替换。

### 4.2 button 元素

```
button "文本" at (x, y) size=(w, h) [color="#xxx"] [nav="scene_name"] [cmd="command"]
```

| 参数 | 说明 |
|------|------|
| `nav` | 点击后导航到场景或触发 label 跳转 |
| `cmd` | 点击后执行字符串命令（`do_` 前缀） |
| 优先级 | `nav` 优先，其次 `cmd` |

支持 `{player.gold}` 变量替换。

### 4.3 image 元素

```
image "路径" at (x, y) [size=(w, h)] [opacity=N]
```

### 4.4 imagebutton 元素

```
imagebutton "图片路径" at (x, y) size=(w, h) [nav="scene"] [cmd="command"]
```

图片按钮，参数同 `button`，但使用图片作为外观。

### 4.5 bar / vbar 元素

```
bar value=50 max=100 at (x, y) size=(w, h)
vbar value=50 max=100 at (x, y) size=(w, h)
```

进度条（水平/垂直）。

### 4.6 panel 元素

```
panel direction=horizontal at (x, y) size=(w, h)
  [子元素...]
```

容器面板，`direction` 可选 `horizontal` 或 `vertical`。

### 4.7 grid 元素

```
grid at (x, y) size=(w, h) columns=3 rows=2
  [子元素...]
```

网格容器（Avalonia 原生 Grid），支持 `columns`/`rows` 定义。

### 4.8 scrollviewer 元素

```
scrollviewer at (x, y) size=(w, h)
  [子元素...]
```

可滚动容器。

### 4.9 viewport 元素（Phase 24）

```
viewport at (x, y) size=(w, h)
  [子元素...]
```

可滚动视图区域（对标 Ren'Py viewport）。

### 4.10 slider 元素

```
slider at (x, y) size=(w, h) min=0 max=100 value=50
```

滑块控件。

### 4.11 checkbox 元素

```
checkbox "标签文本" at (x, y) checked=true
```

复选框控件。

### 4.12 canvas 元素

```
canvas at (x, y) size=(w, h)
  [子元素...]
```

绝对定位容器（子元素坐标相对于 canvas 原点）。

### 4.13 border 元素

```
border at (x, y) size=(w, h) borderthickness=2 bordercolor="#FFFFFF"
```

独立边框控件（仅边框/背景，不自动包含 StackPanel）。

### 4.14 separator 元素

```
separator at (x, y) size=(w, 2)
```

分隔线。

### 4.15 spacer 元素

```
spacer at (x, y) size=(w, h)
```

空白占位元素。

### 4.16 video 元素

```
video "path/to/video.mp4" at (x, y) size=(w, h) volume=0.8 loop=false autoplay=true
```

视频播放控件。音视频分离架构：控件静音（Volume=0），音频走 AudioManager。

### 4.17 样式引用（class）

```
style "btn_primary" color="#88CCFF" size=18 fontFamily="Microsoft YaHei"
button "开始游戏" class="btn_primary" at (50%, 300) size=(200, 42)
// 按钮继承 btn_primary 的 color 和 size，自身 at/size 不被覆盖
```

- 元素通过 `class="style_name"` 引用样式
- 样式属性作为**默认值**，元素自身属性**覆盖**样式

---

## 五、流程命令

### 5.1 say — 对话

```
say "对话内容"
say "对话内容" speaker="说话者"
say "对话内容" speaker="说话者" clickable=true
say "对话内容" speaker="说话者" okey
```

| 参数 | 说明 |
|------|------|
| `speaker` | 说话者标识（匹配 `character` 定义的 key，主语法） |
| `clickable=true` | Phase 17: 禁用模态遮罩，允许场景按钮交互 |
| `okey` | Phase 17: `clickable=true` 的语法糖 |
| `by` | `speaker=` 的兼容别名（`say "text" by "名字"` 等价于 `say "text" speaker="名字"`） |

- 默认情况下，say 启用透明模态遮罩（ZIndex=50），拦截点击，仅推进对话
- `clickable=true` 或 `okey` 禁用遮罩，允许用户在 say 期间点击场景按钮
- 支持 `{player.gold}` 变量替换
- 支持内联标记（`{b}粗体{/b}` 等）
- `speaker` 匹配 `character` 定义的 key 时自动应用样式（含侧脸图）

### 5.2 navigate — 导航

```
navigate "scene_name"
navigate "scene_name" scene "alt_scene_name"
```

- 导航到指定场景
- 场景未注册时自动通过 `StoryRegistry` 懒加载
- 找不到场景且不以 `do_` 开头时，尝试作为 label 跳转

### 5.3 scene — 清空堆栈切换场景

```
scene "scene_name"
```

与 `navigate` 不同，`scene` 会清空场景堆栈。

### 5.4 set — 变量赋值

```
set "player.gold" {player.gold + 20}
set "player.hp" {player.hp - 10}
set "player.name" "新名字"
set "player.level" 5
```

- 左侧双引号内为变量名（支持点号命名）
- 右侧支持表达式 `{...}` 或静态值
- 在 DslExecutor 内**同步执行**（不经过管道），确保后续 `if` 读到最新值

### 5.5 transition — 过渡动画

```
transition "fade" duration=0.5
transition "slideleft" duration=0.6 easing=EaseOutQuad
```

| 类型标识符 | 过渡效果 |
|-----------|---------|
| `fade` / `crossfade` | CrossFade 交叉淡入 |
| `fadeout` | FadeOut 淡出 |
| `slideleft` / `slideleftin` | SlideLeftIn 左滑入 |
| `slideright` / `sliderightin` | SlideRightIn 右滑入 |
| `slideup` / `slideupin` | SlideUpIn 上滑入 |
| `slidedown` / `slidedownin` | SlideDownIn 下滑入 |
| `zoomin` / `zoom` | ZoomIn 缩放入 |
| `blink` / `blinkout` | BlinkOut 闪烁 |

可选参数：
- `duration`：时长（秒，默认 0.5）
- `easing`：缓动函数名（默认 `EaseOutQuad`）

### 5.6 wait — 等待

```
wait 2.0                # 不可跳过等待（必须等满指定时长）
wait 2.0 skipable       # 可跳过等待（用户点击可提前结束）
```

| 参数 | 说明 |
|------|------|
| `秒数` | 等待时长（秒） |
| `skipable` | 可选关键字，允许用户点击跳过等待 |

- `wait N`（不带 `skipable`）：使用 `Task.Delay` 实现，不可跳过
- `wait N skipable`：并行监听 `Task.Delay` 和用户点击事件，任一触发即完成
- `wait skipable` 期间显示透明遮罩，点击推进
- 对标 Ren'Py 的 `pause(N, hard=True)` / `pause(N, hard=False)`

### 5.7 pause — 暂停

```
pause               # 等待用户点击（对标 Ren'Py pause()）
pause 2.0           # 可跳过等待 2 秒（= wait 2.0 skipable）
pause 2.0 hard      # 不可跳过等待 2 秒（= wait 2.0）
```

- `pause`（无参数）：等待用户点击，创建 `pause` 类型检查点
- `pause N`：可跳过的定时等待，创建 `wait_skipable` 类型检查点
- `pause N hard`：不可跳过的定时等待，创建 `wait` 类型检查点
- 遮罩行为与 `wait skipable` 一致

### 5.8 bgm — 播放 BGM

```
bgm "bgm_main.wav" volume=0.8
```

`volume`：音量 0~1（默认 1.0）

### 5.9 show / hide — 显示/隐藏元素

```
show "Images/character.png" at (200, 100)
hide "Images/character.png"
```

`show` 可带 `at (x, y)` 坐标。`hide` 按路径匹配移除。

**带过渡的 show/hide（Phase 25）：**

```
show "Images/character.png" at (200, 100) with "fade" duration=1.5
hide "Images/character.png" with "dissolve" duration=2.0
```

- `with "transition"`：指定过渡类型（fade/dissolve 等）
- `duration=N`：过渡时长（秒）
- 过渡命令自动追加到 ShowHideCommand 之后

### 5.10 background — 设置背景

```
background "bg_forest.jpg"
```

### 5.11 animate — 控件动画

```
animate "target" opacity 0.5 duration=1.0 easing=EaseOutQuad
```

| 参数 | 说明 |
|------|------|
| `target` | 元素标识 |
| `property` | 动画属性（`opacity`, `x`, `y`, `scale`, `rotate`） |
| `目标值` | 数字 |
| `duration` | 持续时间（秒，默认 1.0） |
| `easing` | 缓动函数（默认 `EaseOutQuad`） |

### 5.12 animate_block — 批量动画

```
animate_block "target" x=100 y=200 opacity=0.5 duration=1.0 easing="EaseOutQuad"
```

- 对同一目标同时执行多个属性动画
- 编译为多条独立的 `AnimateCommand`，共享 `duration` 和 `easing`
- 支持的动画属性：`x`、`y`、`opacity`、`rotate`、`scale`
- `duration` 默认 1.0，`easing` 默认 `EaseOutQuad`

### 5.13 shake — 屏幕震动

```
shake intensity=10 duration=0.5
```

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `intensity` | 10 | 震动强度（像素） |
| `duration` | 0.5 | 持续时间（秒） |

### 5.14 input — 用户输入

```
input "请输入你的名字" store "player.name"
input "选择职业" store "player.class" options=["战士", "法师", "盗贼"]
```

| 参数 | 说明 |
|------|------|
| `store` | 存储输入的变量键名 |
| `options` | 可选的选项列表（多项选择） |

### 5.15 save / load — 存档/读档

```
save "slot_1"
load "slot_1"
```

### 5.16 back / forward — 堆栈导航

```
back
forward
```

### 5.17 call / return — 子过程调用

```
call sub_routine
// ... 子过程执行完毕后
return
```

### 5.18 skip / auto — 播放模式

```
skip    // 切换跳过模式
auto    // 切换自动模式
```

### 5.19 gallery unlock — 解锁 CG

```
gallery unlock "cg_001" "Images/cg_001.jpg" title="结局插画" scene="ending"
```

### 5.20 debug — 调试日志

```
debug "调试信息" level=Info
```

`level` 可选：`Info` / `Warning` / `Error` / `Debug`（默认 `Info`）

### 5.21 nvl — NVL 模式

```
nvl          // 进入 NVL 模式，后续对话累积显示
nvl clear    // 清空 NVL 累积文本并退出
```

### 5.22 style — 样式表定义

```
style "style_name" color="#88CCFF" size=18 fontFamily="Microsoft YaHei"
```

- 定义可复用的样式属性集合，存储到 `__style_{name}` 字典
- 场景元素通过 `class="style_name"` 引用样式
- 样式属性作为**默认值**，元素自身属性**覆盖**样式
- 支持属性：`color`、`size`、`fontFamily`、`opacity` 等

### 5.23 call_screen — 调用界面

```
call_screen "settings_panel"
call_screen "item_select" store="selected_item"
call_screen "shop" store="result" with "items=[药水,武器],gold=100"
```

| 参数 | 说明 |
|------|------|
| `场景名` | 要调用的 UI 场景名 |
| `store` | 可选，存储返回结果的变量键名 |
| `with` | 可选，传入参数（`k=v,k=v` 格式，Phase 24） |

- 导航到指定 UI 场景并阻塞当前脚本执行
- UI 场景内通过 `Ctrl.SetScreenResult(value)` 设置返回值
- DslExecutor 检测到返回值后恢复脚本执行
- 如果指定了 `store`，返回值存储到对应变量
- `with` 参数在 UI 场景中通过 `Ctrl.GetScreenParam<T>(key)` 获取
- 对标 Ren'Py 的 `call screen` 语句

### 5.24 window — 对话框窗口管理（Phase 24）

```
window auto     # 对话框自动模式（有对话显示、无对话隐藏）
window show     # 强制显示对话框
window hide     # 强制隐藏对话框
```

- `__dialog_window_mode` 状态键控制（auto/show/hide）
- 对标 Ren'Py 的 `window auto` / `window show` / `window hide`

### 5.25 block_rollback / fix_rollback（Phase 24）

```
block_rollback   # 阻止后续检查点创建（对标 Ren'Py renpy.block_rollback()）
fix_rollback     # 恢复检查点创建（对标 Ren'Py renpy.fix_rollback()）
```

- `block_rollback` 后，所有交互点不再创建回溯检查点
- `fix_rollback` 清除阻止标记，恢复正常检查点创建

---

## 六、控制流

### 6.1 label 与 jump

```
label chapter1:
  transition "fade" duration=0.5
  say "第一章开始了。" speaker="旁白"
  jump chapter1_begin

jump chapter1_begin
```

- `label xxx:`：标记入口点，编译时自动注册到全局 label 索引
- `jump xxx`：跳转到指定 label，编译时自动填入 `TargetIndex`
- 每个 label 末尾自动插入 `EndCommand` 哨兵，执行完后停止推进

### 6.2 if / elif / else

```
if {player.hp <= 0} {
  set "player.hp" 20
  set "player.gold" {player.gold + 50}
}

if {player.hp <= 0} {
  set "player.hp" 20
} else {
  set "player.gold" {player.gold + 10}
}

if {player.hp <= 0} {
  set "player.hp" 20
} else if {player.hp > 50} {
  set "player.gold" {player.gold + 10}
} else {
  set "player.gold" {player.gold + 5}
}
```

- 条件表达式不需要额外花括号（`player.hp <= 0` 即可）
- 块必须用 `{` 和 `}` 包裹
- 支持 `&&`、`||` 逻辑运算（语法级：`if {条件1 and 条件2}`）

### 6.3 while 循环

```
while {player.gold < 100} {
  set "player.gold" {player.gold + 10}
  say "金币 +10（当前 {player.gold}）"
}
```

- 条件为 `true` 时重复执行块内命令
- 条件为 `false` 时跳出循环
- 支持 `break` / `continue`（Phase 25）

### 6.4 for 循环（Phase 24）

```
for "i" in {5} {
  say "第 {i} 次循环"
}

for "item" in {["药水", "武器", "防具"]} {
  say "物品：{item}"
}
```

- 编译为 `while` + 索引变量
- 支持 `break` / `continue`（Phase 25）
- `break` 跳出循环，`continue` 跳到下一次迭代

### 6.5 break / continue（Phase 25）

```
while {true} {
  set "temp.dice" random(1, 6)
  if {temp.dice == 6} {
    break       // 掷出 6 时跳出循环
  }
  if {temp.dice == 1} {
    continue    // 掷出 1 时跳过本次
  }
  say "掷出了 {temp.dice}"
}
```

- `break`：立即跳出最近的 `while` 或 `for` 循环
- `continue`：跳到最近循环的下一次迭代
- 在循环外使用会编译报错

### 6.6 menu — 菜单选择

```
menu "选择行动："
  option "继续前进" -> path_ahead
  option "检查周围" -> search_area
  option "返回城镇" -> back_town
```

- 显示菜单，等待用户选择
- 每个选项格式：`option "显示文本" -> target_label`
- 选择后跳转到对应 label

---

## 七、变量表达式

### 7.1 模板替换

在 `text`、`button` 和 `say` 的内容中使用 `{变量名}`：

```
text "金币: {player.gold}" at (100, 200)
text "当前时间: 第{days}天 {hours}:{mins:00}" at (100, 250)
say "你的 HP 是 {player.hp}/{player.maxHp}"
```

支持的格式：
- `{player.gold}` → 扁平 key
- `{player.stats.hp}` → 嵌套字典路径
- `{hours:00}` → 数值格式化（`:00` → D2，`:000` → D3，`:X` → 十六进制）
- `{days}` / `{hours}` / `{mins}` → 从 `__game_time_total_minutes` 计算

### 7.2 条件表达式

用于 `if` / `while` 的条件判断：

```
if {player.gold >= 100}
if {player.hp <= 0}
if {player.level == 5}
if {player.name != ""}
```

支持运算符：`==`、`!=`、`>`、`<`、`>=`、`<=`

逻辑运算：`&&`、`||`、`and`、`or`

三元运算：`{a ? b : c}`

### 7.3 数学表达式

```
set "player.gold" {player.gold + 20}
set "player.gold" {player.gold * 2}
set "player.dice" random(1, 6)
set "player.result" {player.base + player.bonus * 2}
```

支持运算符：`+`、`-`、`*`、`/`、`%`

### 7.4 内置函数

| 函数 | 说明 | 示例 |
|------|------|------|
| `random(min, max)` | 随机整数（含两端） | `random(1, 6)` |
| `min(a, b)` | 最小值 | `min(10, 20)` |
| `max(a, b)` | 最大值 | `max(10, 20)` |

### 7.5 自定义函数

通过 C# 注册自定义 DSL 函数：

```csharp
DslExpressionEvaluator.RegisterFunction("clamp", (args) =>
{
    var val = Convert.ToDouble(args[0]);
    var min = Convert.ToDouble(args[1]);
    var max = Convert.ToDouble(args[2]);
    return Math.Clamp(val, min, max);
});
```

在 DSL 中使用：

```
set "player.hp" clamp(player.hp, 0, player.maxHp)
```

### 7.6 字面量

| 字面量 | 示例 | 类型 |
|--------|------|------|
| 整数 | `100` | int |
| 浮点 | `3.14` | double |
| 布尔 | `true` / `false` | bool |
| null | `null` | null |
| 字符串 | `"文本"` | string |
| 数组 | `["a", "b", "c"]` | List |
| 对象 | `{"key": value}` | Dictionary |

---

## 八、内联标记（Inline Markup）

在 `say` 和 `text` 内容中使用富文本标记：

| 标记 | 说明 | 示例 |
|------|------|------|
| `{b}...{/b}` | 粗体 | `{b}重要{/b}` |
| `{i}...{/i}` | 斜体 | `{i}备注{/i}` |
| `{u}...{/u}` | 下划线 | `{u}强调{/u}` |
| `{color=#FF0000}...{/color}` | 文字颜色 | `{color=#FF0000}红色{/color}` |
| `{font=SimHei}...{/font}` | 字体 | `{font=SimHei}黑体{/font}` |
| `{size=24}...{/size}` | 字号 | `{size=24}大字{/size}` |
| `{w}` | 等待点击 | `第一段{w}第二段` |
| `{fast}` | 快速跳过打字机 | `{fast}立即显示` |
| `{p}` | 段落换行 | `第一段{p}第二段` |

> 注意：`{b}` 等标记与 `{变量名}` 使用相同花括号语法，但 DSL 引擎会自动区分标记标签和变量表达式。

---

## 九、通用交互属性

Phase 27 引入了通用交互系统，以下属性支持**所有 UI 控件**（不仅限于按钮）：

### 交互属性

| 属性 | 说明 | 示例 |
|------|------|------|
| `nav="scene"` | 点击导航到场景/label | `image "bg" nav="chapter1"` |
| `cmd="do_xxx"` | 点击执行字符串命令 | `text "提示" cmd="do_help"` |
| `hover_source="path"` | 鼠标悬停换图（Image 专用） | `image "a.png" hover_source="a_hover.png"` |
| `hover_color="#xxx"` | 鼠标悬停变色（Text/Button/Border） | `button "btn" hover_color="#FF0000"` |
| `hover_opacity=0.8` | 鼠标悬停透明度变化（通用） | `image "bg" hover_opacity=0.8` |
| `selected_source="path"` | 点击切换图片（Image，保持选中态） | `image "tab" selected_source="tab_on.png"` |
| `disabled=true` | 禁用控件交互 | `button "btn" disabled=true` |

### 交互优先级

1. `disabled=true` → 禁用所有交互
2. `nav` → 点击投递 `NavigateCommand`
3. `cmd` → 点击执行字符串命令
4. `hover_*` → 鼠标进入/离开时触发视觉变化
5. `selected_source` → 点击切换图片并保持选中态

---

## 十、变量命名约定

### 核心规则：`__` 前缀 = 系统变量

- 任何以 `__` 开头的变量均为引擎内部状态
- **存档系统自动排除所有 `__` 变量**
- 创作者**不应当**创建以 `__` 开头的自定义变量

### 用户变量命名空间

| 命名空间 | 示例 | 说明 |
|---------|------|------|
| `player.*` | `player.gold`、`player.hp` | 玩家属性（自动存档） |
| `npc.*` | `npc.merchant.name` | NPC 属性（自动存档） |
| `story.*` | `story.progress` | 剧情状态（自动存档） |
| `chapter*.*` | `chapter1.flag_door` | 章节专属状态（自动存档） |
| `sandbox.*` | `sandbox.battle_count` | 沙盒/测试状态（自动存档） |
| `_local_*` | `_local_temp_counter` | 局部变量（不存档，场景切换清除） |

---

## 十一、回溯与前进

灵泛引擎实现了类似 Ren'Py 的统一线性回溯时间线。回溯检查点在以下交互点自动创建：

| 交互类型 | 说明 |
|---------|------|
| `say` | 每句对话创建检查点 |
| `menu` | 菜单选择前创建检查点 |
| `input` | 用户输入前创建检查点 |
| `wait` / `wait skipable` | 等待前创建检查点 |
| `pause` | 暂停前创建检查点 |
| `call_screen` | 调用界面前创建检查点 |
| `scene_idle` | 场景空闲（EndCommand/命令耗尽）时创建 |
| `navigate` | 导航前创建检查点 |

- **新交互截断未来**：回溯后创建新交互时，截断当前位置之后的检查点
- **回溯重展示**：`IsReplay` 标记控制，回放时不记录历史
- **跨场景回溯**：检查点快照含 `__dsl_commands` + `__scene_elements`，支持跨场景回退
- **最大检查点数**：`MaxRollbackCheckpoints=100`（可配置）
- **block_rollback**：阻止后续检查点创建（Phase 24）
- **C# 场景**：通过 `CreateSceneCheckpoint()` 创建场景级检查点，回溯时通过异常机制终止旧 Runner（Phase 26）

鼠标滚轮上滚 = 回溯，下滚 = 前进（仅 Game 场景生效，50ms 节流）。

---

## 十二、视频命令

### 12.1 video — 播放视频

```
video "Videos/intro.mp4" volume=0.8 loop=false autoplay=true
```

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `volume` | 1.0 | 音量（0~1，但 GpuMediaPlayer 静音，音频走 AudioManager） |
| `loop` | false | 是否循环播放 |
| `autoplay` | true | 是否自动播放 |

### 12.2 stop_video — 停止视频

```
stop_video
```

### 12.3 pause_video — 暂停视频

```
pause_video
```

### 12.4 resume_video — 恢复视频

```
resume_video
```

### 12.5 seek_video — 跳转视频

```
seek_video 5.0    # 跳转到 5 秒位置
```

### 12.6 cutscene — 过场动画

```
cutscene "Videos/op.mp4" skipable=true volume=1.0
cutscene "Videos/op.mp4" skipable=false
```

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `skipable` | true | 用户是否可点击跳过 |
| `volume` | 1.0 | 音量 |

- 全屏播放，阻塞脚本执行
- `skipable=true` 时显示透明遮罩，点击跳过
- `skipable=false` 时必须等待自然结束
- 对标 Ren'Py `renpy.movie_cutscene()`

---

## 十三、DSL ↔ C# 命令映射表

| DSL 语法 | C# 命令类型 | IGameController 方法 |
|---------|------------|---------------------|
| `say "text" speaker="speaker"` | `ShowDialogCommand` | `Say` / `SayAsync` |
| `say "text" clickable=true` / `say okey` | `ShowDialogCommand(Clickable=true)` | `Say(..., clickable: true)` |
| `character "key" name=... side=...` | `SetVariableCommand` | `DefineCharacter` |
| `navigate "scene"` | `NavigateCommand` | `Navigate` / `NavigateAsync` |
| `scene "name"` | `SceneCommand` | — |
| `set "key" value` | `SetVariableCommand` | `Set` / `SetAsync` |
| `define "key" value once` | `SetVariableCommand(IsDefine=true)` | `Define` / `DefineAsync` |
| `let "key" value once` | `SetVariableCommand(IsDefine=true)` | — |
| `transition "fade" duration=0.5` | `TransitionCommand` | `Transition` / `TransitionAsync` |
| `wait 2.0` | `WaitCommand(IsSkipable=false)` | `WaitAsync` |
| `wait 2.0 skipable` | `WaitCommand(IsSkipable=true)` | `SkipableWaitAsync` |
| `pause` | `HardPauseCommand` | `WaitForClickAsync` |
| `pause 2.0` | `WaitCommand(IsSkipable=true)` | `SkipableWaitAsync` |
| `pause 2.0 hard` | `WaitCommand(IsSkipable=false)` | `WaitAsync` |
| `style "name" ...` | `SetVariableCommand` | — |
| `animate_block "t" ...` | 多条 `AnimateCommand` | — |
| `call_screen "scene"` | `CallScreenCommand` | `CallScreenAsync` |
| `call_screen "scene" with "k=v"` | `CallScreenCommand(Params=...)` | `CallScreenAsync(..., parameters)` |
| `window auto/show/hide` | `SetVariableCommand` | `SetWindowAuto` / `ShowWindow` / `HideWindow` |
| `block_rollback` | `SetVariableCommand` | `BlockRollback` |
| `fix_rollback` | `SetVariableCommand` | `FixRollback` |
| `bgm "path" volume=0.8` | `PlayBgmCommand` | `PlayBgm` / `PlayBgmAsync` |
| `show "path" at (x,y)` | `ShowHideCommand(IsShow=true)` | `Show` / `ShowAsync` |
| `show "path" with "fade" duration=1.5` | `ShowHideCommand` + `TransitionCommand` | — |
| `hide "path"` | `ShowHideCommand(IsShow=false)` | `Hide` / `HideAsync` |
| `hide "path" with "dissolve" duration=2.0` | `ShowHideCommand` + `TransitionCommand` | — |
| `background "path"` | `ShowHideCommand(IsBackground=true)` | `Background` / `BackgroundAsync` |
| `input "prompt" store "key"` | `InputCommand` | `InputAsync` |
| `save "slot"` | `SaveLoadCommand(IsSave=true)` | `Save` / `SaveAsync` |
| `load "slot"` | `SaveLoadCommand(IsSave=false)` | `Load` / `LoadAsync` |
| `jump label` | `JumpCommand` | — |
| `call label` | `CallCommand` | — |
| `return` | `ReturnCommand` | — |
| `if {cond} { ... }` | `BranchCommand` | — |
| `while {cond} { ... }` | `BranchCommand` | — |
| `for "var" in {expr} { ... }` | `BranchCommand`（编译为 while） | — |
| `break` | `JumpCommand` | — |
| `continue` | `JumpCommand` | — |
| `menu "prompt"` + `option` | `MenuCommand` | `ShowMenuAsync` |
| `animate "t" prop val` | `AnimateCommand` | — |
| `shake intensity=10` | `ShakeCommand` | `Shake` / `ShakeAsync` |
| `back` | `BackCommand` | `Back` / `BackAsync` |
| `forward` | `ForwardCommand` | `Forward` / `ForwardAsync` |
| — (鼠标滚轮上滚) | `RollbackCommand` | `Rollback` / `RollbackAsync` |
| — (鼠标滚轮下滚) | `RollforwardCommand` | `Rollforward` / `RollforwardAsync` |
| `skip` | `ToggleSkipCommand` | `ToggleSkip` / `ToggleSkipAsync` |
| `auto` | `ToggleAutoCommand` | `ToggleAuto` / `ToggleAutoAsync` |
| `gallery unlock "id" "path"` | `UnlockGalleryCommand` | `UnlockGallery` / `UnlockGalleryAsync` |
| `debug "msg" level=Info` | `DebugLogCommand` | `DebugLog` / `DebugLogAsync` |
| `nvl` / `nvl clear` | `NvlCommand` | `EnterNvl` / `ClearNvl` |
| `video "path" volume=N` | `PlayVideoCommand` | `PlayVideo` / `PlayVideoAsync` |
| `stop_video` | `StopVideoCommand` | `StopVideo` / `StopVideoAsync` |
| `pause_video` | `PauseVideoCommand` | `PauseVideo` / `PauseVideoAsync` |
| `resume_video` | `ResumeVideoCommand` | `ResumeVideo` / `ResumeVideoAsync` |
| `seek_video N` | `SeekVideoCommand` | `SeekVideo` / `SeekVideoAsync` |
| `cutscene "path" skipable=true` | `CutsceneCommand` | `PlayCutsceneAsync` |

---

## 十四、编译错误信息

### 解析阶段错误

逐行解析时，如果某行语法错误，返回包含行号和原始内容的错误：

```
DSL 解析错误（第 15 行）: 未知的语句类型: foo bar
  → foo bar x=1
```

### 编译阶段错误

两遍扫描/命令生成阶段的异常：

```
DSL 编译错误: InvalidOperationException: 无法解析跳转目标 label 'missing_label'
  堆栈:    at LingFanDslEngine.ResolvePendingJumps(...)
```

### 热重载错误

`StoryRegistry.ReloadFile` 编译失败时返回 `false` 并输出日志：

```
[StoryRegistry] ReloadFile failed: Stories/chapter1/chapter1.story — DSL 解析错误（第 23 行）...
```

---

## 十五、完整示例

### 15.1 标题画面 + 章节

```
// ===== 变量定义 =====
define "player.name" "旅人" once
define "player.gold" 100 once
define "player.hp" 50 once
define "player.maxHp" 100 once
define "story.progress" 0 once

// ===== 角色定义 =====
character "narrator" name="旁白" color="#AAAAAA"
character "hero" name="勇者" color="#44AAFF" side="Images/hero_side.png"
character "boss" name="魔王" color="#FF4444" side="Images/boss_side.png"

// ===== 标题场景 =====
scene "title_main"
  text "灵泛引擎" at (50%, 120) size=48 color="#FFD700" align=center
  text "Demo" at (50%, 180) size=20 color="#AAAAAA" align=center
  button "开始游戏" at (50%, 320) size=(240, 44) color="#88CCFF" nav="chapter1_intro" align=center
  button "沙盒模式" at (50%, 380) size=(240, 44) color="#88FF88" nav="sandbox" align=center
  button "退出" at (50%, 440) size=(240, 44) color="#FF8888" cmd="do_exit" align=center

// ===== 第一章入口 =====
label chapter1_intro:
  transition "fade" duration=0.8
  say "第一章：冒险开始" speaker="narrator"
  say "你是一位年轻的旅人，踏上了未知的旅程..."
  say "前方有三条路，你选择哪一条？"

menu "选择行动："
  option "继续前进" -> path_ahead
  option "检查周围" -> search_area
  option "返回城镇" -> back_town

// ===== 路径 A：继续前进 =====
label path_ahead:
  set "story.progress" 1
  say "你选择了继续前进。"
  if {player.gold >= 50} {
    say "你花了 50 金币买了一把剑。"
    set "player.gold" {player.gold - 50}
  } else {
    say "你没有足够的金币，只能空手前行。"
  }
  say "前方出现了一只怪物！"
  shake intensity=15 duration=0.3
  set "player.hp" {player.hp - 20}
  say "你受到了 20 点伤害！（HP: {player.hp}/{player.maxHp}）"
  if {player.hp <= 0} {
    say "你倒下了..."
    navigate "game_over"
  }
  navigate "chapter1_end"

// ===== 路径 B：检查周围 =====
label search_area:
  set "story.progress" 2
  say "你仔细检查了周围。"
  set "player.gold" {player.gold + 30}
  say "发现了 30 金币！（当前: {player.gold}）"
  navigate "chapter1_end"

// ===== 第一章结束 =====
label chapter1_end:
  transition "fade" duration=0.5
  say "第一章结束。" speaker="narrator"
  say "你的最终状态：HP {player.hp}/{player.maxHp}，金币 {player.gold}"
  save "auto_chapter1"
  say "已自动存档。"
  navigate "title_main"
```

### 15.2 带动画和音频的场景

```
scene "battle_arena"
  background "Images/battle_bg.jpg"
  image "Images/hero.png" at (200, 300)
  image "Images/enemy.png" at (800, 300)

label battle_start:
  bgm "Audio/battle_theme.mp3" volume=0.7
  say "战斗开始！" speaker="系统"
  animate "Images/enemy.png" x 850 duration=0.5 easing=EaseOutQuad
  shake intensity=10 duration=0.3
  say "敌人发起了攻击！"
  set "player.hp" {player.hp - 15}
  say "HP: {player.hp}/{player.maxHp}"

  if {player.hp <= 0} {
    say "你被击败了..."
    navigate "game_over"
  }

  menu "选择行动："
    option "攻击" -> battle_attack
    option "防御" -> battle_defend
    option "逃跑" -> battle_flee

label battle_attack:
  set "enemy.hp" {enemy.hp - 25}
  say "对敌人造成了 25 点伤害！"
  if {enemy.hp <= 0} {
    say "敌人被击败了！"
    bgm "Audio/victory.mp3" volume=0.8
    say "你赢了！"
    gallery unlock "cg_victory" "Images/cg_victory.jpg" title="胜利" scene="battle_start"
    navigate "chapter1_end"
  }
  jump battle_start
```

### 15.3 for 循环 + break/continue

```
label gambling:
  let "temp.gold" 0
  for "round" in {10} {
    set "temp.dice" random(1, 6)
    if {temp.dice == 6} {
      say "掷出了 6！幸运一击，提前结束！"
      break
    }
    if {temp.dice == 1} {
      say "掷出了 1，霉运！跳过这轮。"
      continue
    }
    set "temp.gold" {temp.gold + temp.dice}
    say "第 {round} 轮：掷出 {temp.dice}（累计: {temp.gold}）"
  }
  say "赌博结束！获得 {temp.gold} 金币！"
  set "player.gold" {player.gold + temp.gold}
  navigate "casino"
```

### 15.4 call_screen 带参数

```
// 主脚本
label shop_scene:
  call_screen "shop_panel" store="purchase" with "items=[药水,武器,防具],gold=100"
  say "你选择了：{purchase}"
  navigate "town"

// UI 场景 shop_panel 中可通过 Ctrl.GetScreenParam 获取参数
```

### 15.5 视频过场动画

```
label intro_video:
  cutscene "Videos/op.mp4" skipable=true volume=0.8
  say "视频播放完毕。" speaker="narrator"
  navigate "chapter1_intro"

label no_skip_intro:
  cutscene "Videos/op.mp4" skipable=false
  say "这段视频不可跳过。" speaker="narrator"
```

### 15.6 window 管理 + block_rollback

```
label cutscene_dialog:
  window hide          # 隐藏对话框，全屏展示
  transition "fade" duration=1.0
  wait 2.0
  window show          # 显示对话框
  say "旁白出现。" speaker="narrator"
  
  block_rollback       # 阻止回溯到此点之前
  say "这个选择无法回溯。"
  menu "你的选择："
    option "选项 A" -> path_a
    option "选项 B" -> path_b
  fix_rollback         # 恢复回溯
```
