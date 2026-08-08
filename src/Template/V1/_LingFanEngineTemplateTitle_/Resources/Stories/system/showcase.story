// ============================================================
// 灵泛引擎 · 功能总览巡演
// 一站式体验：对话/菜单/NVL/auto/过渡/动画/音频/视频/变量/流程控制
// 像游戏开场一样流畅串联，方便直接给人看
// ============================================================

scene "showcase"
  set "story.progress" 99
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.35
  text "灵泛引擎" x=50% y=8% size=48 color="#FFD700" halign=center font="Microsoft YaHei"
  text "LingFan Engine · 功能总览巡演" x=50% y=16% size=18 color="#CCCCCC" halign=center font="Microsoft YaHei"

  character "narrator" name="旁白" color="#AAAAAA" font="Microsoft YaHei"
  character "hero" name="旅人" color="#FFD700" font="Microsoft YaHei"
  character "poet" name="吟游诗人" color="#88FF88" font="Microsoft YaHei"

  // ── 开场：基础对话 ──
  say "欢迎来到灵泛引擎的功能演示。" speaker="narrator"
  say "接下来你将看到引擎的核心能力，逐一展现。" speaker="narrator"
  say "一切皆为 List 和 Dict——这是引擎的设计哲学。" speaker="poet"

  // ── 菜单分支 ──
  menu "想从哪里开始看？"
    "按顺序全部看完" -> sc_full_tour
    "只看音视频" -> sc_media_only
    "只看 NVL 与自动推进" -> sc_nvl_auto

// == 完整巡演 ==================================================
label sc_full_tour:
  // ── 1. 变量与表达式 ──
  set "_local_score" 0
  say "【1/9 变量系统】支持 define/let/set + 表达式插值。" speaker="narrator"
  set "_local_score" {_local_score + 10}
  say "当前积分：{_local_score}（通过 {表达式} 插值显示）。" speaker="narrator"

  // ── 2. 条件分支 ──
  if {_local_score >= 10}
    say "【2/9 条件分支】if/else 判断：积分已达标！" speaker="narrator"
  else
    say "积分不足。" speaker="narrator"
  set "_local_score" {_local_score + 10}

  // ── 3. 循环 ──
  say "【3/9 循环】while 循环演示：" speaker="narrator"
  set "_local_i" 0
  while {_local_i < 3}
    set "_local_i" {_local_i + 1}
    say "  循环第 {_local_i} 次" speaker="narrator"

  // ── 4. 过渡动画 ──
  transition "fade" duration=1.0
  say "【4/9 过渡动画】fade 淡入淡出（1.0s）。" speaker="narrator"
  shake intensity=12 duration=0.5
  say "   震动效果（shake intensity=12）。" speaker="narrator"

  // ── 5. 音频系统 ──
  bgm "Audio/crickets_night01.mp3" volume=0.4
  say "【5/9 音频】BGM 播放中（夜晚虫鸣，40% 音量）。" speaker="narrator"
  se "Audio/chest_drawer_open.mp3" volume=0.7
  say "   SE 音效叠加播放（抽屉打开声）。" speaker="narrator"
  wait 1.5 skipable

  // ── 6. 视频播放 ──
  say "【6/9 视频】接下来播放视频..." speaker="narrator"
  video "Video/m1.mp4" volume=0.7
  say "   视频播放中（点击可跳过等待）" speaker="系统" clickable=true
  stop_video
  say "   ✓ 视频已停止。" speaker="narrator"

  // ── 7. NVL 模式 + 自动推进 ──
  nvl auto
  say "【7/9 NVL 模式】进入全屏文本累积模式。" speaker="poet"
  say "每一句追加到同一个文本框，像看书一样。" speaker="poet"
  say "{b}{color=#FFD700}nvl auto{/color}{/b} 已开启——本段会自动翻页！" speaker="poet"
  say "适合长段叙事、独白、传说背景介绍。" speaker="poet"
  nvl clear
  say "nvl clear 清空累积文本，但仍在 NVL 模式。" speaker="narrator"
  say "新内容从这里开始重新累积。" speaker="narrator"
  nvl exit
  say "   nvl exit 退出 NVL，自动关闭 auto 模式，恢复 ADV。" speaker="narrator"

  // ── 8. 子过程调用 ──
  say "【8/9 子过程】call/return 演示：" speaker="narrator"
  call sc_subroutine
  say "   子过程已返回，继续主流程。" speaker="narrator"

  // ── 9. 存档/读档 ──
  save "showcase_slot"
  say "【9/9 存档】进度已保存到 showcase_slot。" speaker="narrator"
  bgm ""
  say "" speaker="narrator"
  transition "zoomin" duration=1.5
  say "{b}{color=#FFD700}巡演完成！{color}{/b}" speaker="poet"
  say "你已见证：对话·菜单·NVL·auto·过渡·震动·音频·视频·变量·条件·循环·子过程·存档" speaker="narrator"
  say "感谢体验灵泛引擎 ✨" speaker="poet"
  scene "title_main"

// == 子过程 ====================================================
label sc_subroutine:
  say "  → 这里是子过程内部（由 call 调用）。" speaker="narrator"
  return

// == 仅音视频 ==================================================
label sc_media_only:
  bgm "Audio/crickets_night01.mp3" volume=0.5
  say "♪ BGM：夜晚虫鸣" speaker="系统"
  se "Audio/chest_drawer_open.mp3"
  say "✓ SE：抽屉打开声" speaker="系统"
  video "Video/m1.mp4" volume=0.7
  say "▶ 视频播放中（点击跳过）" speaker="系统" clickable=true
  stop_video
  bgm ""
  say "✓ 全部停止。返回标题。" speaker="系统"
  scene "title_main"

// == 仅 NVL + auto ===============================================
label sc_nvl_auto:
  nvl auto
  say "NVL 自动推进模式已开启。" speaker="吟游诗人"
  say "每句话会根据 auto_speed 设置的时间间隔自动翻页。" speaker="吟游诗人"
  say "你不需要不断点击——{b}像看书一样{/b}享受故事。" speaker="吟游诗人"
  say "{color=#FFD700}这就是 NVL auto 的魅力所在。{/color}" speaker="吟游诗人"
  nvl exit
  say "✓ NVL 自动推进演示结束（auto 已随 nvl exit 自动关闭）。" speaker="系统"
  scene "title_main"
