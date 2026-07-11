// ============================================================
// 第一章 - 基础剧情场景
// 演示 DSL 对话流、过渡动画、菜单选择
// ============================================================

// == 第一章入口场景 ==========================================
scene "chapter1_start"
  set "story.progress" 1
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.4
  text "第一章 · 起点" x=50% y=10% size=32 color="#FFD700" halign=center font="Microsoft YaHei"
  text "{player.name} · 金币: {player.gold} · HP: {player.hp}/{player.maxHp}" x=5% y=88% size=14 color="#666666" font="Consolas"

  // 角色定义
  character "narrator" name="旁白" color="#AAAAAA" font="Microsoft YaHei"
  character "hero" name="勇者" color="#FFD700" font="Microsoft YaHei"

  // 开场对话
  say "你踏入了这片陌生的土地..." speaker="narrator"
  say "这里就是{b}{color=#FFD700}冒险的起点{/color}{/b}。" speaker="narrator"
  say "我是谁？我为什么会在这里？" speaker="hero"
  say "风卷起落叶，没有人回答你的问题。" speaker="narrator"

  // 选择分支（menu 块不需要 end，选项缩进即可）
  menu "你要怎么做？"
    "四处看看" -> chapter1_explore
    "继续前进" -> chapter1_forward

// == 分支：四处看看 ============================================
label chapter1_explore:
  say "你环顾四周，发现了一条蜿蜒的小路。" speaker="narrator"
  say "路边的花丛中似乎有什么东西在闪烁。" speaker="narrator"
  say "那是...{color=#FFD700}金币{/color}！" speaker="hero"
  set "player.gold" {player.gold + 10}
  say "获得了 10 金币！（当前：{player.gold}）" speaker="narrator"
  say "你沿着小路继续前行。" speaker="narrator"
  jump chapter1_end

// == 分支：继续前进 ============================================
label chapter1_forward:
  say "你决定不再犹豫，沿着大路向前走去。" speaker="narrator"
  say "远处的山丘上隐约可以看到一座城镇。" speaker="narrator"
  say "那里一定有人能告诉我发生了什么。" speaker="hero"
  jump chapter1_end

// == 第一章结尾 ================================================
label chapter1_end:
  say "天色渐暗，你的冒险才刚刚开始..." speaker="narrator"
  say "（第一章完）" speaker="narrator" color="#888888"
  pause 1.5
  scene "title_main"
