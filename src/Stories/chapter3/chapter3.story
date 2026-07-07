// ============================================================
// 第三章 - 神秘洞穴 + 三种结局
// ============================================================

// == 洞穴入口 =================================================
scene "cavern"
  set "story.progress" 4
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.7
  text "神秘洞穴" x=50% y=10% size=32 color="#FFD700" halign=center font="Microsoft YaHei"
  text "洞穴深处散发着微弱的光芒。" x=5% y=20% size=16 color="#CCCCCC" font="Microsoft YaHei"
  text "善行: {story.good_deeds} · 恶行: {story.bad_deeds}" x=5% y=88% size=12 color="#888888" font="Consolas"
  say "你走进洞穴，空气冰冷而干燥。" speaker="旁白"
  say "在洞穴的尽头，你看到了一块悬浮在空中的发光晶石。" speaker="旁白"
  say "那是一块星辰碎片——传说中蕴含着巨大力量的遗物。" speaker="旁白"
  say "晶石似乎感应到了你的到来，光芒开始剧烈跳动。" speaker="旁白"
  transition "zoomin" duration=1.0
  say "你需要做出最终的选择。" speaker="系统"
  navigate "cavern_choice"

// == 最终选择（menu + 条件结局）===============================
label cavern_choice:
  menu "你将如何对待星辰碎片？"
    "拿走它的力量" -> ending_power
    "封印它的力量" -> ending_seal
    "转身离开" -> ending_leave

// == 坏结局：拿走力量 ==========================================
label ending_power:
  set "story.ending" "power"
  set "story.bad_deeds" {story.bad_deeds + 1}
  shake intensity=20 duration=1.0
  say "你伸手触碰晶石，一股灼热的力量涌入体内。" speaker="旁白"
  say "你感到前所未有的强大，但同时，某种东西在心中消逝。" speaker="旁白"
  say "你走出洞穴时，雾气散了。小镇的人们用恐惧的目光看着你。" speaker="旁白"
  if {story.good_deeds >= 2}
    say "你曾经帮助过的人们为你感到惋惜..." speaker="旁白"
  else
    say "没有人敢接近你。你成了小镇新的传说——一个可怕的传说。" speaker="旁白"
  say "力量是诱人的，但它从来不问代价。" speaker="吟游诗人"
  say "【坏结局：力量之主】" speaker="系统"
  say "故事到此结束。返回标题可重新开始。" speaker="系统"
  scene "title_main"

// == 好结局：封印力量 ==========================================
label ending_seal:
  set "story.ending" "seal"
  set "story.good_deeds" {story.good_deeds + 1}
  transition "fade" duration=2.0
  say "你闭上眼，将双手按在晶石上，低声念出封印的咒语。" speaker="旁白"
  say "晶石的光芒逐渐黯淡，最终化为一缕青烟消散。" speaker="旁白"
  say "洞穴开始震动，你迅速跑向出口。" speaker="旁白"
  shake intensity=10 duration=0.5
  say "当你回到阳光下时，森林的雾气已经散去。" speaker="旁白"
  say "小镇重见天日，人们纷纷走上街头，惊叹不已。" speaker="旁白"
  say "你知道自己做了正确的事，尽管没有人会知道真相。" speaker="旁白"
  if {npc.innkeeper.trust >= 5}
    say "酒馆老板为你倒了一杯酒：我就知道你不是普通人。" speaker="{npc.innkeeper.name}"
  say "星辰碎片的力量不会消失，它只是回到了它该在的地方。" speaker="吟游诗人"
  say "【好结局：封印者】" speaker="系统"
  say "故事到此结束。返回标题可重新开始。" speaker="系统"
  scene "title_main"

// == 普通结局：转身离开 ========================================
label ending_leave:
  set "story.ending" "leave"
  say "你看了看晶石，最终转身离开。" speaker="旁白"
  say "有些力量，不属于凡人。" speaker="旁白"
  say "你走出洞穴，迷雾依旧。" speaker="旁白"
  say "也许有一天，会有另一个旅人来到这里，做出不同的选择。" speaker="旁白"
  if {story.wolf_defeated}
    say "至少你击退了野狼，证明了自己的勇气。" speaker="系统"
  else
    say "你带着一身疲惫回到镇上，继续你平凡的旅途。" speaker="系统"
  say "迷雾小镇依旧是那个迷雾小镇，日复一日。" speaker="吟游诗人"
  say "【普通结局：过客】" speaker="系统"
  say "故事到此结束。返回标题可重新开始。" speaker="系统"
  scene "title_main"
