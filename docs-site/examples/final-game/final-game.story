// ============================================================
// 《灯塔守望者》— 灵泛引擎教程完整示例
// 5-10 分钟可通关的短篇视觉小说
// 单线 + 2 个分支结局
// 覆盖：场景 / 对话 / 分支 / 变量 / NVL / 内联标记 / 模板
// ============================================================

// == 全局变量初始化 ==========================================
define "player.name" "旅人" once
define "story.met_keeper" false once
define "story.trust" 0 once
define "story.helped" false once
define "story.ending" "" once

// == 角色定义 =================================================
character "narrator" name="旁白" color="#AAAAAA"
character "keeper" name="守塔人" color="#88CCFF" screen="center"
character "player" name="旅人" color="#FFD700"
character "system" name="系统" color="#88FF88"

// == 标题画面 =================================================
scene "title_main" type=menu
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.4
  text "灯塔守望者" x=50% y=15% size=56 color="#FFD700" halign=center font="Microsoft YaHei"
  text "灵泛引擎教程 · 完整示例" x=50% y=22% size=18 color="#AAAAAA" halign=center
  button "开始故事" x=50% y=45% width=240 height=48 color="#88CCFF" nav="prologue" halign=center
  button "退出" x=50% y=60% width=160 height=40 color="#FF8888" cmd="do_exit" halign=center

// == 序章：NVL 叙事 ==========================================
label prologue:
  set "story.progress" 1
  bgm "Media/bgm_main.wav" volume=0.6
  transition "fade" duration=1.5

  // NVL 模式——全屏累积文本
  nvl
  say "雨夜。{p}" speaker="narrator" template="fullscreen"
  say "你沿着泥泞的小路前行，远处灯塔的光芒在雨幕中若隐若现。" speaker="narrator" template="fullscreen"
  say "你不知道自己从哪里来，只知道必须走到那道光所在的地方。" speaker="narrator" template="fullscreen"
  say "这便是{b}{color=#FFD700}灯塔守望者{/color}{/b}的故事开始之处。" speaker="narrator" template="fullscreen"
  nvl exit

  transition "fade" duration=1.0
  navigate "lighthouse_door"

// == 第一章：灯塔门口 ========================================
scene "lighthouse_door"
  set "story.progress" 2
  bg_switch "Images/door_zoom.jpg" transition=fade duration=0.8
  text "第一章 · 灯塔" x=50% y=10% size=32 color="#FFD700" halign=center font="Microsoft YaHei"

label door_arrive:
  say "你站在灯塔的门前，雨水顺着衣角滴落。" speaker="narrator"
  say "门虚掩着，透出一丝暖黄色的光。" speaker="narrator"
  menu "你要怎么做？"
    "敲门" -> knock_door
    "直接推门进去" -> enter_door

label knock_door:
  set "story.trust" {story.trust + 1}
  say "你轻轻敲了敲门。" speaker="narrator"
  say "请进——门没锁。" speaker="keeper"
  navigate "inside_lighthouse"

label enter_door:
  say "你推开门走了进去。" speaker="narrator"
  say "哦？不请自来的客人吗。" speaker="keeper"
  say "不过没关系，这地方太久没来人了。" speaker="keeper"
  navigate "inside_lighthouse"

// == 灯塔内部 =================================================
scene "inside_lighthouse"
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.2
  text "灯塔内部" x=50% y=10% size=28 color="#FFD700" halign=center font="Microsoft YaHei"

label meet_keeper:
  set "story.met_keeper" true
  say "灯塔内部出乎意料地温暖。" speaker="narrator"
  say "壁炉里的火噼啪作响，一位老人坐在摇椅上。" speaker="narrator"
  say "欢迎，{player.name}。我等你很久了。" speaker="keeper"
  say "你……你怎么知道我的名字？" speaker="player"
  say "灯塔守望者知道每一个迷途之人的名字。" speaker="keeper"
  say "我老了，这座灯塔需要新的守望者。" speaker="keeper"
  say "你愿意留下来吗？" speaker="keeper"

  menu "你的回答是——"
    "我愿意留下来" -> choose_stay
    "我还有自己的旅程" -> choose_leave

