# DSL 语法参考

灵泛引擎 DSL（领域特定语言）用于编写 `.story` 剧本文件。本页列出所有语法关键字和参数。

## 文件格式

- 扩展名：`.story`
- 编码：UTF-8
- 注释：`//` 或 `#` 开头

## 目录结构

```
Stories/
├── title/
│   └── title_main.story      # 入口（TitleSceneName 配置）
├── chapter1/
│   └── chapter1.story
└── system/
    └── sandbox.story
```

引擎自动扫描所有 `.story` 文件，`scene` 和 `label` 名字全局唯一。

## 变量

### define 全局变量

```dsl
define "key" value once
```

- `once`——只在变量不存在时设置

### let / local 局部变量

```dsl
let "key" value
local "key" value        # 别名
```

局部变量以 `_local_` 前缀存储，场景切换时自动清除。

### set 修改

```dsl
set "key" {expression}
set "key" += 50          // ✅ 支持复合赋值 += -= *= /= %=，等价于 {key + 50}
set "key" {key + 50}     // 花括号写法
```

### undef 销毁

```dsl
undef "key"
```

## 角色

```dsl
character "key" name="显示名" color="#FF4444" font="Microsoft YaHei" side="path" screen="template"
```

| 参数 | 说明 |
|:---|:---|
| `name` | 显示名 |
| `color` | 名字颜色 |
| `font` | 字体名（null 时使用 Avalonia 默认字体） |
| `side` | 侧脸图路径 |
| `screen` | 对话框模板名（角色级绑定） |

## 场景

```dsl
scene "name" type=menu|game|ui
  // UI 元素和命令
```

## UI 元素

### text

```dsl
text "内容" x=50% y=20% size=48 color="#FFD700" halign=center font="..." opacity=0.8
```

### button

```dsl
button "文字" x=50% y=50% width=240 height=48 color="#88CCFF" nav="target" cmd="command" value="param" halign=center
```

**交互属性：**

| 属性 | 说明 |
|------|------|
| `nav="scene_name"` | 点击后导航到指定场景 |
| `cmd="command_name"` | 点击后执行注册的命令（配合 `value` 传参） |
| `value="参数值"` | 传递给 `cmd` 命令处理器的参数，支持 `{占位符}` 表达式（点击时求值） |

`nav` 和 `cmd` 互斥，`cmd` 优先级更高。

**cmd + value 示例：**

```dsl
# 静态参数
button "English" cmd="switch_lang" value="en-US"

# 动态参数（{占位符} 点击时求值）
button "加载存档" cmd="load_save" value="{selected_slot}"
```

C# 端注册命令处理器：

```csharp
cmdService.RegisterCommand("switch_lang", async (value, ct) =>
{
    // value 就是 DSL 中 value="xxx" 的值
    var lang = value?.ToString() ?? "zh-CN";
    // ... 处理逻辑
});
```

### image

```dsl
image "path" x=0 y=0 width=100% height=100% opacity=0.5 zindex=10
```

### vbox / hbox

```dsl
vbox x=50% y=40% spacing=12 halign=center
  button "选项1" width=200 height=44
  button "选项2" width=200 height=44
```

## 对话

### say

```dsl
say "文本" speaker="说话者" clickable=true instant=false noskip=false typewriter=true template="xxx"
```

| 参数 | 默认 | 说明 |
|:---|:---|:---|
| `speaker` | null | 说话者（character key 或字面字符串） |
| `clickable` | false | 对话期间允许点击 UI |
| `instant` | false | 跳过打字机 |
| `noskip` | false | Skip 模式下仍需点击 |
| `typewriter` | true | 启用打字机 |
| `template` | null | 对话框模板名 |

### nvl

```dsl
nvl           # 进入 NVL 模式
nvl clear     # 清空文本，仍在 NVL
nvl exit      # 退出 NVL，恢复 ADV
```

### window

```dsl
window show   # 强制显示对话框
window hide   # 强制隐藏
window auto   # 自动模式
```

## 流程控制

### navigate / jump

