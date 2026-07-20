// ============================================================
// 教程 · 快速入门 · 第 02 章：第一个场景
// 一个完整的标题画面：背景图 + 标题 + 按钮
// ============================================================

scene "title_main" type=menu
  // 背景图（opacity 控制透明度）
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.3

  // 标题
  text "我的第一个游戏" x=50% y=15% size=56 color="#FFD700" halign=center font="Microsoft YaHei"
  text "灵泛引擎教程" x=50% y=22% size=18 color="#AAAAAA" halign=center

  // 按钮（nav 跳转到标签）
  button "开始故事" x=50% y=45% width=240 height=48 color="#88CCFF" nav="intro" halign=center
  button "退出游戏" x=50% y=60% width=200 height=40 color="#FF8888" cmd="do_exit" halign=center

// 带布局容器的菜单示例
label intro:
  transition "fade" duration=1.0
  say "你点击了开始故事！" speaker="旁白"
  navigate "title_main"
