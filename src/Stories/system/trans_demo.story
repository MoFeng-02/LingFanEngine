// ============================================================
// 过渡动画演示
// ============================================================

scene "trans_demo" type=menu
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.3
  text "过渡动画演示" x=50% y=10% size=36 color="#FFD700" halign=center font="Microsoft YaHei"
  text "测试各种过渡效果" x=50% y=16% size=16 color="#AAAAAA" halign=center font="Microsoft YaHei"
  button "淡入淡出" x=25% y=35% width=180 height=42 color="#88CCFF" nav="trans_fade" halign=center
  button "放大" x=50% y=35% width=180 height=42 color="#88FF88" nav="trans_zoomin" halign=center
  button "滑动" x=75% y=35% width=180 height=42 color="#FF88AA" nav="trans_slide" halign=center
  button "连续过渡" x=50% y=50% width=180 height=42 color="#FFAAFF" nav="trans_combo" halign=center
  button "返回标题" x=50% y=80% width=160 height=42 color="#FF8888" nav="title_main" halign=center

label trans_fade:
  transition "fade" duration=1.5
  say "淡入淡出过渡完成。" speaker="系统"
  navigate "trans_demo"

label trans_zoomin:
  transition "zoomin" duration=1.0
  say "放大过渡完成。" speaker="系统"
  navigate "trans_demo"

label trans_slide:
  transition "slide" duration=1.0
  say "滑动过渡完成。" speaker="系统"
  navigate "trans_demo"

label trans_combo:
  transition "fade" duration=0.8
  say "第一段..." speaker="系统"
  transition "zoomin" duration=0.8
  say "第二段..." speaker="系统"
  transition "slide" duration=0.8
  say "第三段——连续过渡完成！" speaker="系统"
  navigate "trans_demo"