```dsl
navigate "target"     # 跳转，创建回溯检查点
jump "target"         # 跳转，不创建检查点
```

### menu

```dsl
menu "提示文字"
  "选项1" -> label1
  "选项2" -> label2
```

### call / return

```dsl
call "subroutine"
# 子过程内
return
```

### call_screen

```dsl
call_screen "ui_scene" store="result" with "k=v"
```

### back / forward

```dsl
back      # 回到上一个场景
forward   # 前进到下一个场景
```

## 控制流

### if / else if / else / end

```dsl
if {condition}
  ...
else if {condition}
  ...
else
  ...
end
```

### while / break / continue

```dsl
while {condition}
  ...
  break
  continue
end
```

### for

```dsl
for "var" in {1, 2, 3}
  ...
end
```

### foreach

```dsl
foreach "var" in "array_key"
  ...
```

### switch / case / default

```dsl
switch {expr}
  case 1
    ...
  case 2
    ...
  default
    ...
```

### func / return

```dsl
func name(param1, param2)
  ...
  return value
```

## 数据结构

### array

```dsl
array "key" [item1, item2, item3] once
array_push "key" "item"
array_pop "key"
```

### dict

```dsl
dict "key" {"k1": v1, "k2": v2}
dict_set "key" "subkey" value
```

## 视觉

### background / bg_switch

```dsl
background "path"
bg_switch "path" transition=fade duration=1.0
```

### sprite

```dsl
sprite "tag" src="path" x=30 y=50 fade=0.5
sprite_move "tag" x=100 y=200 duration=1.0
sprite_hide "tag" fade=0.5
sprite_state "tag" emotion="smile"
```

### show / hide

```dsl
show "tag" with "fade" duration=0.5
hide "tag" with "dissolve" duration=0.8
```

### transition

```dsl
transition "fade" duration=1.5
```

效果：`fade` / `crossfade` / `fadeout` / `dissolve` / `slideleft` / `slideright` / `slideup` / `slidedown` / `fadeup` / `fadedown` / `blur` / `zoomin`(或 `zoom`) / `shrink` / `blink`（大小写不敏感，无 `none`）

### animate

```dsl
animate "tag" property value [duration=N] [easing=EaseOutQuad]
```

属性：`x` / `y` / `opacity` / `rotate` / `scale`

缓动（PascalCase 枚举成员名，大小写敏感，默认 `EaseOutQuad`）：`Linear` / `EaseInQuad` / `EaseOutQuad` / `EaseInOutQuad` / `EaseInCubic` / `EaseOutCubic` / `EaseInOutCubic` / `EaseInBack` / `EaseOutBack` / `EaseInOutBack` / `EaseInElastic` / `EaseOutElastic` / `EaseInOutElastic` / `EaseInBounce` / `EaseOutBounce` / `EaseInOutBounce`

示例：`animate "tag" x 80 duration=1.0 easing=EaseOutQuad`

### shake

```dsl
shake duration=0.5 intensity=10
```

### zindex / popup / notify

```dsl
zindex 20             # 设置场景全局 Z 轴层级
popup "name" width=400 height=300 mask=true
notify "提示文字" duration=3.0
```

## 音频

### bgm

```dsl
bgm "path" volume=0.7
```

停止 BGM 需使用 C# API：`gameController.StopBgm()` 或 `StopBgmAsync()`。

BGM 交叉淡入队列（C# API）：

```csharp
await gameController.SendAsync(new BgmQueueCommand { Path = "Audio/BGM/song2.ogg", Volume = 0.7f, CrossFadeDuration = 2.0 });
```

### se / ambient / voice

```dsl
se "path" volume=0.5
ambient "path" volume=0.4
stop_ambient
stop_ambient "tag"

voice "path" volume=0.9 auto_stop=false   // 独立语音语句（单轨）
say "文本" speaker="x" voice="path"         // 随对话行内播放
stop_voice                                  // 停止当前语音
```

::: tip 语音（Voice）
语音走独立单轨通道：下一句 `say voice=` 或 `voice` 会原子替换当前语音。
`say voice=` 在回溯/前进重看时同样重播（符合直觉）。`stop_voice` 用于对话结束后主动中断。
:::

