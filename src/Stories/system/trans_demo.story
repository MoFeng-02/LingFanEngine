// == 过渡动画展示 ============================================
scene "trans_demo"
  text "🎬 过渡动画演示" at (640, 120) size=36 color="#FFD700" align=center font="Microsoft YaHei"
  text "点击按钮切换场景来体验过渡效果" at (640, 185) size=18 color="#AAAAAA" align=center font="Microsoft YaHei"
  button "淡入 (CrossFade)" at (640, 280) size=(200, 42) nav="do_trans_fade"
  button "左滑 (SlideLeft)" at (640, 340) size=(200, 42) nav="do_trans_slide"
  button "右滑 (SlideRight)" at (640, 400) size=(200, 42) nav="do_trans_slide_right"
  button "缩放 (ZoomIn)" at (640, 460) size=(200, 42) nav="do_trans_zoom"
  button "← 返回标题" at (640, 520) size=(160, 42) color="#88CCFF" nav="title_main"

label do_trans_fade:
  transition "fade" duration=0.8
  navigate "trans_crossfade"

label do_trans_slide:
  transition "slideleft" duration=0.6
  navigate "trans_slideleft"

label do_trans_slide_right:
  transition "slideright" duration=0.6
  navigate "trans_slideright"

label do_trans_zoom:
  transition "zoom" duration=0.5
  navigate "trans_zoomin"
