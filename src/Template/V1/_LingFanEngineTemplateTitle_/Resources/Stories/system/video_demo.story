// ============================================================
// 视频播放演示
// 展示 WebView 视频后端集成：播放/暂停恢复/跳转/停止
// 资源：Video/m1.mp4
// ============================================================

scene "video_demo" type=menu
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.25
  text "视频播放演示" x=50% y=10% size=36 color="#FFD700" halign=center font="Microsoft YaHei"
  text "WebView 单实例视频后端（Avalonia.Controls.WebView）· 多槽位承载" x=50% y=17% size=13 color="#AAAAAA" halign=center font="Microsoft YaHei"
  button "播放视频" x=50% y=30% width=220 height=42 color="#88CCFF" nav="vd_play" halign=center
  button "暂停/恢复" x=50% y=39% width=220 height=42 color="#88FF88" nav="vd_pause_resume" halign=center
  button "跳转测试" x=50% y=48% width=220 height=42 color="#FF88AA" nav="vd_seek" halign=center
  button "返回标题" x=50% y=75% width=160 height=42 color="#FF8888" nav="title_main" halign=center

// == 播放视频 =================================================
label vd_play:
  video "Video/m1.mp4" volume=0.8
  say "视频正在播放...（点击可跳过等待）" speaker="系统" clickable=true
  stop_video
  say "✓ 视频已停止。" speaker="系统"
  navigate "video_demo"

// == 暂停/恢复 ================================================
label vd_pause_resume:
  video "Video/m1.mp4" volume=0.8
  say "视频播放中——点击暂停" speaker="系统" clickable=true
  pause_video
  say "⏸ 视频已暂停——点击恢复" speaker="系统" clickable=true
  resume_video
  say "▶ 视频已恢复——点击停止" speaker="系统" clickable=true
  stop_video
  navigate "video_demo"

// == 跳转测试 =================================================
label vd_seek:
  video "Video/m1.mp4" volume=0.8
  say "视频播放中——点击跳转到 5 秒处" speaker="系统" clickable=true
  seek_video 5
  say "⏩ 已跳转到 5 秒处——点击结束" speaker="系统" clickable=true
  stop_video
  navigate "video_demo"
