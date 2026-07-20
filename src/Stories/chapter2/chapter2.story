// ============================================================
// 第二章 - 森林探索 + 战斗
// ============================================================

// == 森林入口 =================================================
scene "forest_entry"
  set "story.progress" 3
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.5
  text "第二章 · 迷雾森林" x=50% y=10% size=32 color="#FFD700" halign=center font="Microsoft YaHei"
  text "森林深处传来野兽的低吼。" x=5% y=20% size=16 color="#CCCCCC" font="Microsoft YaHei"
  text "HP: {player.hp}/{player.maxHp} · 金币: {player.gold}" x=5% y=88% size=12 color="#888888" font="Consolas"
  button "深入森林" x=50% y=40% width=200 height=44 color="#88CCFF" nav="forest_deep" halign=center
  button "返回小镇" x=50% y=75% width=160 height=40 color="#FF8888" nav="town_entrance" halign=center

// == 森林深处 + 野狼遭遇 =====================================
label forest_deep:
  say "你穿过茂密的树林，脚下踩着枯枝发出清脆的响声。" speaker="旁白"
  say "突然，一声低沉的咆哮从前方传来。" speaker="旁白"
  say "一只野狼挡住了你的去路！" speaker="系统"
  menu "你要怎么做？"
    "战斗" -> wolf_battle
    "逃跑" -> wolf_flee

// == 野狼战斗 =================================================
label wolf_battle:
  set "_local_wolf_hp" {random(30, 50)}
  say "野狼 HP: {_local_wolf_hp}" speaker="系统"
  say "你拔出武器，与野狼对峙。" speaker="旁白"
  set "_local_dmg" {random(15, 35)}
  say "你挥出一剑，造成了 {_local_dmg} 点伤害！" speaker="系统"
  set "_local_wolf_hp" {_local_wolf_hp - _local_dmg}
  if {_local_wolf_hp <= 0}
    say "野狼倒下了。" speaker="旁白"
    say "你获得了 30 经验值和 25 金币！" speaker="系统"
    set "player.exp" {player.exp + 30}
    set "player.gold" {player.gold + 25}
    set "story.wolf_defeated" true
    say "你继续深入森林..." speaker="旁白"
    navigate "cavern_entry"
  else
    set "_local_wolf_dmg" {random(5, 15)}
    say "野狼扑上来咬了你一口，造成 {_local_wolf_dmg} 点伤害！" speaker="系统"
    set "player.hp" {player.hp - _local_wolf_dmg}
    if {player.hp <= 0}
      set "player.hp" 1
      say "你差点倒下，但勉强站住了。" speaker="旁白"
    say "野狼还在，HP 剩余 {_local_wolf_hp}。" speaker="系统"
    say "你再次挥剑——" speaker="旁白"
    set "_local_dmg" {random(20, 40)}
    set "_local_wolf_hp" {_local_wolf_hp - _local_dmg}
    say "造成了 {_local_dmg} 点伤害！" speaker="系统"
    if {_local_wolf_hp <= 0}
      say "野狼终于倒下了。" speaker="旁白"
      set "player.exp" {player.exp + 30}
      set "player.gold" {player.gold + 25}
      set "story.wolf_defeated" true
      say "你获得了 30 经验值和 25 金币！" speaker="系统"
      navigate "cavern_entry"
    else
      say "野狼逃走了，你松了口气。" speaker="旁白"
      set "story.wolf_defeated" false
      navigate "cavern_entry"

// == 逃跑 =====================================================
label wolf_flee:
  set "player.hp" {player.hp - 10}
  if {player.hp < 0}
    set "player.hp" 1
  say "你转身就跑，野狼追了一段就放弃了。" speaker="旁白"
  say "逃跑中受了点轻伤，HP -10。" speaker="系统"
  say "你继续深入森林..." speaker="旁白"
  navigate "cavern_entry"

// == 洞穴入口（进入第三章）====================================
label cavern_entry:
  say "穿过森林，你看到了一个幽暗的洞口。" speaker="旁白"
  say "洞口散发着微弱的蓝色光芒。" speaker="旁白"
  if {story.has_clue}
    say "这就是酒馆老板说的那个洞穴。" speaker="旁白"
  else
    say "你有一种不祥的预感，但好奇心驱使你前进。" speaker="旁白"
  say "你走进了洞穴..." speaker="旁白"
  transition "fade" duration=1.5
  navigate "cavern"
