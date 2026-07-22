using System.Runtime.CompilerServices;

// 允许单元测试工程直测 internal 类型（如 Views/ControlFactory），无需渲染或 headless 宿主。
[assembly: InternalsVisibleTo("LingFanEngine.Tests")]
// 独立 headless 测试程序集：Avalonia HeadlessUnitTestSession 会改写全局 AvaloniaLocator/render loop，
// 必须运行在独立进程以避免污染主测试套件的无宿主 Avalonia 控件创建，故 headless 视图测试单拆一工程。
[assembly: InternalsVisibleTo("LingFanEngine.HeadlessTests")]
