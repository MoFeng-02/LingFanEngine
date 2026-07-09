// ============================================================
// 视频播放演示
// ============================================================

// == 视频演示菜单 =============================================
scene "video_demo" type=menu
  image "Images/door_zoom.jpg" x=0 y=0 width=100% height=100% opacity=0.3
  text "视频播放演示" x=50% y=12% size=36 color="#FFD700" halign=center font="Microsoft YaHei"
  text "测试 MediaPlayer.Controls GpuMediaPlayer 集成" x=50% y=18% size=16 color="#AAAAAA" halign=center font="Microsoft YaHei"
  button "播放视频" x=50% y=30% width=220 height=42 color="#88CCFF" nav="vd_play" halign=center
  button "暂停/恢复" x=50% y=40% width=220 height=42 color="#88FF88" nav="vd_pause_resume" halign=center
  button "跳转测试" x=50% y=50% width=220 height=42 color="#FF88AA" nav="vd_seek" halign=center
  button "停止视频" x=50% y=60% width=220 height=42 color="#FF8888" nav="vd_stop" halign=center
  button "返回标题" x=50% y=80% width=160 height=42 color="#FF8888" nav="title_main" halign=center

// == 播放视频 =================================================
label vd_play:
  video "Video/sample.mp4" volume=0.8
  say "视频正在播放..." speaker="系统" clickable=true
  stop_video
  navigate "video_demo"

// == 暂停/恢复 ================================================
label vd_pause_resume:
  video "Video/sample.mp4" volume=0.8
  say "视频播放中，点击暂停" speaker="系统" clickable=true
  pause_video
  say "视频已暂停，点击恢复" speaker="系统" clickable=true
  resume_video
  say "视频已恢复，点击停止" speaker="系统" clickable=true
  stop_video
  navigate "video_demo"

// == 跳转测试 =================================================
label vd_seek:
  video "Video/sample.mp4" volume=0.8
  say "视频播放中，点击跳转到 10 秒处" speaker="系统" clickable=true
  seek_video 10
  say "已跳转到 10 秒处，点击结束" speaker="系统" clickable=true
  stop_video
  navigate "video_demo"

// == 停止视频 =================================================
label vd_stop:
  stop_video
  say "视频已停止" speaker="系统" clickable=true
  navigate "video_demo"
