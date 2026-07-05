// == 战斗 ================================================
scene "battle"
  image "Images/door_zoom.jpg" at (0, 0) size=(100%, 100%) opacity=0.5
  text "⚔ 战斗！" at (50%, 12%) size=30 color="#FF4444" align=center font="Microsoft YaHei"
  text "旅人 HP: {player.hp}/{player.maxHp}" at (5%, 25%) size=18 color="#FFFFFF" font="Consolas"
  say "野狼扑了过来！" speaker="系统"
  set "player.hp" {player.hp - 15}
  say "你受到了 15 点伤害！" speaker="系统"
  if {player.hp <= 0}
    say "你倒下了……" speaker="旁白"
    set "player.hp" 50
    back
    say "你惊醒过来，发现是一场梦。" speaker="旅人"
  else
    say "你击退了野狼！" speaker="系统"
    set "player.gold" {player.gold + 30}
    say "获得了 30 金币！" speaker="系统"
    forward
    say "你继续前行。" speaker="旅人"
  end
  set "player.exp" {player.exp + 50}
  say "经验值 +50" speaker="系统"
  button "继续前进" at (25%, 55%) size=(200, 44) color="#88CCFF" nav="chapter2_advancing"
  button "返回标题" at (55%, 55%) size=(200, 44) color="#FF8888" nav="title_main"
