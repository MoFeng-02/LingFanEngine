// == 沙盒模式 ================================================
scene "sandbox"
  image "Images/door_zoom.jpg" at (0, 0) size=(100%, 100%) opacity=0.3
  text "📜 沙盒模式" at (50%, 12%) size=36 color="#FFD700" align=center font="Microsoft YaHei"
  text "自由测试各种 DSL 功能" at (50%, 18%) size=16 color="#AAAAAA" align=center font="Microsoft YaHei"
  text "旅人 · 金币: {player.gold} · HP: {player.hp}/{player.maxHp}" at (5%, 25%) size=14 color="#FFFFFF" font="Consolas"
  text "等级: {player.level}  经验: {player.exp}" at (5%, 30%) size=14 color="#FFFFFF" font="Consolas"
  text "游戏时间: 第{days}天 {hours}:{mins:00}" at (5%, 35%) size=14 color="#888888" font="Consolas"
  button "💰 加 50 金币" at (15%, 45%) size=(160, 42) color="#88FF88" cmd="do_sandbox_add_gold"
  button "💸 减 20 金币" at (38%, 45%) size=(160, 42) color="#FF8888" cmd="do_sandbox_sub_gold"
  button "❤ 加 20 HP" at (62%, 45%) size=(160, 42) color="#FF88AA" cmd="do_sandbox_add_hp"
  button "⬆ 升级" at (85%, 45%) size=(160, 42) color="#88CCFF" cmd="do_sandbox_level_up"
  button "🎲 掷骰子" at (15%, 55%) size=(160, 42) color="#FFAA88" cmd="do_sandbox_roll_dice"
  button "⚔ 刷怪" at (38%, 55%) size=(160, 42) color="#FF4444" cmd="do_sandbox_battle"
  button "💾 存档" at (62%, 55%) size=(160, 42) color="#88FF88" cmd="do_save"
  button "📂 读档" at (85%, 55%) size=(160, 42) color="#88CCFF" cmd="do_load"
  button "🎵 播放 BGM" at (15%, 65%) size=(160, 42) color="#AA88FF" cmd="play_bgm" value="Media/bgm_main.wav"
  button "🔇 停止 BGM" at (38%, 65%) size=(160, 42) color="#AA88FF" cmd="stop_bgm"
  button "← 返回标题" at (50%, 78%) size=(160, 42) color="#FF8888" nav="title_main"

label do_sandbox_add_gold:
  set "player.gold" {player.gold + 50}
  say "金币 +50" speaker="系统"
  navigate "sandbox"

label do_sandbox_sub_gold:
  set "player.gold" {player.gold - 20}
  if {player.gold < 0}
    set "player.gold" 0
  end
  say "金币 -20" speaker="系统"
  navigate "sandbox"

label do_sandbox_add_hp:
  set "player.hp" {player.hp + 20}
  if {player.hp > player.maxHp}
    set "player.hp" {player.maxHp}
  end
  say "HP +20" speaker="系统"
  navigate "sandbox"

label do_sandbox_level_up:
  set "player.level" {player.level + 1}
  set "player.maxHp" {player.maxHp + 20}
  set "player.hp" {player.maxHp}
  say "等级提升到 {player.level}！" speaker="系统"
  navigate "sandbox"

label do_sandbox_roll_dice:
  let "dice" 1 once
  set "_local_dice" {random(1, 6)}
  say "🎲 掷出了 {_local_dice} 点" speaker="系统"
  navigate "sandbox"

label do_sandbox_battle:
  let "enemy_hp" 1 once
  set "_local_enemy_hp" {random(20, 50)}
  say "野狼出现了！HP: {_local_enemy_hp}" speaker="系统"
  set "sandbox.battle_count" {sandbox.battle_count + 1}
  set "player.hp" {player.hp - random(5, 15)}
  if {player.hp <= 0}
    set "player.hp" 50
    say "你倒下了……但被救了回来。" speaker="系统"
  else
    set "player.gold" {player.gold + random(10, 30)}
    say "你战胜了野狼！" speaker="系统"
  end
  navigate "sandbox"

label do_sandbox_while_test:
  say "开始 while 循环测试" speaker="系统"
  set "sandbox.test_count" 0
  let "i" 0 once
  while {_local_i < 3}
    set "_local_i" {_local_i + 1}
    set "sandbox.test_count" {sandbox.test_count + 1}
    say "循环第 {_local_i} 次" speaker="系统"
  end
  say "循环结束，共 {sandbox.test_count} 次" speaker="系统"
  navigate "sandbox"

// ========== call/return 测试 ==========
label do_sandbox_call_test:
  say "准备调用子过程" speaker="系统"
  call do_sub_test
  say "子过程返回了！" speaker="系统"
  navigate "sandbox"

label do_sub_test:
  say "我是子过程，执行完后会自动返回" speaker="系统"
  return

// ========== show/hide/background 测试 ==========
label do_sandbox_show:
  show "player" at (200, 300)
  say "显示了 player" speaker="系统"
  navigate "sandbox"

label do_sandbox_hide:
  hide "player"
  say "隐藏了 player" speaker="系统"
  navigate "sandbox"

label do_sandbox_bg:
  background "Images/door_zoom.jpg"
  say "切换了背景图片" speaker="系统"
  navigate "sandbox"

// ========== animate 控件动画 ==========
label do_sandbox_animate:
  animate "player" x 500 duration=1.0 easing=EaseOutBounce
  say "player 移动到 x=500" speaker="系统"
  navigate "sandbox"

// ========== menu 菜单 ==========
label do_sandbox_menu:
  say "你想做什么？" speaker="系统"
  menu "选择行动"
  navigate "sandbox"
