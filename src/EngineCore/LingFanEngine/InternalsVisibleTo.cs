using System.Runtime.CompilerServices;

// 允许单元测试工程直测 internal 类型（如 Views/ControlFactory），无需渲染或 headless 宿主。
[assembly: InternalsVisibleTo("LingFanEngine.Tests")]