// == 结局一：留下 ============================================
label choose_stay:
  set "story.helped" true
  set "story.trust" {story.trust + 2}
  say "我愿意留下来。" speaker="player"
  say "很好。" speaker="keeper"
  say "那么，从今以后，你便是这座灯塔的守望者。" speaker="keeper"
  say "老人站起身，将一盏铜灯递到你手中。" speaker="narrator"
  say "这盏灯，会在每一个雨夜为迷途之人指路。" speaker="keeper"
  say "你要记住——光不灭，希望就不灭。" speaker="keeper"
  transition "fade" duration=2.0
  navigate "ending_stay"

// == 结局二：离开 ============================================
label choose_leave:
  say "谢谢你的好意，但我还有自己的旅程要走。" speaker="player"
  say "老人沉默了片刻，微微点头。" speaker="narrator"
  say "我理解。每个人都有自己的路要走。" speaker="keeper"
  say "但在你离开之前，让我送你一样东西。" speaker="keeper"
  say "老人从壁炉旁取下一盏小小的铜灯。" speaker="narrator"
  say "带上它。无论你走到哪里，它都会为你照亮回家的路。" speaker="keeper"
  set "story.trust" {story.trust + 1}
  transition "fade" duration=2.0
  navigate "ending_leave"

// == 结局一画面：守望者 ======================================
scene "ending_stay" type=menu
  set "story.ending" "守望者"
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.5
  text "结局 · 守望者" x=50% y=15% size=48 color="#FFD700" halign=center font="Microsoft YaHei"

label ending_stay_show:
  nvl
  say "很多年后。" speaker="narrator" template="fullscreen"
  say "又是一个雨夜，你坐在那把摇椅上，望着窗外的海。" speaker="narrator" template="fullscreen"
  say "灯塔的光穿过雨幕，照亮远方的小路。" speaker="narrator" template="fullscreen"
  say "你知道，总会有迷途之人沿着这道光走来。" speaker="narrator" template="fullscreen"
  say "而你，会像当年的老人一样，对他们说——" speaker="narrator" template="fullscreen"
  say "{b}{color=#FFD700}「欢迎回家。」{/color}{/b}" speaker="narrator" template="fullscreen"
  nvl exit
  navigate "ending_menu"

// == 结局二画面：远行者 ======================================
scene "ending_leave" type=menu
  set "story.ending" "远行者"
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.5
  text "结局 · 远行者" x=50% y=15% size=48 color="#FFD700" halign=center font="Microsoft YaHei"

label ending_leave_show:
  nvl
  say "你带着那盏铜灯踏上了旅途。" speaker="narrator" template="fullscreen"
  say "穿过森林，翻过山岭，渡过无数河流。" speaker="narrator" template="fullscreen"
  say "无论多黑的夜，那盏灯始终亮着，温暖而稳定。" speaker="narrator" template="fullscreen"
  say "有时你会想起那座灯塔，想起那位老人。" speaker="narrator" template="fullscreen"
  say "你知道，只要你愿意，那道光永远在身后为你指路。" speaker="narrator" template="fullscreen"
  say "{b}{color=#FFD700}而前方，还有更广阔的世界等着你。{/color}{/b}" speaker="narrator" template="fullscreen"
  nvl exit
  navigate "ending_menu"

// == 结局菜单 =================================================
scene "ending_menu" type=menu
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.4
  text "故事结束" x=50% y=12% size=40 color="#FFD700" halign=center font="Microsoft YaHei"
  text "你达成了结局：{b}{color=#FFD700}{story.ending}{/color}{/b}" x=50% y=20% size=20 color="#CCCCCC" halign=center
  text "好感度：{story.trust}" x=50% y=26% size=16 color="#888888" halign=center
  button "再玩一次" x=50% y=45% width=200 height=44 color="#88CCFF" nav="title_main" halign=center
  button "退出游戏" x=50% y=60% width=160 height=40 color="#FF8888" cmd="do_exit" halign=center
