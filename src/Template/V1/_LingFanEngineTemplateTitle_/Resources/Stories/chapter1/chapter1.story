// ============================================================
// 第一章 - 起点
// 演示：对话流/过渡动画/菜单选择/BGM/SE/NVL 叙事
// ============================================================

scene "chapter1_start"
  set "story.progress" 1
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.4
  text "第一章 · 起点" x=50% y=10% size=32 color="#FFD700" halign=center font="Microsoft YaHei"
  text "{player.name} · 金币: {player.gold} · HP: {player.hp}/{player.maxHp}" x=5% y=88% size=14 color="#666666" font="Consolas"

  // 角色定义
  character "narrator" name="旁白" color="#AAAAAA" font="Microsoft YaHei"
  character "hero" name="{player.name}" color="#FFD700" font="Microsoft YaHei"
  character "poet" name="吟游诗人" color="#88FF88" font="Microsoft YaHei"

  // BGM 开场
  bgm "Audio/crickets_night01.mp3" volume=0.35

  // ── NVL 开场叙事 ──
  nvl
  say "迷雾笼罩着这座小镇，没有人记得它何时出现。" speaker="poet"
  say "旅人啊，你踏着暮色而来，带着满身的尘土与疲惫。" speaker="poet"
  say "镇民们的目光躲闪而警惕，似乎在隐瞒着什么{p}" speaker="poet"
  say "而你，即将揭开这一切背后的{b}{color=#FFD700}秘密{/color}{/b}。" speaker="poet"
  nvl clear
  say "你站在小镇入口，雾气在脚边翻涌。" speaker="旁白"
  say "前方是一座陌生的城镇，空气中弥漫着潮湿的木头气味。" speaker="旁白"
  nvl exit

  // ADV 对话
  transition "fade" duration=1.0
  say "你踏入了这片陌生的土地..." speaker="narrator"
  se "Audio/chest_drawer_open.mp3" volume=0.5
  say "这里就是{b}{color=#FFD700}冒险的起点{/color}{/b}。" speaker="narrator"
  say "我是谁？我为什么会在这里？" speaker="hero"
  say "风卷起落叶，没有人回答你的问题。" speaker="narrator"

  // 选择分支
  menu "你要怎么做？"
    "四处看看" -> chapter1_explore
    "继续前进" -> chapter1_forward

label chapter1_explore:
  se "Audio/chest_drawer_open.mp3" volume=0.6
  say "你环顾四周，发现了一条蜿蜒的小路。" speaker="narrator"
  say "路边的花丛中似乎有什么东西在闪烁。" speaker="narrator"
  say "那是...{color=#FFD700}金币{/color}！" speaker="hero"
  set "player.gold" {player.gold + 10}
  say "获得了 10 金币！（当前：{player.gold}）" speaker="narrator"
  say "你沿着小路继续前行。" speaker="narrator"
  jump chapter1_end

label chapter1_forward:
  say "你决定不再犹豫，沿着大路向前走去。" speaker="narrator"
  transition "slide" duration=1.0
  say "远处的山丘上隐约可以看到一座城镇。" speaker="narrator"
  say "那里一定有人能告诉我发生了什么。" speaker="hero"
  jump chapter1_end

label chapter1_end:
  bgm ""
  say "天色渐暗，你的冒险才刚刚开始..." speaker="narrator"
  say "（第一章完）" speaker="narrator" color="#888888"
  pause 1.5
  scene "title_main"
