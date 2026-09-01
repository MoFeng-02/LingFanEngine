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
set "key" += 50          // ✅ 复合赋值：+=  -=  *=  /=  %=
set "key" -= 10          // 等价于 {key - 10}
set "key" *= 2           // 等价于 {key * 2}
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

## 通用布局属性

以下属性适用于**所有 UI 元素**（text/button/image/grid/panel 等）：

| 属性 | 说明 | 示例 |
|:---|:---|:---|
| `x` / `y` | 坐标（像素或百分比） | `x=50% y=100` |
| `width` / `height` | 尺寸 | `width=200 height=44` |
| `minWidth` / `minHeight` | 最小尺寸 | `minWidth=100` |
| `maxWidth` / `maxHeight` | 最大尺寸 | `maxWidth=300` |
| `margin` | 外边距 `"left,top,right,bottom"` | `margin="10,20,10,20"` |
| `padding` | 内边距 `"left,top,right,bottom"` | `padding="5,5,5,5"` |
| `halign` / `align` | 水平对齐：`left` / `center` / `right` / `stretch` | `halign=center` |
| `valign` / `yalign` | 垂直对齐：`top` / `center` / `bottom` / `stretch` | `valign=center` |
| `opacity` | 透明度 0.0~1.0 | `opacity=0.5` |
| `visible` | 是否可见 | `visible=false` |
| `enabled` | 是否启用交互 | `enabled=false` |
| `zindex` | 层级 | `zindex=10` |
| `clipToBounds` | 裁剪子元素 | `clipToBounds=true` |
| `cursor` | 鼠标样式 | `cursor=hand` |
| `rotation` | 旋转角度 | `rotation=45` |
| `scale` / `scaleX` / `scaleY` | 缩放 | `scale=1.5` |
| `cornerRadius` | 圆角半径 | `cornerRadius=8` |
| `borderBrush` / `borderColor` | 边框颜色 | `borderColor="#FFFFFF"` |
| `borderThickness` | 边框粗细 | `borderThickness=2` |
| `class` | 引用已定义的 style | `class="btn_primary"` |

### 通用交互属性

以下属性适用于**所有 UI 控件**（不仅限于按钮）：

| 属性 | 说明 | 示例 |
|:---|:---|:---|
| `nav="scene"` | 点击导航到场景/label | `image "bg" nav="chapter1"` |
| `cmd="do_xxx"` | 点击执行字符串命令 | `text "提示" cmd="do_help"` |
| `hover_source="path"` | 鼠标悬停换图（Image 专用） | `image "a.png" hover_source="a_hover.png"` |
| `hover_color="#xxx"` | 鼠标悬停变色 | `button "btn" hover_color="#FF0000"` |
| `hover_opacity=0.8` | 鼠标悬停透明度 | `image "bg" hover_opacity=0.8` |
| `selected_source="path"` | 点击切换图片 | `image "tab" selected_source="tab_on.png"` |
| `disabled=true` | 禁用交互 | `button "btn" disabled=true` |

交互优先级：`disabled` > `nav` > `cmd` > `hover_*` > `selected_source`

## UI 元素

### text

```dsl
text "内容" x=50% y=20% size=48 color="#FFD700" halign=center font="..." opacity=0.8
```

### button

```dsl
button "文字" x=50% y=50% width=240 height=48 color="#88CCFF" nav="target" cmd="command" value="param" halign=center
```

| 属性 | 说明 |
|:---|:---|
| `nav="scene_name"` | 点击后导航到指定场景 |
| `cmd="command_name"` | 点击后执行注册的命令（配合 `value` 传参） |
| `value="参数值"` | 传递给 `cmd` 命令处理器的参数，支持 `{占位符}` 表达式 |

`nav` 和 `cmd` 互斥，`cmd` 优先级更高。

### image

```dsl
image "path" x=0 y=0 width=100% height=100% opacity=0.5 zindex=10 stretch=uniformtofill
```

`stretch`：`fill` / `uniform` / `uniformtofill`

### grid（网格容器）

```dsl
grid x=50% y=50% width=800 height=600 columns="*,2*,*" rows="Auto,100,*"
  text "标题" col=0 row=0 halign=center
  button "开始" col=1 row=1 colspan=2
  image "bg.png" col=0 row=2 colspan=3
```

