// ============================================================
// 过渡动画演示
// 展示 fade / zoomin / slide 三种内置过渡效果
// ============================================================

scene "trans_demo" type=menu
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.25
  text "过渡动画演示" x=50% y=12% size=36 color="#FFD700" halign=center font="Microsoft YaHei"
  text "20 种过渡别名 · DslTransitionNames 全对齐" x=50% y=19% size=14 color="#AAAAAA" halign=center font="Microsoft YaHei"
  button "淡入淡出" x=25% y=35% width=180 height=42 color="#88CCFF" nav="trans_fade" halign=center
  button "放大" x=50% y=35% width=180 height=42 color="#88FF88" nav="trans_zoomin" halign=center
  button "滑动" x=75% y=35% width=180 height=42 color="#FF88AA" nav="trans_slide" halign=center
  button "连续过渡" x=50% y=50% width=180 height=42 color="#FFAAFF" nav="trans_combo" halign=center
  button "震动+过渡" x=50% y=60% width=180 height=42 color="#FFAA88" nav="trans_shake" halign=center
  button "返回标题" x=50% y=80% width=160 height=42 color="#FF8888" nav="title_main" halign=center

label trans_fade:
  transition "fade" duration=1.5
  say "淡入淡出过渡完成（fade, 1.5s）。" speaker="系统"
  navigate "trans_demo"

label trans_zoomin:
  transition "zoomin" duration=1.0
  say "放大过渡完成（zoomin, 1.0s）。" speaker="系统"
  navigate "trans_demo"

label trans_slide:
  transition "slide" duration=1.0
  say "滑动过渡完成（slide, 1.0s）。" speaker="系统"
  navigate "trans_demo"

label trans_combo:
  transition "fade" duration=0.8
  say "第一段：fade 0.8s..." speaker="系统"
  transition "zoomin" duration=0.8
  say "第二段：zoomin 0.8s..." speaker="系统"
  transition "slide" duration=0.8
  say "第三段：slide 0.8s —— 连续过渡完成！" speaker="系统"
  navigate "trans_demo"

label trans_shake:
  shake intensity=15 duration=0.8
  say "屏幕震动！（intensity=15, 0.8s）" speaker="系统"
  transition "fade" duration=1.0
  say "震动 + fade 组合效果。" speaker="系统"
  navigate "trans_demo"
