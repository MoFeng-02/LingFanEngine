// ============================================================
// 迷雾小镇 - 标题画面 + 序章
// 全局玩家属性在 title_main 场景 define once 初始化
// ============================================================

// == 标题场景 ================================================
scene "title_main" type=menu
  define "player.name" "旅人" once
  define "player.hp" 50 once
  define "player.maxHp" 100 once
  define "player.gold" 100 once
  define "player.level" 1 once
  define "player.exp" 0 once
  define "story.progress" 0 once
  define "story.met_innkeeper" false once
  define "story.met_elder" false once
  define "story.has_clue" false once
  define "story.good_deeds" 0 once
  define "story.bad_deeds" 0 once
  define "story.wolf_defeated" false once
  define "story.ending" "" once
  define "npc.innkeeper.name" "老张" once
  define "npc.innkeeper.trust" 0 once
  define "sandbox.battle_count" 0 once
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.5
  text "迷雾小镇" x=50% y=15% size=56 color="#FFD700" halign=center font="Microsoft YaHei"
  text "灵泛引擎 DSL 演示" x=50% y=22% size=18 color="#AAAAAA" halign=center font="Microsoft YaHei"
  text "{player.name} · 金币: {player.gold} · HP: {player.hp}/{player.maxHp}" x=50% y=28% size=14 color="#666666" halign=center font="Consolas"
  button "开始故事" x=50% y=38% width=240 height=48 color="#88CCFF" nav="prologue"
  button "沙盒模式" x=50% y=48% width=200 height=42 color="#FFAA88" nav="sandbox"
  button "过渡动画演示" x=50% y=58% width=200 height=42 color="#88FF88" nav="trans_demo"

// == 序章（NVL 叙事）========================================
label prologue:
  set "story.progress" 1
  bgm "Media/bgm_main.wav" volume=0.7
  transition "fade" duration=1.5
  nvl
  say "迷雾笼罩着这座小镇，没有人记得它何时出现。" speaker="吟游诗人"
  say "旅人啊，你踏着暮色而来，带着满身的尘土与疲惫。" speaker="吟游诗人"
  say "镇民们的目光躲闪而警惕，似乎在隐瞒着什么{p}" speaker="吟游诗人"
  say "而你，即将揭开这一切背后的{b}{color=#FFD700}秘密{/color}{/b}。" speaker="吟游诗人"
  nvl clear
  say "你站在小镇入口，雾气在脚边翻涌。" speaker="旁白"
  say "前方是一座陌生的城镇，空气中弥漫着潮湿的木头气味。" speaker="旁白"
  navigate "town_entrance"
