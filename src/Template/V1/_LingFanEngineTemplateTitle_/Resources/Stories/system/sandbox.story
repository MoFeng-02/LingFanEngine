// ============================================================
// 沙盒模式 - 引擎功能测试
// 变量/战斗/循环/子过程/回溯/BGM/角色模板/等待/对话框
// ============================================================

scene "sandbox" type=menu
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.3
  text "沙盒模式" x=50% y=8% size=36 color="#FFD700" halign=center font="Microsoft YaHei"
  text "测试引擎各项功能" x=50% y=15% size=16 color="#AAAAAA" halign=center font="Microsoft YaHei"
  text "{player.name} · 等级: {player.level} · 金币: {player.gold} · HP: {player.hp}/{player.maxHp}" x=5% y=24% size=13 color="#FFFFFF" font="Consolas"
  text "经验: {player.exp} · 战斗次数: {sandbox.battle_count}" x=5% y=29% size=13 color="#FFFFFF" font="Consolas"
  button "金币 +50" x=15% y=40% width=130 height=38 color="#88FF88" nav="sb_add_gold" halign=center
  button "金币 -20" x=35% y=40% width=130 height=38 color="#FF8888" nav="sb_sub_gold" halign=center
  button "HP +20" x=55% y=40% width=130 height=38 color="#FF88AA" nav="sb_add_hp" halign=center
  button "升级" x=75% y=40% width=130 height=38 color="#88CCFF" nav="sb_level_up" halign=center
  button "掷骰子" x=15% y=49% width=130 height=38 color="#FFAA88" nav="sb_dice" halign=center
  button "战斗测试" x=35% y=49% width=130 height=38 color="#FF4444" nav="sb_battle" halign=center
  button "存档/读档" x=55% y=49% width=130 height=38 color="#88FF88" nav="sb_save_load" halign=center
  button "BGM 测试" x=75% y=49% width=130 height=38 color="#FFCCAA" nav="sb_bgm" halign=center
  button "循环测试" x=15% y=58% width=130 height=38 color="#AA88FF" nav="sb_while" halign=center
  button "子过程" x=35% y=58% width=130 height=38 color="#AA88FF" nav="sb_call" halign=center
  button "回溯测试" x=55% y=58% width=130 height=38 color="#AAFFAA" nav="sb_rollback" halign=center
  button "等待测试" x=75% y=58% width=130 height=38 color="#AAFFAA" nav="sb_wait" halign=center
  button "角色模板" x=25% y=67% width=130 height=38 color="#FFAAFF" nav="sb_character" halign=center
  button "SE 音效" x=50% y=67% width=130 height=38 color="#FFCC88" nav="sb_se" halign=center
  button "返回标题" x=50% y=80% width=140 height=38 color="#FF8888" nav="title_main" halign=center

label sb_add_gold:
  set "player.gold" {player.gold + 50}
  say "金币 +50（当前：{player.gold}）" speaker="系统"
  navigate "sandbox"

label sb_sub_gold:
  set "player.gold" {player.gold - 20}
  if {player.gold < 0}
    set "player.gold" 0
  say "金币 -20（当前：{player.gold}）" speaker="系统"
  navigate "sandbox"

label sb_add_hp:
  set "player.hp" {player.hp + 20}
  if {player.hp > player.maxHp}
    set "player.hp" {player.maxHp}
  say "HP +20（当前：{player.hp}/{player.maxHp}）" speaker="系统"
  navigate "sandbox"

label sb_level_up:
  set "player.level" {player.level + 1}
  set "player.maxHp" {player.maxHp + 20}
  set "player.hp" {player.maxHp}
  say "等级提升至 {player.level}！HP 上限 +20" speaker="系统"
  navigate "sandbox"

label sb_dice:
  set "_local_dice" {random(1, 6)}
  say "你掷出了 {_local_dice} 点！" speaker="系统"
  navigate "sandbox"

label sb_battle:
  set "_local_enemy_hp" {random(20, 50)}
  set "sandbox.battle_count" {sandbox.battle_count + 1}
  se "Audio/chest_drawer_open.mp3" volume=0.7
  say "野怪出现！HP: {_local_enemy_hp}" speaker="系统"
  set "_local_dmg" {random(10, 30)}
  say "你造成了 {_local_dmg} 点伤害！" speaker="系统"
  set "_local_enemy_hp" {_local_enemy_hp - _local_dmg}
  if {_local_enemy_hp <= 0}
    say "野怪倒下！+30 经验，+25 金币" speaker="系统"
    set "player.exp" {player.exp + 30}
    set "player.gold" {player.gold + 25}
  else
    set "player.hp" {player.hp - random(5, 15)}
    if {player.hp <= 0}
      set "player.hp" 50
      say "你倒下了，恢复 50 HP。" speaker="系统"
    else
      say "野狼逃走。" speaker="系统"
  navigate "sandbox"

label sb_save_load:
  save "demo_slot"
  say "已保存到 demo_slot" speaker="系统"
  say "现在修改金币..." speaker="系统"
  set "player.gold" {player.gold + 999}
  say "金币临时 +999（当前：{player.gold}）" speaker="系统"
  load "demo_slot"
  say "已读档——金币应恢复到保存时的值：{player.gold}" speaker="系统"
  navigate "sandbox"

label sb_bgm:
  bgm "Audio/crickets_night01.mp3" volume=0.5
  say "♪ BGM 播放中..." speaker="系统"
  say "返回沙盒会停止 BGM。" speaker="系统"
  navigate "sandbox"

label sb_se:
  se "Audio/chest_drawer_open.mp3" volume=0.9
  say "✓ SE 音效播放（抽屉打开声）" speaker="系统"
  navigate "sandbox"

label sb_while:
  say "while 循环测试：" speaker="系统"
  set "_local_i" 0
  while {_local_i < 3}
    set "_local_i" {_local_i + 1}
    say "  循环第 {_local_i} 次" speaker="系统"
  say "循环完成！" speaker="系统"
  navigate "sandbox"

label sb_call:
  say "调用子过程..." speaker="系统"
  call sb_subroutine
  say "子过程已返回！" speaker="系统"
  navigate "sandbox"

label sb_subroutine:
  say "  → 子过程内部执行中..." speaker="系统"
  return

label sb_rollback:
  say "第一句——可回溯到这里。" speaker="系统"
  say "第二句——按 Back 回退。" speaker="系统"
  say "第三句——按 Forward 前进。" speaker="系统"
  navigate "sandbox"

label sb_wait:
  say "等待/暂停测试开始。" speaker="系统"
  say "wait 2.0（不可跳过）..." speaker="系统"
  wait 2.0
  say "wait 3.0 skipable（点击可跳过）..." speaker="系统"
  wait 3.0 skipable
  say "pause（等点击）..." speaker="系统"
  pause
  say "测试结束！" speaker="系统"
  navigate "sandbox"

label sb_character:
  character "hero" name="勇者" color="#FFD700" font="Microsoft YaHei"
  character "villain" name="魔王" color="#FF4444" font="Microsoft YaHei"
  character "narrator" name="" color="#AAAAAA" font="Consolas"
  say "角色模板测试。" speaker="narrator"
  say "我是勇者。" speaker="hero"
  say "区区勇者？" speaker="villain"
  say "覆盖颜色。" speaker="villain" color="#00FF00"
  navigate "sandbox"
