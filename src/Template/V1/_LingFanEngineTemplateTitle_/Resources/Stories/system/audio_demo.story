// ============================================================
// 音频系统演示
// 展示 BGM / SE / Ambient / Voice 四通道 + 音量控制 + 停止
// 资源：Audio/chest_drawer_open.mp3 / crickets_night01.mp3
// ============================================================

scene "audio_demo" type=menu
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.25
  text "音频系统演示" x=50% y=10% size=36 color="#FFD700" halign=center font="Microsoft YaHei"
  text "四通道架构：BGM / SE / Ambient / Voice（miniaudio 后端）" x=50% y=17% size=14 color="#AAAAAA" halign=center font="Microsoft YaHei"
  button "BGM 播放" x=30% y=32% width=200 height=42 color="#88CCFF" nav="ad_bgm_play" halign=center
  button "SE 音效" x=70% y=32% width=200 height=42 color="#FFCC88" nav="ad_se_play" halign=center
  button "Ambient 环境音" x=30% y=40% width=200 height=42 color="#88FFAA" nav="ad_ambient" halign=center
  button "Voice 语音" x=70% y=40% width=200 height=42 color="#FFAAFF" nav="ad_voice" halign=center
  button "音量控制" x=30% y=48% width=200 height=42 color="#AAAAFF" nav="ad_volume" halign=center
  button "组合测试" x=70% y=48% width=200 height=42 color="#FF8888" nav="ad_combo" halign=center
  button "返回标题" x=50% y=80% width=160 height=42 color="#FF8888" nav="title_main" halign=center

// == BGM 播放 ==================================================
label ad_bgm_play:
  bgm "Audio/crickets_night01.mp3" volume=0.6
  say "♪ BGM 开始播放：夜晚虫鸣（crickets_night01.mp3）" speaker="系统"
  say "BGM 默认循环播放，不会自动停止。" speaker="系统"
  say "点击继续将停止 BGM 并返回菜单。" speaker="系统"
  bgm ""
  navigate "audio_demo"

// == SE 音效 ====================================================
label ad_se_play:
  say "准备播放 SE 音效——抽屉打开声..." speaker="系统"
  se "Audio/chest_drawer_open.mp3" volume=0.9
  say "✓ SE 播放完成（chest_drawer_open.mp3）" speaker="系统"
  say "SE 是一次性音效，播放完毕后自动释放通道。" speaker="系统"
  say "可以连续播放多个 SE，引擎会自动管理通道复用。" speaker="系统"
  se "Audio/chest_drawer_open.mp3" volume=0.5
  say "第二次以较低音量播放同一音效。" speaker="系统"
  navigate "audio_demo"

// == Ambient 环境音 ==============================================
label ad_ambient:
  ambient "Audio/crickets_night01.mp3" loop=true volume=0.4
  say "♨ Ambient 环境音开始：低音量循环虫鸣" speaker="系统"
  say "Ambient 与 BGM 的区别：Ambient 用于环境氛围层，可与 BGM 叠加。" speaker="系统"
  wait 2.0 skipable
  say "停止环境音..." speaker="系统"
  stop_ambient
  say "✓ Ambient 已停止。" speaker="系统"
  navigate "audio_demo"

// == Voice 语音 ================================================
label ad_voice:
  say "Voice 通道用于角色语音，支持 auto_stop（新语音自动打断旧语音）。" speaker="系统"
  voice "Audio/chest_drawer_open.mp3" volume=0.8
  say "「这是一段语音台词。」" speaker="勇者"
  say "Voice 播放期间可以继续对话——语音与文本独立。" speaker="系统"
  voice "Audio/chest_drawer_open.mp3" volume=0.6
  say "「第二段语音会自动打断第一段（auto_stop 默认 true）。」" speaker="吟游诗人"
  stop_voice
  say "✓ Voice 已手动停止。" speaker="系统"
  navigate "audio_demo"

// == 音量控制 ==================================================
label ad_volume:
  bgm "Audio/crickets_night01.mp3" volume=0.2
  say "BGM 以 20% 音量开始..." speaker="系统"
  say "逐步提升到 80%——注意音量变化。" speaker="系统"
  bgm "Audio/crickets_night01.mp3" volume=0.5
  say "现在 50%..." speaker="系统"
  bgm "Audio/crickets_night01.mp3" volume=0.8
  say "现在 80%..." speaker="系统"
  say "每条 bgm 命令都会平滑切换到新的音量和曲目。" speaker="系统"
  bgm ""
  say "✓ BGM 已停止。" speaker="系统"
  navigate "audio_demo"

// == 组合测试（多通道叠加）====================================
label ad_combo:
  bgm "Audio/crickets_night01.mp3" volume=0.35
  say "♪ BGM 层启动（低音量背景）" speaker="系统"
  ambient "Audio/crickets_night01.mp3" loop=true volume=0.25
  say "♨ Ambient 层叠加（更低的氛围层）" speaker="系统"
  say "现在同时播放 SE..." speaker="系统"
  se "Audio/chest_drawer_open.mp3" volume=0.8
  say "✓ SE 在 BGM+Ambient 之上独立播放！" speaker="系统"
  say "四通道互不干扰，各自独立控制音量和生命周期。" speaker="系统"
  wait 2.0 skipable
  say "全部停止——回到宁静。" speaker="系统"
  bgm ""
  stop_ambient
  say "✓ 所有音频通道已关闭。" speaker="系统"
  navigate "audio_demo"
