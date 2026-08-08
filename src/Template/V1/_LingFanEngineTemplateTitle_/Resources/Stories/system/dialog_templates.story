// ============================================================
// 对话框模板演示
// 三级优先级：say template > character screen > 全局默认
// 三种内置模板：bottom / center / fullscreen
// 含 NVL 模式 + 内联标记 + 标签暂停
// ============================================================

scene "dialog_templates" type=menu
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.25
  text "对话框模板演示" x=50% y=10% size=36 color="#FFD700" halign=center font="Microsoft YaHei"
  text "三级优先级：say template > character screen > 全局默认" x=50% y=17% size=12 color="#888888" halign=center font="Consolas"
  button "1. 底部条（默认）" x=25% y=32% width=200 height=42 color="#88CCFF" nav="tpl_bottom" halign=center
  button "2. 中央气泡" x=50% y=32% width=200 height=42 color="#FFAAFF" nav="tpl_center" halign=center
  button "3. 全屏 NVL" x=75% y=32% width=200 height=42 color="#FF88AA" nav="tpl_fullscreen" halign=center
  button "4. 角色级模板" x=25% y=42% width=200 height=42 color="#88FF88" nav="tpl_character" halign=center
  button "5. 混合切换" x=50% y=42% width=200 height=42 color="#FFCC88" nav="tpl_mixed" halign=center
  button "6. 内联标记" x=75% y=42% width=200 height=42 color="#AA88FF" nav="tpl_markup" halign=center
  button "7. NVL+Auto" x=25% y=52% width=200 height=42 color="#AAFFAA" nav="tpl_nvl_auto" halign=center
  button "返回标题" x=50% y=75% width=160 height=42 color="#FF8888" nav="title_main" halign=center

label tpl_bottom:
  say "默认底部条对话框（bottom 模板）。" speaker="系统"
  say "不指定 template 时使用全局默认。" speaker="系统"
  say "适用于标准 ADV 对话场景。" speaker="系统"
  navigate "dialog_templates"

label tpl_center:
  say "中央气泡对话框（center 模板）。" speaker="旁白" template="center"
  say "适用于内心独白、旁白、OS。" speaker="旁白" template="center"
  say "圆角半透明背景，居中显示。" speaker="旁白" template="center"
  navigate "dialog_templates"

label tpl_fullscreen:
  say "全屏对话框（fullscreen 模板）。" speaker="旁白" template="fullscreen"
  say "半透明背景，ScrollViewer 支持滚动。" speaker="旁白" template="fullscreen"
  navigate "dialog_templates"

label tpl_character:
  character "hero" name="勇者" color="#FFD700" screen="bottom"
  character "villain" name="魔王" color="#FF4444" screen="center"
  character "narrator" name="旁白" color="#AAAAAA" screen="fullscreen"
  say "我是旁白，character 定义了 screen=fullscreen。" speaker="narrator"
  say "我是勇者，screen=bottom，用默认底部条。" speaker="hero"
  say "哼，区区勇者？" speaker="villain"
  say "三级优先级：say template > character screen > 全局默认。" speaker="系统"
  navigate "dialog_templates"

label tpl_mixed:
  say "场景内可以自由切换模板。" speaker="旁白"
  say "中央气泡。" speaker="旁白" template="center"
  say "全屏。" speaker="旁白" template="fullscreen"
  say "回到默认底部条。" speaker="旁白"
  navigate "dialog_templates"

label tpl_markup:
  say "内联标记：{b}粗体{color=#FFD700}金色{/color}{/b}。" speaker="系统"
  say "{i}斜体{/i} / {u}下划线{/u} / {b}{i}粗斜体{/i}{/b}。" speaker="系统"
  say "{color=#00FF00}绿{/color} / {color=#FF4444}红{/color} / {color=#4488FF}蓝{/color}。" speaker="系统"
  navigate "dialog_templates"

label tpl_nvl_auto:
  nvl auto
  say "NVL + 自动推进组合演示。" speaker="吟游诗人" template="fullscreen"
  say "全屏模板 + NVL 累积 + auto 自动翻页。" speaker="吟游诗人" template="fullscreen"
  say "三合一效果——最适合长叙事段落。" speaker="吟游诗人" template="fullscreen"
  nvl exit
  say "✓ auto 已随 nvl exit 自动关闭。" speaker="系统"
  navigate "dialog_templates"
