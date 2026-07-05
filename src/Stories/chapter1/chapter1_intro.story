// == 第一章 · 初端 ==========================================
define "story.progress" 0 once

scene "chapter1_intro"
  image "Images/door_zoom.jpg" at (0, 0) size=(100%, 100%) opacity=0.4
  text "第一章 · 初端" at (50%, 12%) size=36 color="#FFD700" align=center font="Microsoft YaHei"
  text "你站在一个古老的村落前。" at (5%, 25%) size=22 color="#FFFFFF" font="Microsoft YaHei"
  text "风吹过麦田，带来远方的气息。" at (5%, 30%) size=20 color="#CCCCCC" font="Microsoft YaHei"
  text "前方有三条路可选。" at (5%, 35%) size=18 color="#AAAAAA" font="Microsoft YaHei"
  text "当前进度: 第{story.progress}步" at (5%, 40%) size=16 color="#88FF88" font="Consolas"
  button "左边的小路" at (25%, 55%) size=(200, 44) color="#88CCFF" nav="do_path1"
  button "右边的岔路" at (55%, 55%) size=(200, 44) color="#88FF88" nav="do_path2"
  button "← 返回标题" at (50%, 70%) size=(160, 42) color="#FF8888" nav="title_main"

label do_path1:
  say "你走进左边的小路，发现了一些金币！" speaker="旁白"
  set "story.progress" {story.progress + 1}
  set "player.gold" {player.gold + 20}
  say "金币增加了 20" speaker="系统"
  navigate "path1"
  end

label do_path2:
  say "右边的岔路似乎有危险的气息……" speaker="旁白"
  set "story.progress" {story.progress + 1}
  say "你被荆棘划伤了！" speaker="系统"
  set "player.hp" {player.hp - 10}
  if {player.hp <= 0}
    say "你昏迷了过去……" speaker="旁白"
    set "player.hp" {player.maxHp / 2}
  end
  navigate "path2"
  end
  end
