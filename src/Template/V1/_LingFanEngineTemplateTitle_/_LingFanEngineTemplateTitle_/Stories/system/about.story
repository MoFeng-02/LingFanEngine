// ============================================================
// 关于页面
// ============================================================

scene "about" type=menu
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.2

  text "关于" x=50% y=15% size=48 color="#FFD700" halign=center font="Microsoft YaHei"
  text "灵泛引擎模板项目 v1.0" x=50% y=25% size=20 color="#CCCCCC" halign=center font="Microsoft YaHei"
  text "基于 .NET 10 + C# + Avalonia 12" x=50% y=32% size=16 color="#888888" halign=center font="Microsoft YaHei"
  text "一切皆为 List 和 Dict" x=50% y=38% size=16 color="#888888" halign=center font="Microsoft YaHei"

  text "功能特性：" x=30% y=50% size=16 color="#AAAAAA" font="Microsoft YaHei" halign=left valign=top
  text "• DSL 双范式开发（.story + C# StoryScript）" x=32% y=56% size=14 color="#999999" font="Microsoft YaHei" halign=left valign=top
  text "• Ren'Py 风格回溯系统（滚轮上/下）" x=32% y=61% size=14 color="#999999" font="Microsoft YaHei" halign=left valign=top
  text "• AOT 友好，跨平台（Desktop/Android/iOS/Browser）" x=32% y=66% size=14 color="#999999" font="Microsoft YaHei" halign=left valign=top
  text "• 存档/读档、CG 鉴赏、对话历史、设置面板" x=32% y=71% size=14 color="#999999" font="Microsoft YaHei" halign=left valign=top
  text "• 音视频集成（LibVLC 音频 + GpuMediaPlayer 视频）" x=32% y=76% size=14 color="#999999" font="Microsoft YaHei" halign=left valign=top

  button "返回标题" x=50% y=88% width=200 height=44 color="#88CCFF" nav="title_main" halign=center valign=top
