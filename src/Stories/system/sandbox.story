// ============================================================
// 沙盒模式 - 引擎功能测试
// ============================================================
// type=menu
scene "sandbox" type=menu
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.3
  text "沙盒模式" x=50% y=10% size=36 color="#FFD700" halign=center font="Microsoft YaHei"
  text "测试引擎各项功能" x=50% y=16% size=16 color="#AAAAAA" halign=center font="Microsoft YaHei"
  text "{player.name} · 等级: {player.level} · 金币: {player.gold} · HP: {player.hp}/{player.maxHp}" x=5% y=25% size=14 color="#FFFFFF" font="Consolas"
  text "经验: {player.exp} · 战斗次数: {sandbox.battle_count}" x=5% y=30% size=14 color="#FFFFFF" font="Consolas"
  button "金币 +50" x=15% y=42% width=140 height=40 color="#88FF88" nav="sb_add_gold" halign=center
  button "金币 -20" x=35% y=42% width=140 height=40 color="#FF8888" nav="sb_sub_gold" halign=center
  button "HP +20" x=55% y=42% width=140 height=40 color="#FF88AA" nav="sb_add_hp" halign=center
  button "升级" x=75% y=42% width=140 height=40 color="#88CCFF" nav="sb_level_up" halign=center
  button "掷骰子" x=15% y=52% width=140 height=40 color="#FFAA88" nav="sb_dice" halign=center
  button "战斗测试" x=35% y=52% width=140 height=40 color="#FF4444" nav="sb_battle" halign=center
  button "存档" x=55% y=52% width=140 height=40 color="#88FF88" nav="sb_save" halign=center
  button "读档" x=75% y=52% width=140 height=40 color="#88CCFF" nav="sb_load" halign=center
  button "循环测试" x=15% y=62% width=140 height=40 color="#AA88FF" nav="sb_while" halign=center
  button "子过程测试" x=35% y=62% width=140 height=40 color="#AA88FF" nav="sb_call" halign=center
  button "回溯测试" x=55% y=62% width=140 height=40 color="#AAFFAA" nav="sb_rollback" halign=center
  button "BGM 测试" x=75% y=62% width=140 height=40 color="#FFCCAA" nav="sb_bgm" halign=center
  button "角色模板" x=15% y=72% width=140 height=40 color="#FFAAFF" nav="sb_character" halign=center
  button "等待测试" x=35% y=72% width=140 height=40 color="#AAFFAA" nav="sb_wait" halign=center
  button "返回标题" x=50% y=80% width=160 height=42 color="#FF8888" nav="title_main" halign=center

// == 变量操作 =================================================
label sb_add_gold:
  set "player.gold" {player.gold + 50}
  say "金币 +50" speaker="系统"
  navigate "sandbox"

label sb_sub_gold:
  set "player.gold" {player.gold - 20}
  if {player.gold < 0}
    set "player.gold" 0
  say "金币 -20" speaker="系统"
  navigate "sandbox"

label sb_add_hp:
  set "player.hp" {player.hp + 20}
  if {player.hp > player.maxHp}
    set "player.hp" {player.maxHp}
  say "HP +20" speaker="系统"
  navigate "sandbox"

label sb_level_up:
  set "player.level" {player.level + 1}
  set "player.maxHp" {player.maxHp + 20}
  set "player.hp" {player.maxHp}
  say "等级提升至 {player.level}！" speaker="系统"
  navigate "sandbox"

// == 骰子（random 函数）=======================================
label sb_dice:
  let "dice" 1 once
  set "_local_dice" {random(1, 6)}
  set "sandbox.dice" {_local_dice}
  say "你掷出了 {_local_dice} 点！" speaker="系统"
  navigate "sandbox"

// == 战斗测试（random + if/else）==============================
label sb_battle:
  let "enemy_hp" 0 once
  set "_local_enemy_hp" {random(20, 50)}
  set "sandbox.battle_count" {sandbox.battle_count + 1}
  say "野怪出现！HP: {_local_enemy_hp}" speaker="系统"
  set "_local_dmg" {random(10, 30)}
  say "你造成了 {_local_dmg} 点伤害！" speaker="系统"
  set "player.hp" {player.hp - random(5, 15)}
  if {player.hp <= 0}
    set "player.hp" 50
    say "你倒下了，但恢复了 50 HP。" speaker="系统"
  else
    set "player.gold" {player.gold + random(10, 30)}
    say "你战胜了野怪！" speaker="系统"
  navigate "sandbox"

// == 存档/读档 ================================================
label sb_save:
  save "demo_slot"
  say "已保存到 demo_slot" speaker="系统"
  navigate "sandbox"

label sb_load:
  load "demo_slot"
  say "已从 demo_slot 读取" speaker="系统"

// == while 循环测试 ===========================================
label sb_while:
  say "开始 while 循环测试..." speaker="系统"
  set "_local_i" 0
  while {_local_i < 3}
    set "_local_i" {_local_i + 1}
    say "循环第 {_local_i} 次" speaker="系统"
  say "循环测试完成！" speaker="系统"
  navigate "sandbox"

// == call/return 子过程测试 ===================================
label sb_call:
  say "准备调用子过程..." speaker="系统"
  call sb_subroutine
  say "子过程已返回！" speaker="系统"
  navigate "sandbox"

label sb_subroutine:
  say "这里是子过程内部，正在执行..." speaker="系统"
  return

// == 回溯测试 =================================================
label sb_rollback:
  say "这是第一句对话，可以尝试回溯到这里。" speaker="系统"
  say "这是第二句，按 Back 键回退到上一句。" speaker="系统"
  say "这是第三句，按 Forward 键可以前进。" speaker="系统"
  say "回溯测试结束。" speaker="系统"
  navigate "sandbox"

// == BGM 测试 =================================================
label sb_bgm:
  bgm "Media/bgm_main.wav" volume=0.5
  say "BGM 播放中..." speaker="系统"
  say "再次进入沙盒会停止 BGM。" speaker="系统"
  navigate "sandbox"

// == 角色模板测试 =============================================
label sb_character:
  character "hero" name="勇者" color="#FFD700" font="Microsoft YaHei"
  character "villain" name="魔王" color="#FF4444" font="Microsoft YaHei"
  character "narrator" name="" color="#AAAAAA" font="Consolas"
  say "角色模板测试开始。" speaker="narrator"
  say "我是勇者，名字和颜色由 character 定义。" speaker="hero"
  say "哼，区区勇者也敢挑战我？" speaker="villain"
  say "say 显式参数可以覆盖角色定义。" speaker="villain" color="#00FF00"
  say "角色模板测试结束." speaker="narrator"
  navigate "sandbox"

// == 等待/暂停测试 ============================================
label sb_wait:
  say "等待/暂停测试开始。" speaker="系统"
  say "接下来等待 2 秒（不可跳过，wait 2.0）..." speaker="系统"
  wait 2.0
  say "2 秒已过。接下来等待 3 秒（可跳过，wait 3.0 skipable）——点击屏幕可跳过！" speaker="系统"
  wait 3.0 skipable
  say "可跳过等待结束。接下来 pause（等待点击）——点击屏幕继续！" speaker="系统"
  pause
  say "pause 1.5（可跳过）——点击或等 1.5 秒..." speaker="系统"
  pause 1.5
  say "pause 2.0 hard（不可跳过）——必须等 2 秒..." speaker="系统"
  pause 2.0 hard
  say "等待/暂停测试结束！" speaker="系统"
  navigate "sandbox"
