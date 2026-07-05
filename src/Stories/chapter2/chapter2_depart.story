// == 第二章 · 启程 ============================================
scene "chapter2_depart"
  image "Images/door_zoom.jpg" at (0, 0) size=(100%, 100%) opacity=0.4
  text "第二章 · 启程" at (50%, 12%) size=36 color="#FFD700" align=center font="Microsoft YaHei"
  text "你来到了新的区域——翠绿平原。" at (5%, 25%) size=22 color="#FFFFFF" font="Microsoft YaHei"
  text "前方是一片开阔的草原，远处有战斗的痕迹。" at (5%, 30%) size=20 color="#CCCCCC" font="Microsoft YaHei"
  say "这里就是翠绿平原吗？看起来并不平静。" speaker="旅人"
  button "继续前进" at (25%, 55%) size=(200, 44) color="#88CCFF" nav="chapter2_advancing"
  button "← 返回标题" at (50%, 70%) size=(160, 42) color="#FF8888" nav="title_main"

scene "chapter2_advancing"
  image "Images/door_zoom.jpg" at (0, 0) size=(100%, 100%) opacity=0.5
  text "深入探索" at (50%, 12%) size=30 color="#FFD700" align=center font="Microsoft YaHei"
  text "前方出现了敌人！" at (5%, 25%) size=22 color="#FFFFFF" font="Microsoft YaHei"
  text "一只野狼挡住了去路。" at (5%, 30%) size=20 color="#CCCCCC" font="Microsoft YaHei"
  say "看来不打倒它是不行了。" speaker="旅人"
  label start_battle:
  say "野狼冲了过来！" speaker="系统"
  back
  say "你回到了安全位置。" speaker="系统"
  button "⚔ 迎战" at (25%, 55%) size=(200, 44) color="#FF8888" nav="battle"
  button "← 返回" at (50%, 70%) size=(160, 42) color="#FF8888" nav="title_main"
