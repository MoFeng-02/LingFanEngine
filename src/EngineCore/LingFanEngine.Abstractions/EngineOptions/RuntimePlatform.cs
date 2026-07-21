namespace LingFanEngine.Abstractions.EngineOptions;

/// <summary>
/// 运行时平台分类。用于 <see cref="LingFanEngineOptions.GetTargetFps"/> 等平台相关自适应逻辑。
/// <list type="bullet">
///   <item><description>Unknown：未指定，由引擎按 <see cref="System.OperatingSystem"/> 自动检测（默认，保持原有行为）。</description></item>
///   <item><description>Desktop：强制桌面端行为（如目标帧率取 DesktopTargetFps）。</description></item>
///   <item><description>Mobile：强制移动端行为（如目标帧率取 MobileTargetFps）。</description></item>
/// </list>
/// 宿主（如 SDK.Android / SDK.iOS）可在启动时显式设置，以覆盖自动检测结果（便于特殊环境或测试）。
/// </summary>
public enum RuntimePlatform
{
    Unknown = 0,
    Desktop,
    Mobile,
}
