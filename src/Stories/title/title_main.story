// == 标题场景 =============================================
// 入口文件，引擎启动时自动加载
define "player.name" "旅人" once
define "player.gold" 100 once
define "player.hp" 50 once
define "player.maxHp" 100 once
define "player.level" 1 once
define "player.exp" 0 once
define "sandbox.battle_count" 0 once
define "sandbox.dice" 0 once
define "npc.merchant.name" "老张" once
define "npc.merchant.favorability" 0 once
define "story.progress" 0 once

scene "title_main"
  image "Images/door_zoom.jpg" at (0, 0) size=(100%, 100%) opacity=0.5
  text "灵泛引擎" at (50%, 15%) size=56 color="#FFD700" align=center font="Microsoft YaHei"
  text "DSL 纯故事线演示" at (50%, 22%) size=18 color="#AAAAAA" align=center font="Microsoft YaHei"
  text "旅人 · 金币: {player.gold} · HP: {player.hp}/{player.maxHp}" at (50%, 28%) size=14 color="#666666" align=center font="Consolas"
  button "📖 第一章 · 初端" at (50%, 38%) size=(220, 44) color="#88CCFF" nav="chapter1_intro"
  button "🚀 第二章 · 启程" at (50%, 46%) size=(220, 44) color="#88CCFF" nav="chapter2_depart"
  button "🤝 第三章 · 相遇" at (50%, 54%) size=(220, 44) color="#88FF88" nav="chapter3_meet"
  button "⏱ 时间系统" at (20%, 65%) size=(180, 38) color="#FFAA88" nav="show_time"
  button "📜 沙盒模式" at (50%, 65%) size=(180, 38) color="#FFAA88" nav="sandbox"
  button "🎬 过渡动画" at (80%, 65%) size=(180, 38) color="#88FF88" nav="trans_demo"
  button "English" at (20%, 75%) size=(160, 36) color="#AA88FF" cmd="switch_lang" value="en"
  button "中文" at (50%, 75%) size=(160, 36) color="#AA88FF" cmd="switch_lang" value="zh"
  button "📦 let 变量测试" at (80%, 75%) size=(180, 38) color="#88FF88" nav="demo_let"

scene "demo_let"
  text "📦 let 局部变量测试" at (50%, 15%) size=30 color="#FFD700" align=center font="Microsoft YaHei"
  text "每次进入，_local_counter 初始为 0，然后 +1" at (50%, 22%) size=16 color="#AAAAAA" align=center font="Microsoft YaHei"
  let "counter" 0 once
  set "_local_counter" {_local_counter + 1}
  text "当前计数: {_local_counter}" at (50%, 30%) size=22 color="#FFFFFF" align=center font="Consolas"
  text "切换场景再回来，计数恢复为 0" at (50%, 38%) size=14 color="#888888" align=center font="Microsoft YaHei"
  button "← 返回标题" at (50%, 50%) size=(160, 42) color="#FF8888" nav="title_main"
