// ============================================================
// 音视频播放演示
// 视频：Video/m1.mp4（GpuMediaPlayer 后端）
// 音频：Audio/crickets_night01.mp3 / Audio/chest_drawer_open.mp3（LibVLC 后端）
// ============================================================

// == 音视频演示菜单 =============================================
scene "video_demo" type=menu
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.3
  text "音视频演示" x=50% y=8% size=36 color="#FFD700" halign=center font="Microsoft YaHei"
  text "测试 GpuMediaPlayer + LibVLC 音频集成" x=50% y=14% size=16 color="#AAAAAA" halign=center font="Microsoft YaHei"
  // 视频测试
  text "—— 视频 ——" x=50% y=22% size=18 color="#88CCFF" halign=center font="Microsoft YaHei"
  button "播放视频" x=25% y=30% width=200 height=40 color="#88CCFF" nav="vd_play" halign=center
  button "暂停/恢复" x=50% y=30% width=200 height=40 color="#88FF88" nav="vd_pause_resume" halign=center
  button "循环播放" x=75% y=30% width=200 height=40 color="#FFAAFF" nav="vd_loop" halign=center
  button "跳转测试" x=25% y=38% width=200 height=40 color="#FF88AA" nav="vd_seek" halign=center
  button "过场动画" x=50% y=38% width=200 height=40 color="#FFCC88" nav="vd_cutscene" halign=center
  button "停止视频" x=75% y=38% width=200 height=40 color="#FF8888" nav="vd_stop" halign=center
  // 音频测试
  text "—— 音频 ——" x=50% y=48% size=18 color="#88FF88" halign=center font="Microsoft YaHei"
  button "播放 BGM" x=25% y=56% width=200 height=40 color="#88CCFF" nav="vd_bgm" halign=center
  button "停止 BGM" x=50% y=56% width=200 height=40 color="#FF8888" cmd="do_stop_bgm" halign=center
  button "播放 SE" x=75% y=56% width=200 height=40 color="#88FF88" nav="vd_se" halign=center
  button "环境音" x=25% y=64% width=200 height=40 color="#AA88FF" nav="vd_ambient" halign=center
  button "停止环境音" x=50% y=64% width=200 height=40 color="#FF8888" nav="vd_stop_ambient" halign=center
  button "语音" x=75% y=64% width=200 height=40 color="#FFAAFF" nav="vd_voice" halign=center
  // 返回
  button "返回标题" x=50% y=80% width=160 height=42 color="#FF8888" nav="title_main" halign=center

// == 视频播放 =================================================
label vd_play:
  video "Video/m1.mp4" volume=0.8
  say "视频正在播放..." speaker="系统" clickable=true
  stop_video
  navigate "video_demo"

// == 暂停/恢复 ================================================
label vd_pause_resume:
  video "Video/m1.mp4" volume=0.8
  say "视频播放中，点击暂停" speaker="系统" clickable=true
  pause_video
  say "视频已暂停，点击恢复" speaker="系统" clickable=true
  resume_video
  say "视频已恢复，点击停止" speaker="系统" clickable=true
  stop_video
  navigate "video_demo"

// == 循环播放 =================================================
label vd_loop:
  video "Video/m1.mp4" volume=0.6 loop=true
  say "视频循环播放中，点击停止" speaker="系统" clickable=true
  stop_video
  navigate "video_demo"

// == 跳转测试 =================================================
label vd_seek:
  video "Video/m1.mp4" volume=0.8
  say "视频播放中，点击跳转到 10 秒处" speaker="系统" clickable=true
  seek_video 10
  say "已跳转到 10 秒处，点击结束" speaker="系统" clickable=true
  stop_video
  navigate "video_demo"

// == 过场动画（全屏可跳过）====================================
label vd_cutscene:
  cutscene "Video/m1.mp4" skipable=true volume=0.9
  say "过场动画播放完毕。" speaker="系统" clickable=true
  navigate "video_demo"

// == 停止视频 =================================================
label vd_stop:
  stop_video
  say "视频已停止" speaker="系统" clickable=true
  navigate "video_demo"

// == BGM 播放 =================================================
label vd_bgm:
  bgm "Audio/crickets_night01.mp3" volume=0.5
  say "BGM 播放中（蟋蟀夜鸣）..." speaker="系统" clickable=true
  say "点击继续返回菜单，BGM 会继续播放。" speaker="系统" clickable=true
  navigate "video_demo"

// == SE 播放 ==================================================
label vd_se:
  se "Audio/chest_drawer_open.mp3" volume=0.8
  say "SE 已播放（抽屉开合声）。" speaker="系统" clickable=true
  navigate "video_demo"

// == 环境音 ===================================================
label vd_ambient:
  ambient "Audio/crickets_night01.mp3" loop=true volume=0.3
  say "环境音播放中（蟋蟀循环）..." speaker="系统" clickable=true
  say "点击继续返回菜单。" speaker="系统" clickable=true
  navigate "video_demo"

// == 停止环境音 ===============================================
label vd_stop_ambient:
  stop_ambient
  say "环境音已停止。" speaker="系统" clickable=true
  navigate "video_demo"

// == 语音 =====================================================
label vd_voice:
  voice "Audio/chest_drawer_open.mp3" volume=0.9
  say "语音已播放。" speaker="系统" clickable=true
  navigate "video_demo"