| 属性 | 说明 | 示例 |
|:---|:---|:---|
| `columns` | 列定义（`*`=填充, `2*`=两倍, `100`=固定像素） | `columns="*,2*"` |
| `rows` | 行定义 | `rows="Auto,100,*"` |
| `col` | 子元素列索引（0-based） | `col=0` |
| `row` | 子元素行索引（0-based） | `row=1` |
| `colspan` | 列跨距 | `colspan=2` |
| `rowspan` | 行跨距 | `rowspan=2` |

### panel / vbox / hbox（容器）

```dsl
panel direction=vertical spacing=12 x=50% y=40% halign=center
  button "选项1" width=200 height=44
  button "选项2" width=200 height=44
```

| 属性 | 说明 |
|:---|:---|
| `direction` | `horizontal`（默认）/ `vertical` |
| `spacing` | 子元素间距 |

`vbox` = `panel direction=vertical`，`hbox` = `panel direction=horizontal` 的语法糖。

### scrollview（可滚动容器）

```dsl
scrollview x=0 y=0 width=400 height=300
  text "长文本内容..." y=0
  text "更多内容..." y=100
```

### slider（滑块）

```dsl
slider x=50% y=50% width=200 min=0 max=100 value=50 orientation=horizontal
```

### checkbox（复选框）

```dsl
checkbox "同意条款" x=50% y=50% checked=true
```

### progressbar（进度条）

```dsl
progressbar x=50% y=50% width=200 value=75 max=100
```

### spacer / divider

```dsl
spacer x=0 y=0 width=10 height=50
divider x=0 y=100 width=400 height=2
```

## 对话

### say

```dsl
say "文本" speaker="说话者" clickable=true template="xxx" voice="Audio/voice.mp3"
```

| 参数 | 默认 | 说明 |
|:---|:---|:---|
| `speaker` | null | 说话者（character key 或字面字符串） |
| `clickable` | false | 单词修饰符：写 `clickable` 或 `clickable=true` 启用 |
| `instant` | false | 单词修饰符：写 `instant=true` 跳过打字机 |
| `noskip` | false | 单词修饰符：写 `noskip=true` 让 Skip 模式仍需点击 |
| `typewriter` | true | 单词修饰符：写 `typewriter=true` 强制启用打字机 |
| `template` | null | 对话框模板名 |
| `voice` | null | 行内语音路径（随对话播放） |

> 注意：`clickable` / `instant` / `noskip` / `typewriter` 均为**单词修饰符**，只能写 `=true`（或省略 `=true`），**不能写 `=false`**。

### nvl

```dsl
nvl           # 进入 NVL 模式
nvl clear     # 清空文本，仍在 NVL
nvl exit      # 退出 NVL，恢复 ADV（同时自动关闭 auto 模式）
nvl auto      # 进入 NVL 并开启作用域自动推进
```

> **`nvl auto` 作用域语义**：开启后引擎自动推进 NVL 内的每条 Say（间隔由 `auto_speed` 控制），遇到 `menu` / `input` 等决策点自然停下；执行 `nvl exit` 时自动关闭自动模式。

### window

```dsl
window show   # 强制显示对话框
window hide   # 强制隐藏
window auto   # 自动模式
```

## 等待与暂停

### wait

```dsl
wait 2              # 等待 2 秒（创建回溯检查点）
wait 1.5 skipable  # 等待 1.5 秒，期间玩家可点击立即跳过
```

### pause

```dsl
pause          # 暂停并等待玩家点击继续（创建回溯检查点）
pause 2.0      # 可跳过的定时等待 2 秒（= wait 2.0 skipable）
pause 2.0 hard # 不可跳过的定时等待 2 秒（= wait 2.0）
```

## 流程控制

### navigate / jump

```dsl
navigate "target"     # 跳转，创建回溯检查点
jump "target"         # 跳转，不创建检查点
```

### scene（导航）

```dsl
scene "scene_name"    # 在 label 内使用 = 导航（清空堆栈）
```

> `scene` 在文件顶层 = 定义场景块（UI 布局）；在 label 内 = 导航命令（等同 navigate 但清空堆栈）。

### menu

```dsl
menu "提示文字"
  option "选项1" -> label1
  option "选项2" -> label2
```

也支持简写：`"选项文本" -> label`（省略 `option` 关键字）。

### input

```dsl
input "提示文字" store "变量键" [options=["选项A", "选项B"]]
```

### call / return

```dsl
call subroutine
# 子过程内
return
```

### label

