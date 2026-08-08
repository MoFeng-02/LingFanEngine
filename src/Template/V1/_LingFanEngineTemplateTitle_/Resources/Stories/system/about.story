// ============================================================
// 关于页面
// ============================================================

scene "about" type=menu
  image "Images/lingfan.png" x=0 y=0 width=100% height=100% opacity=0.2

  text "关于" x=50% y=12% size=48 color="#FFD700" halign=center font="Microsoft YaHei"
  text "灵泛引擎模板项目 v1.0" x=50% y=22% size=20 color="#CCCCCC" halign=center font="Microsoft YaHei"
  text "基于 .NET 10 + C# + Avalonia 12" x=50% y=29% size=16 color="#888888" halign=center font="Microsoft YaHei"
  text "一切皆为 List 和 Dict" x=50% y=35% size=16 color="#888888" halign=center font="Microsoft YaHei"

  text "功能特性：" x=28% y=47% size=16 color="#AAAAAA" font="Microsoft YaHei" halign=left valign=top
  text "• DSL 双范式开发（.story + C# StoryScript）" x=30% y=53% size=14 color="#999999" font="Microsoft YaHei" halign=left valign=top
  text "• Ren'Py 风格回溯系统（滚轮上/下）+ NVL 模式" x=30% y="58%" size=14 color="#999999" font="Microsoft YaHei" halign=left valign=top
  text "• AOT 友好，跨平台（Desktop/Android/iOS/Browser）" x=30% y=63% size=14 color="#999999" font="Microsoft YaHei" halign=left valign=top
  text "• 四通道音频（BGM/SE/Ambient/Voice）+ 视频播放" x=30% y=68% size=14 color="#999999" font="Microsoft YaHei" halign=left valign=top
  text "• 存档/读档、CG 鉴赏、对话历史、设置面板" x=30% y=73% size=14 color="#999999" font="Microsoft YaHei" halign=left valign=top
  text "• 20 种过渡动画 · 对话框模板系统 · NVL auto" x=30% y=78% size=14 color="#999999" font="Microsoft YaHei" halign=left valign=top

  button "功能巡演 →" x=50% y=86% width=180 height=42 color="#FFAAFF" nav="showcase" halign=center valign=top
  button "返回标题" x=50% y=92% width=160 height=42 color="#FF8888" nav="title_main" halign=center valign=top