## 文本特效

### text_typewriter

```dsl
text_typewriter speed=30
```

### 内联标记

| 标记 | 效果 |
|:---|:---|
| `{b}...{/b}` | 粗体 |
| `{i}...{/i}` | 斜体 |
| `{u}...{/u}` | 下划线 |
| `{color=#xxx}...{/color}` | 颜色 |
| `{font=xxx}...{/font}` | 字体 |
| `{size=N}...{/size}` | 字号 |
| `{w}` | 暂停（点击继续） |
| `{p}` | 段落暂停 |
| `{fast}` | 跳到末尾 |

## 存档

```dsl
save "slot"
load "slot"
auto_save true|false
save_delete "slot"
```

## 回溯控制

```dsl
block_rollback      # 禁止回溯到此点之前
fix_rollback        # 允许查看但不允许改变
```

## 时间系统

### set_time_event

```dsl
set_time_event "id" HOUR [minute=N] [day=N] [once=true|false] [weekdays="Mon,Tue"] [condition="{expr}"] [desc="描述"]
  # 缩进块：触发时执行的代码
```

### unregister_time_event

```dsl
unregister_time_event "id"
```

### time_pause / time_resume / skip_time

```dsl
time_pause
time_resume
skip_time N          # 跳过 N 分钟
```

### time_event（兼容）

```dsl
time_event day=N hour=N target="label" once=true
```

## 播放控制

```dsl
auto_speed N         # 自动播放间隔（秒）
no_skip              # 禁用跳过
force_skip           # 强制跳过
video_skipable true|false   # 视频可跳过
video_auto_nav "scene"      # 视频结束后自动导航到场景
```

## 解锁系统

```dsl
# CG 解锁（长语法，含图片路径）
gallery unlock "cg_id" "Images/cg.png" title="标题" scene="查看场景"
# CG 解锁（短语法，无路径）
gallery_unlock "cg_id" title="标题"
# 章节解锁
chapter "ch1" name "第一章" unlock=true
# 成就解锁
achievement "ach1" name "成就名"
```

## Live2D

```dsl
live2d_char "tag" src="path" x=50 y=50 height=400 fade=0.5
live2d_show "tag"
live2d_motion "tag" name="motion_name" fade=0.3 loop=true
live2d_expr "tag" name="expr_name" fade=0.3
live2d_param "tag" param="BodyAngleX" value=-8 weight=0.6
live2d_hide "tag" fade=0.5
live2d_pause "tag"
live2d_resume "tag"
```

## 视频

```dsl
video "path" [volume=N] [loop=true|false] [autoplay=true|false]
stop_video
pause_video
resume_video
seek_video N         # 跳到 N 秒
cutscene "path" [skipable=true|false]  # 播放不可跳过视频
```

## 调试

```dsl
debug "message" level=Info|Warn|Error|Debug
```

## 表达式语法

| 运算 | 示例 |
|:---|:---|
| 算术 | `{a + b}` `{a - b}` `{a * b}` `{a / b}` `{a % b}` |
| 比较 | `{a > b}` `{a < b}` `{a >= b}` `{a <= b}` `{a == b}` `{a != b}` |
| 逻辑 | `{a && b}` `{a \|\| b}` `{!a}` |
| 三元 | `{a > b ? "大" : "小"}` |
| 随机 | `{random(1, 6)}` |
| 数学函数 | `{min(a, b)}` `{max(a, b)}` `{abs(a)}` `{clamp(a, 0, 100)}` |
| 格式化 | `{var:0.0}` `{var:#,##0}` |
| 变量引用 | `{player.gold}` |

::: warning 表达式不支持的写法
- 单 `&` / 单 `|` 未实现，误用会**静默返回 `false`**；逻辑请用 `&&` / `||`。
- 链式比较 `{a < b < c}` 不支持，拆成 `{a < b && b < c}`。
- `===` / `!==` / `++` / `--` 均不支持。
:::

## 块结束

```dsl
end     # if/while/for/func/switch 块结束
```
