// == 时间系统 ================================================
scene "show_time"
  image "Images/door_zoom.jpg" at (0, 0) size=(100%, 100%) opacity=0.3
  text "⏱ 游戏时间" at (50%, 12%) size=36 color="#FFD700" align=center font="Microsoft YaHei"
  text "当前游戏时间: 第{days}天 {hours}:{mins:00}" at (50%, 22%) size=22 color="#FFFFFF" align=center font="Consolas"
  text "游戏时间每秒自动推进" at (50%, 30%) size=16 color="#AAAAAA" align=center font="Microsoft YaHei"
  text "旅人 · 金币: {player.gold} · HP: {player.hp}/{player.maxHp}" at (5%, 75%) size=14 color="#888888" font="Consolas"
  button "← 返回标题" at (50%, 50%) size=(160, 42) color="#FF8888" nav="title_main"
