// ============================================================
// 教程 · 快速入门 · 第 04 章：分支选择
// 演示 menu / navigate / label / if-else / set
// ============================================================

define "npc.trust" 0 once
define "player.gold" 100 once

scene "branch_demo" type=menu
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.3
  text "分支演示" x=50% y=10% size=36 color="#FFD700" halign=center
  text "好感度: {npc.trust} · 金币: {player.gold}" x=50% y=20% size=16 color="#CCCCCC" halign=center
  button "进入场景" x=50% y=50% width=200 height=44 color="#88CCFF" nav="encounter" halign=center
  button "返回" x=50% y=80% width=160 height=40 color="#FF8888" nav="title_main" halign=center

label encounter:
  say "一位老人挡住了你的去路。" speaker="旁白"
  menu "你要怎么做？"
    "帮助老人" -> help_elder
    "无视离开" -> ignore_elder

label help_elder:
  say "谢谢你，年轻人！" speaker="老人"
  set "npc.trust" += 1
  set "player.gold" += 20
  say "好感度 +1，获得 20 金币。" speaker="系统"
  navigate "branch_demo"

label ignore_elder:
  say "你径直走过，他叹了口气。" speaker="旁白"
  set "npc.trust" -= 1
  say "好感度 -1。" speaker="系统"
  navigate "branch_demo"
