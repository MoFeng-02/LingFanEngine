// ============================================================
// 第一章 - 小镇探索（入口 + 酒馆 + 广场）
// ============================================================

// == 小镇入口 ================================================
scene "town_entrance"
  set "story.progress" 2
  bg_switch "Images/door_zoom.jpg" transition=fade duration=0.8
  text "第一章 · 小镇入口" x=50% y=10% size=32 color="#FFD700" halign=center font="Microsoft YaHei"
  text "{player.name} · HP: {player.hp}/{player.maxHp} · 金币: {player.gold}" x=5% y=88% size=14 color="#666666" font="Consolas"
  say "迷雾中隐约可见两座建筑——一座酒馆，一座广场。" speaker="旁白"
  button "进入酒馆" x=25% y=50% width=200 height=44 color="#88CCFF" nav="tavern" halign=center
  button "前往广场" x=50% y=50% width=200 height=44 color="#88FF88" nav="plaza" halign=center
  button "进入森林" x=75% y=50% width=200 height=44 color="#FFAA88" nav="forest_entry" halign=center
  button "返回标题" x=50% y=75% width=160 height=40 color="#FF8888" nav="title_main" halign=center

// == 酒馆场景 ================================================
scene "tavern"
  define "story.tavern_visited" true once
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.3
  text "老张的酒馆" x=50% y=10% size=30 color="#FFD700" halign=center font="Microsoft YaHei"
  text "老板 {npc.innkeeper.name} 正在擦拭酒杯。" x=5% y=20% size=16 color="#CCCCCC" font="Microsoft YaHei"
  text "好感度: {npc.innkeeper.trust}" x=5% y=88% size=12 color="#888888" font="Consolas"
  button "与老板聊天" x=25% y=45% width=180 height=42 color="#88CCFF" nav="tavern_chat" halign=center
  button "买杯酒 (10金币)" x=50% y=45% width=180 height=42 color="#FFCC88" nav="tavern_drink" halign=center
  button "打听传闻" x=75% y=45% width=180 height=42 color="#AA88FF" nav="tavern_rumors" halign=center
  button "离开酒馆" x=50% y=75% width=160 height=40 color="#FF8888" nav="town_entrance" halign=center

// == 与老板聊天（好感度分支）=================================
label tavern_chat:
  set "story.met_innkeeper" true
  if {npc.innkeeper.trust < 3}
    say "哦？新来的旅人？坐吧，别客气。" speaker="{npc.innkeeper.name}"
    say "这镇子不太平，你小心点。" speaker="{npc.innkeeper.name}"
    set "npc.innkeeper.trust" {npc.innkeeper.trust + 1}
  else if {npc.innkeeper.trust < 6}
    say "又是你啊，最近怎么样？" speaker="{npc.innkeeper.name}"
    say "森林深处那座洞穴...我劝你别去。" speaker="{npc.innkeeper.name}"
    say "不过你若是执意要去，带上把好武器。" speaker="{npc.innkeeper.name}"
    set "npc.innkeeper.trust" {npc.innkeeper.trust + 1}
    set "story.has_clue" true
  else
    say "老朋友！来，这杯算我请的。" speaker="{npc.innkeeper.name}"
    say "那个洞穴里据说封印着某种力量。" speaker="{npc.innkeeper.name}"
    say "有人说那是星辰坠落留下的碎片。" speaker="{npc.innkeeper.name}"
    set "story.has_clue" true
  say "你回到了酒馆大厅。" speaker="旁白"
  navigate "tavern"

// == 买酒（条件判断 + HP 恢复）===============================
label tavern_drink:
  if {player.gold >= 10}
    set "player.gold" {player.gold - 10}
    set "player.hp" {player.hp + 15}
    if {player.hp > player.maxHp}
      set "player.hp" {player.maxHp}
    say "你喝了一杯蜜酒，温暖传遍全身。" speaker="旁白"
    say "金币 -10，HP 恢复至 {player.hp}" speaker="系统"
    set "npc.innkeeper.trust" {npc.innkeeper.trust + 1}
  else
    say "抱歉，你的金币不够。" speaker="{npc.innkeeper.name}"
    say "等有了钱再来吧。" speaker="{npc.innkeeper.name}"
  navigate "tavern"

// == 打听传闻 =================================================
label tavern_rumors:
  say "你想打听点什么？" speaker="{npc.innkeeper.name}"
  say "最近镇子周围的雾越来越浓了..." speaker="{npc.innkeeper.name}"
  if {story.has_clue}
    say "我不是告诉你了吗？森林深处的洞穴，别去。" speaker="{npc.innkeeper.name}"
  else
    say "有人看见森林深处有光，但没人敢进去看。" speaker="{npc.innkeeper.name}"
    say "你要是真想去，先确认自己够强。" speaker="{npc.innkeeper.name}"
  navigate "tavern"

// == 广场景色 =================================================
scene "plaza"
  define "story.plaza_visited" true once
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.3
  text "小镇广场" x=50% y=10% size=30 color="#FFD700" halign=center font="Microsoft YaHei"
  text "一位老人坐在喷泉旁，似乎在等什么人。" x=5% y=20% size=16 color="#CCCCCC" font="Microsoft YaHei"
  text "善行: {story.good_deeds} · 恶行: {story.bad_deeds}" x=5% y=88% size=12 color="#888888" font="Consolas"
  button "帮助老人" x=25% y=50% width=180 height=42 color="#88CCFF" nav="plaza_help" halign=center
  button "无视离开" x=50% y=50% width=180 height=42 color="#FF8888" nav="plaza_ignore" halign=center
  button "返回入口" x=75% y=50% width=180 height=42 color="#FFAA88" nav="town_entrance" halign=center

// 广场入口流程（条件对话）
label plaza:
  if {story.met_elder}
    say "老人看到你回来，微笑着点了点头。" speaker="旁白"
    say "快去森林吧，那个洞穴还在等着你。" speaker="老人"

// == 帮助老人（善行路线）=====================================
label plaza_help:
  if {story.met_elder}
    say "你已经帮过我了，快去森林吧。" speaker="老人"
    navigate "plaza"
  set "story.met_elder" true
  set "story.good_deeds" {story.good_deeds + 1}
  say "谢谢你，年轻人。这把旧骨头真是不中用了。" speaker="老人"
  say "你要去森林吗？那里有座洞穴，传说藏着星辰的碎片。" speaker="老人"
  say "但要小心，洞穴里的力量不是凡人能驾驭的。" speaker="老人"
  set "story.has_clue" true
  set "player.exp" {player.exp + 20}
  say "经验值 +20" speaker="系统"
  navigate "plaza"

// == 无视老人（恶行路线）=====================================
label plaza_ignore:
  set "story.met_elder" true
  set "story.bad_deeds" {story.bad_deeds + 1}
  say "你径直从老人身边走过，他叹了口气。" speaker="旁白"
  say "这年头，连停下来说句话的人都没有了..." speaker="老人"
  navigate "town_entrance"
