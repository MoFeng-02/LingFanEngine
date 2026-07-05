// == 分岔路结果 ============================================
define "story.progress" 0 once

scene "path1"
  image "Images/door_zoom.jpg" at (0, 0) size=(100%, 100%) opacity=0.3
  text "左边的小路" at (50%, 15%) size=30 color="#FFD700" align=center font="Microsoft YaHei"
  text "你发现了一些闪闪发光的金币！" at (5%, 25%) size=22 color="#FFFFFF" font="Microsoft YaHei"
  text "金币 +20" at (5%, 32%) size=18 color="#88FF88" font="Consolas"
  say "你小心地将金币收进口袋。" speaker="旅人"
  button "返回村落" at (25%, 50%) size=(200, 44) color="#88CCFF" nav="chapter1_intro"
  button "继续探索" at (55%, 50%) size=(200, 44) color="#88FF88" nav="chapter2_depart"
  button "← 返回标题" at (50%, 65%) size=(160, 42) color="#FF8888" nav="title_main"

scene "path2"
  image "Images/door_zoom.jpg" at (0, 0) size=(100%, 100%) opacity=0.3
  text "右边的岔路" at (50%, 15%) size=30 color="#FFD700" align=center font="Microsoft YaHei"
  text "你狼狈地回到村落。" at (5%, 25%) size=22 color="#FFFFFF" font="Microsoft YaHei"
  text "HP -10" at (5%, 32%) size=18 color="#FF8888" font="Consolas"
  say "得小心一点了……" speaker="旅人"
  button "返回村落" at (25%, 50%) size=(200, 44) color="#88CCFF" nav="chapter1_intro"
  button "直接出发" at (55%, 50%) size=(200, 44) color="#88FF88" nav="chapter2_depart"
  button "← 返回标题" at (50%, 65%) size=(160, 42) color="#FF8888" nav="title_main"
