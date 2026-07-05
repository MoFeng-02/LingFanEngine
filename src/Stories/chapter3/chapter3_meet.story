// == 第三章 · 相遇 ============================================
scene "chapter3_meet"
  image "Images/door_zoom.jpg" at (0, 0) size=(100%, 100%) opacity=0.4
  text "第三章 · 相遇" at (50%, 12%) size=36 color="#FFD700" align=center font="Microsoft YaHei"
  text "你在旅途中遇到了神秘的老者。" at (5%, 25%) size=22 color="#FFFFFF" font="Microsoft YaHei"
  text "他自称是这片大陆的守护者。" at (5%, 30%) size=20 color="#CCCCCC" font="Microsoft YaHei"
  say "年轻人，我看你天赋异禀……" speaker="老者"
  button "打招呼" at (20%, 55%) size=(200, 44) color="#88FF88" nav="greet"
  button "购买物品" at (50%, 55%) size=(200, 44) color="#88CCFF" nav="buy"
  button "← 返回标题" at (50%, 70%) size=(160, 42) color="#FF8888" nav="title_main"

scene "greet"
  text "打招呼" at (50%, 15%) size=30 color="#FFD700" align=center font="Microsoft YaHei"
  say "您好，老先生！" speaker="旅人"
  say "呵呵，有礼貌的年轻人。这个东西送给你。" speaker="老者"
  set "player.gold" {player.gold + 100}
  say "金币 +100" speaker="系统"
  button "← 返回" at (50%, 50%) size=(160, 42) color="#FF8888" nav="chapter3_meet"

scene "buy"
  text "购买物品" at (50%, 15%) size=30 color="#FFD700" align=center font="Microsoft YaHei"
  text "旅人金币: {player.gold}  当前 HP: {player.hp}/{player.maxHp}" at (5%, 25%) size=16 color="#FFFFFF" font="Consolas"
  if {player.gold >= 50}
    say "这是一瓶恢复药水，50 金币。" speaker="老者"
    set "player.gold" {player.gold - 50}
    set "player.hp" {player.hp + 30}
    if {player.hp > player.maxHp}
      set "player.hp" {player.maxHp}
    end
    say "HP 恢复了 30 点！" speaker="系统"
  else
    say "金币不够，去多赚点吧。" speaker="老者"
  end
  button "← 返回" at (50%, 50%) size=(160, 42) color="#FF8888" nav="chapter3_meet"
  button "回到标题" at (50%, 60%) size=(160, 42) color="#FF8888" nav="title_main"