```dsl
label start:        # 定义标签（冒号可省略）
jump "start"        # 跳转到标签
```

### call_screen

```dsl
call_screen "ui_scene" store="result" with "k=v,k2=v2"
```

### back / forward

```dsl
back      # 回到上一个场景
forward   # 前进到下一个场景
```

## 控制流

### if / else if / else

DSL 支持**缩进式**和**花括号式**两种块语法：

```dsl
# 缩进式（推荐）
if {condition}
  say "条件成立"
else if {other}
  say "其他条件"
else
  say "默认"
end    # 可选：格式化锚点，帮助缩进器识别块边界

# 花括号式
if {condition} {
  say "条件成立"
} else {
  say "默认"
}
```

::: tip `end` 的实际语义
`end` 在引擎中是 **no-op（已废弃）**——编译时直接跳过，不生成任何命令。块边界完全由**缩进**决定。

`end` 的唯一价值是作为**格式化锚点**：让 LSP 格式化器明确知道块在哪里结束，避免后续行被错误缩进到块内。类似 Python 的 `# endregion`。
:::

### while / break / continue

```dsl
while {condition}
  say "循环中"
  break        # 跳出循环
  continue     # 跳到下一次迭代
end
```

### for

```dsl
for "var" in {1, 2, 3}
  say "第 {var} 次"
end
```

### foreach

```dsl
foreach "var" in "array_key"
  say "{var}"
```

### switch / case / default

```dsl
switch {expr}
  case 1
    say "一"
  case 2
    say "二"
  default
    say "其他"
```

### func / return

```dsl
func name(param1, param2)
  say "参数: {param1}"
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

效果：`fade` / `crossfade` / `fadeout` / `dissolve` / `slideleft` / `slideright` / `slideup` / `slidedown` / `fadeup` / `fadedown` / `blur` / `zoomin`(或 `zoom`) / `shrink` / `blink`（大小写不敏感）

### animate

```dsl
animate "tag" property value [duration=N] [easing=EaseOutQuad]
```

属性：`x` / `y` / `opacity` / `rotate` / `scale`

缓动：`Linear` / `EaseInQuad` / `EaseOutQuad` / `EaseInOutQuad` / `EaseInCubic` / `EaseOutCubic` / `EaseInOutCubic` / `EaseInBack` / `EaseOutBack` / `EaseInOutBack` / `EaseInElastic` / `EaseOutElastic` / `EaseInOutElastic` / `EaseInBounce` / `EaseOutBounce` / `EaseInOutBounce`

### animate_block

```dsl
animate_block "target" x=100 y=200 opacity=0.5 duration=1.0 easing=EaseOutQuad
```

### style

```dsl
style "panel" background="#202030" border=2 corner=8
style dialog border=1 color="#FFFFFF"
```

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
bgm ""                 # 停止 BGM
```

### se / ambient / voice

```dsl
se "path" volume=0.5
ambient "path" volume=0.4
ambient "path" loop=false volume=0.3   # 非循环环境音
stop_ambient
stop_ambient "tag"

voice "path" volume=0.9 auto_stop=false   # 独立语音语句（单轨）
say "文本" speaker="x" voice="path"        # 随对话行内播放
stop_voice                                 # 停止当前语音
```

::: tip 语音（Voice）
语音走独立单轨通道：下一句 `say voice=` 或 `voice` 会原子替换当前语音。
`say voice=` 在回溯/前进重看时同样重播。`stop_voice` 用于对话结束后主动中断。
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
auto_save true         # 开启自动存档
auto_save false        # 关闭自动存档
save_delete "slot"     # 删除存档
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
```

### unregister_time_event / restore_time_event

```dsl
unregister_time_event "id"       # 注销时间事件
restore_time_event "id"         # 恢复已注销的时间事件
```

### time_pause / time_resume / skip_time

```dsl
time_pause
time_resume
skip_time N          # 跳过 N 分钟
```

### time_event（兼容旧语法）

```dsl
time_event day=N hour=N target="label" once=true
```

## 播放控制

```dsl
auto                 # 切换自动播放模式（与 skip 互斥）
skip                 # 切换跳过模式（与 auto 互斥）
auto_speed N         # 自动播放间隔（秒），默认 3.0
no_skip              # 禁用跳过模式
force_skip           # 强制进入跳过模式
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
cutscene "path" [skipable=true|false]  # 播放过场动画
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
