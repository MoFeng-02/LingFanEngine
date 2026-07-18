namespace LingFanEngine.Abstractions.Interfaces.Logging;

/// <summary>
/// 引擎日志级别（从低到高）
/// </summary>
public enum EngineLogLevel
{
    /// <summary>跟踪——最详细的诊断信息</summary>
    Trace = 0,

    /// <summary>调试——开发期诊断信息</summary>
    Debug = 1,

    /// <summary>信息——常规引擎运行信息</summary>
    Info = 2,

    /// <summary>警告——非预期但可恢复的情况</summary>
    Warning = 3,

    /// <summary>错误——运行时错误，通常需要处理</summary>
    Error = 4,

    /// <summary>严重——致命错误，引擎可能无法继续运行</summary>
    Critical = 5
}
