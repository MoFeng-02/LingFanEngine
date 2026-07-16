// ============================================================
// 模板项目 - 标题画面（首页）
// 左侧菜单栏布局：左侧垂直按钮列 + 右侧主内容区
// 全局玩家属性在 title_main 场景 define once 初始化
// ============================================================
define "player.name" "白狐" once
scene "title_main" type=menu
  // ── 全局变量初始化（"你不认识他之前，他不存在于你的世界"）──
  
  define "player.hp" 50 once
  define "player.maxHp" 100 once
  define "player.gold" 100 once
  define "story.progress" 0 once

  // ── 背景 ──
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.3

  // ── 左侧菜单栏背景（无边框，半透明黑色底）──
  panel "" x=0 y=0 width=220 height=100% halign=left borderThickness=0

  // ── 左侧菜单标题 ──
  text "主菜单" x=20 y=30 size=24 color="#FFD700" font="Microsoft YaHei" halign=left valign=top

  // ── 左侧菜单按钮（垂直排列，halign=left 防止居中，valign=top 防止垂直居中）──
  button "开始故事" x=20 y=90 width=180 height=44 color="#88CCFF" nav="chapter1_start" halign=left valign=top
  button "继续游戏" x=20 y=144 width=180 height=44 color="#88FF88" cmd="continue_game" halign=left valign=top
  button "读取存档" x=20 y=198 width=180 height=44 color="#FFCC88" cmd="open_load" halign=left valign=top
  button "保存进度" x=20 y=252 width=180 height=44 color="#88FF88" cmd="open_save" halign=left valign=top
  button "游戏设置" x=20 y=306 width=180 height=44 color="#AA88FF" cmd="open_settings" halign=left valign=top
  button "对话历史" x=20 y=360 width=180 height=44 color="#FFAAFF" cmd="open_history" halign=left valign=top
  button "CG 鉴赏" x=20 y=414 width=180 height=44 color="#FFAAFF" cmd="open_gallery" halign=left valign=top
  button "关于" x=20 y=468 width=180 height=44 color="#AAAAAA" nav="about" halign=left valign=top
  button "退出游戏" x=20 y=560 width=180 height=44 color="#FF8888" cmd="do_exit" halign=left valign=top

  // ── 右侧主内容区 ──
  text "游戏标题" x=60% y=20% size=64 color="#FFD700" halign=center font="Microsoft YaHei"
  text "灵泛引擎模板项目" x=60% y=30% size=22 color="#AAAAAA" halign=center font="Microsoft YaHei"
  text "基于 C# + Avalonia 的视觉小说引擎" x=60% y=36% size=16 color="#888888" halign=center font="Microsoft YaHei"

  // ── 状态信息 ──
  text "{player.name} · 金币: {player.gold} · HP: {player.hp}/{player.maxHp}" x=60% y=50% size=14 color="#666666" halign=center font="Consolas"
  text "故事进度: {story.progress}" x=60% y=55% size=14 color="#666666" halign=center font="Consolas"

  // ── 底部提示 ──
  text "Space/Enter=推进  Esc=关闭面板  右键=快捷菜单  滚轮=回溯/前进  Ctrl+S=存档  Ctrl+L=读档  F11=全屏" x=50% y=92% size=11 color="#555555" halign=center font="Consolas"
