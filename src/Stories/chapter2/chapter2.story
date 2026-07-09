// ============================================================
// 第二章 - 森林冒险（入口 + 野狼 + 深处）
// ============================================================

// == 森林入口 ================================================
scene "forest_entry"
  set "story.progress" 3
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.5
  text "第二章 · 迷雾森林" x=50% y=10% size=32 color="#FFD700" halign=center font="Microsoft YaHei"
  text "树木在雾中若隐若现，脚下是松软的落叶。" x=5% y=20% size=16 color="#CCCCCC" font="Microsoft YaHei"
  text "{player.name} · HP: {player.hp}/{player.maxHp}" x=5% y=88% size=14 color="#666666" font="Consolas"
  say "森林比想象中更安静，静得有些不自然。" speaker="旁白"
  button "深入森林" x=50% y=50% width=200 height=44 color="#88CCFF" nav="forest_wolf" halign=center
  button "返回小镇" x=50% y=75% width=160 height=40 color="#FF8888" nav="town_entrance" halign=center

// == 野狼遭遇 =================================================
label forest_wolf:
  say "前方树丛中传来低沉的咆哮声..." speaker="旁白"
  say "一只野狼挡住了去路！它的眼睛在雾中闪着绿光。" speaker="旁白"
  shake intensity=8 duration=0.3
  say "你必须做出选择。" speaker="系统"
  menu "如何应对野狼？"
    "战斗" -> forest_battle
    "逃跑" -> forest_flee

// == 战斗（if/else + random + shake）=========================
label forest_battle:
  let "dmg" 0 once
  set "_local_dmg" {random(15, 25)}
  say "你拔出武器，向野狼冲去！" speaker="旁白"
  shake intensity=12 duration=0.5
  say "野狼被击中，受到 {_local_dmg} 点伤害！" speaker="系统"
  set "player.exp" {player.exp + 30}
  say "经验值 +30" speaker="系统"
  // 野狼反击
  set "_local_wolf_dmg" {random(5, 15)}
  set "player.hp" {player.hp - _local_wolf_dmg}
  say "野狼反咬一口，你损失了 {_local_wolf_dmg} 点HP！" speaker="系统"
  if {player.hp <= 0}
    set "player.hp" {player.maxHp / 2}
    say "你险些倒下，但靠意志撑住了。" speaker="旁白"
    say "HP 恢复至 {player.hp}" speaker="系统"
  else
    say "你成功击退了野狼！" speaker="旁白"
    set "story.wolf_defeated" true
    set "player.gold" {player.gold + 20}
    say "金币 +20" speaker="系统"
  say "野狼逃走了，前方的路畅通了。" speaker="旁白"
  navigate "forest_deep"

// == 逃跑 =====================================================
label forest_flee:
  say "你转身就跑，野狼在后面追了一阵后放弃了。" speaker="旁白"
  set "player.hp" {player.hp - 5}
  say "逃跑中擦伤了，HP -5" speaker="系统"
  if {player.hp <= 0}
    set "player.hp" 10
    say "你狼狈地回到入口，勉强缓过气来。" speaker="旁白"
    navigate "forest_entry"
  else
    say "你回到森林入口喘了口气。" speaker="旁白"
    say "要不要再试一次？" speaker="系统"
    navigate "forest_entry"

// == 森林深处（通往第三章）====================================
scene "forest_deep"
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.6
  text "森林深处" x=50% y=10% size=30 color="#FFD700" halign=center font="Microsoft YaHei"
  text "前方出现了一个幽暗的洞口，冷风从中吹出。" x=5% y=20% size=16 color="#CCCCCC" font="Microsoft YaHei"
  text "{player.name} · HP: {player.hp}/{player.maxHp}" x=5% y=88% size=14 color="#666666" font="Consolas"
  if {story.has_clue}
    say "这就是传闻中的洞穴。你能感觉到里面蕴含着某种力量。" speaker="旁白"
  else
    say "洞口漆黑一片，你没有任何线索，不知道里面有什么。" speaker="旁白"
    say "也许该回镇上打听一下消息。" speaker="系统"
  button "进入洞穴" x=50% y=50% width=200 height=44 color="#FFD700" nav="cavern" halign=center
  button "返回小镇" x=50% y=75% width=160 height=40 color="#FF8888" nav="town_entrance" halign=center
