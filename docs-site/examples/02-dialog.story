// ============================================================
// 教程 · 快速入门 · 第 03 章：让角色说话
// 演示 say / character / 内联标记 / 标签暂停
// ============================================================

// 预定义角色样式
character "narrator" name="旁白" color="#AAAAAA"
character "lao_zhang" name="老张" color="#FFAA00" font="Microsoft YaHei"

scene "dialog_demo" type=menu
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.3
  text "对话演示" x=50% y=10% size=36 color="#FFD700" halign=center
  button "开始对话" x=50% y=50% width=200 height=44 color="#88CCFF" nav="talk" halign=center
  button "返回" x=50% y=80% width=160 height=40 color="#FF8888" nav="title_main" halign=center

label talk:
  // 基础对话
  say "你好，旅人。" speaker="lao_zhang"
  say "这镇子不太平，你小心点。" speaker="lao_zhang"

  // 变量插值
  define "player.name" "旅人" once
  say "你叫{player.name}对吧？听说了。" speaker="lao_zhang"

  // 内联标记
  say "这是{b}粗体{/b}，这是{i}斜体{/i}。" speaker="narrator"
  say "{color=#FFD700}金色文字{/color}，{size=24}大字{/size}。" speaker="narrator"
  say "嵌套：{b}{color=#FFD700}金色粗体{/color}{/b}。" speaker="narrator"

  // 标签暂停
  say "前半句{w}后半句（点击继续）。" speaker="narrator"
  say "段落一{p}段落二（点击继续）。" speaker="narrator"

  navigate "dialog_demo"
