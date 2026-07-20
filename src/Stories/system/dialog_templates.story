// ============================================================
// 对话框模板演示（Phase 65）
// 演示三级优先级：say template= > character screen= > 全局默认
// 演示三种内置模板：bottom（默认底部条）/ center（中央气泡）/ fullscreen（全屏 NVL）
// ============================================================

// == 模板演示菜单 =============================================
scene "dialog_templates" type=menu
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.3
  text "对话框模板演示" x=50% y=10% size=36 color="#FFD700" halign=center font="Microsoft YaHei"
  text "Phase 65 模板注册系统" x=50% y=16% size=16 color="#AAAAAA" halign=center font="Microsoft YaHei"
  text "三级优先级：say template > character screen > 全局默认" x=50% y=21% size=12 color="#888888" halign=center font="Consolas"
  button "1. 底部条（默认）" x=25% y=35% width=200 height=42 color="#88CCFF" nav="tpl_bottom" halign=center
  button "2. 中央气泡" x=50% y=35% width=200 height=42 color="#FFAAFF" nav="tpl_center" halign=center
  button "3. 全屏 NVL" x=75% y=35% width=200 height=42 color="#FF88AA" nav="tpl_fullscreen" halign=center
  button "4. 角色级模板" x=25% y=45% width=200 height=42 color="#88FF88" nav="tpl_character" halign=center
  button "5. 混合切换" x=50% y=45% width=200 height=42 color="#FFCC88" nav="tpl_mixed" halign=center
  button "6. 内联标记" x=75% y=45% width=200 height=42 color="#AA88FF" nav="tpl_markup" halign=center
  button "7. 标签暂停" x=25% y=55% width=200 height=42 color="#FFAAAA" nav="tpl_tags" halign=center
  button "8. NVL 模式" x=50% y=55% width=200 height=42 color="#AAFFAA" nav="tpl_nvl" halign=center
  button "返回标题" x=50% y=80% width=160 height=42 color="#FF8888" nav="title_main" halign=center

// == 1. 默认底部条 ============================================
label tpl_bottom:
  say "这是默认的底部条对话框（bottom 模板）。" speaker="系统"
  say "没有指定 template= 时，使用全局默认模板。" speaker="系统"
  say "全局默认由 AddDialogTemplates() 注册时设置。" speaker="系统"
  navigate "dialog_templates"

// == 2. 中央气泡（template="center"）=========================
label tpl_center:
  say "这是中央气泡对话框（center 模板）。" speaker="旁白" template="center"
  say "适用于内心独白、旁白、OS。" speaker="旁白" template="center"
  say "圆角半透明背景，居中显示，MaxWidth=600。" speaker="旁白" template="center"
  say "template= 参数只影响这一句对话。" speaker="旁白" template="center"
  navigate "dialog_templates"

// == 3. 全屏 NVL（template="fullscreen"）====================
label tpl_fullscreen:
  say "这是全屏对话框（fullscreen 模板）。" speaker="旁白" template="fullscreen"
  say "全屏半透明背景，ScrollViewer 支持滚动。" speaker="旁白" template="fullscreen"
  say "同时支持 NVL 累积模式（见第 8 项演示）。" speaker="旁白" template="fullscreen"
  navigate "dialog_templates"

// == 4. 角色级模板（character screen=）========================
label tpl_character:
  // 定义角色时绑定 screen 属性——该角色的所有对话自动使用指定模板
  character "narrator" name="旁白" color="#AAAAAA" screen="center"
  character "hero" name="勇者" color="#FFD700" screen="bottom"
  character "poet" name="诗人" color="#88FF88" screen="fullscreen"
  say "我是旁白，character 定义了 screen=center。" speaker="narrator"
  say "所以我的对话自动使用中央气泡模板，不需要每句都写 template=。" speaker="narrator"
  say "我是勇者，screen=bottom，用默认底部条。" speaker="hero"
  say "我是诗人，screen=fullscreen，用全屏模板。" speaker="poet"
  // say 显式 template= 覆盖角色 screen=
  say "这句虽然是勇者说的，但显式指定了 template=center。" speaker="hero" template="center"
  say "三级优先级：say template > character screen > 全局默认。" speaker="系统"
  navigate "dialog_templates"

// == 5. 混合切换（同一场景内不同模板）========================
label tpl_mixed:
  say "场景内可以自由切换模板——每句对话可以不同。" speaker="旁白"
  say "这句用中央气泡。" speaker="旁白" template="center"
  say "这句用全屏。" speaker="旁白" template="fullscreen"
  say "这句回到默认底部条。" speaker="旁白"
  say "模板实例会被缓存，切换时无额外创建开销。" speaker="系统"
  navigate "dialog_templates"

// == 6. 内联标记（bold/italic/color/font/size 嵌套）=========
label tpl_markup:
  say "内联标记支持嵌套：{b}粗体{color=#FFD700}金色粗体{/color}{/b}。" speaker="系统"
  say "{i}斜体{/i}、{u}下划线{/u}、{b}{i}粗斜体{/i}{/b}。" speaker="系统"
  say "{color=#00FF00}绿色{/color}、{color=#FF4444}红色{/color}、{color=#4488FF}蓝色{/color}。" speaker="系统"
  say "{size=24}大字{/size}、{size=12}小字{/size}。" speaker="系统"
  say "{font=Consolas}等宽字体{/font}、{font=Microsoft YaHei}雅黑{/font}。" speaker="系统"
  say "内联标记在所有模板中通用——由 DialogEngine 统一处理。" speaker="系统"
  navigate "dialog_templates"

// == 7. 标签暂停（{w} 和 {p}）=================================
label tpl_tags:
  say "{w} 标签暂停打字机，点击后继续。{w}这里暂停了，点击继续。" speaker="系统"
  say "{p} 标签也暂停，但通常用于段落分隔。{p}点击继续到下一句。" speaker="系统"
  say "{fast} 标签跳过剩余打字机效果，直接显示到当前位置。" speaker="系统" template="center"
  say "标签暂停在所有模板中都有效。" speaker="系统"
  navigate "dialog_templates"

// == 8. NVL 模式 + fullscreen 模板 ============================
label tpl_nvl:
  nvl
  say "NVL 模式下，对话文本会累积显示。" speaker="吟游诗人" template="fullscreen"
  say "每一句都追加到同一个文本框中。" speaker="吟游诗人" template="fullscreen"
  say "适合长段叙事和内心独白。" speaker="吟游诗人" template="fullscreen"
  say "{b}{color=#FFD700}星辰碎片{/color}{/b}的传说由此展开..." speaker="吟游诗人" template="fullscreen"
  nvl clear
  say "nvl clear 清空累积文本，但仍在 NVL 模式。" speaker="系统" template="fullscreen"
  say "新的对话继续累积。" speaker="系统" template="fullscreen"
  nvl exit
  say "nvl exit 退出 NVL 模式，恢复 ADV 模式。" speaker="系统"
  navigate "dialog_templates"
