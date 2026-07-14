// ============================================================
// 过渡动画演示
// ============================================================

// == 过渡动画选择菜单 =========================================
scene "trans_demo" type=menu
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.3
  text "过渡动画演示" x=50% y=12% size=36 color="#FFD700" halign=center font="Microsoft YaHei"
  text "点击按钮体验不同的过渡效果" x=50% y=18% size=16 color="#AAAAAA" halign=center font="Microsoft YaHei"
  button "淡入 (Fade)" x=50% y=30% width=220 height=42 color="#88CCFF" nav="do_trans_fade" halign=center
  button "左滑 (SlideLeft)" x=50% y=40% width=220 height=42 color="#88FF88" nav="do_trans_slide" halign=center
  button "右滑 (SlideRight)" x=50% y=50% width=220 height=42 color="#FF88AA" nav="do_trans_slide_right" halign=center
  button "缩放 (ZoomIn)" x=50% y=60% width=220 height=42 color="#FFAA88" nav="do_trans_zoom" halign=center
  button "返回标题" x=50% y=80% width=160 height=42 color="#FF8888" nav="title_main" halign=center

// == 过渡触发 labels ==========================================
label do_trans_fade:
  transition "fade" duration=0.8
  navigate "trans_fade"

label do_trans_slide:
  transition "slideleft" duration=0.6
  navigate "trans_slide"

label do_trans_slide_right:
  transition "slideright" duration=0.6
  navigate "trans_slide_right"

label do_trans_zoom:
  transition "zoomin" duration=0.5
  navigate "trans_zoom"

// == 过渡效果展示场景 =========================================
scene "trans_fade" type=menu
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.2
  text "淡入效果 (Fade)" x=50% y=40% size=36 color="#88CCFF" halign=center font="Microsoft YaHei"
  button "返回过渡菜单" x=50% y=60% width=200 height=42 color="#88CCFF" nav="trans_demo" halign=center

scene "trans_slide" type=menu
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.2
  text "左滑效果 (SlideLeft)" x=50% y=40% size=36 color="#88FF88" halign=center font="Microsoft YaHei"
  button "返回过渡菜单" x=50% y=60% width=200 height=42 color="#88FF88" nav="trans_demo" halign=center

scene "trans_slide_right" type=menu
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.2
  text "右滑效果 (SlideRight)" x=50% y=40% size=36 color="#FF88AA" halign=center font="Microsoft YaHei"
  button "返回过渡菜单" x=50% y=60% width=200 height=42 color="#FF88AA" nav="trans_demo" halign=center

scene "trans_zoom" type=menu
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.2
  text "缩放效果 (ZoomIn)" x=50% y=40% size=36 color="#FFAA88" halign=center font="Microsoft YaHei"
  button "返回过渡菜单" x=50% y=60% width=200 height=42 color="#FFAA88" nav="trans_demo" halign=center
