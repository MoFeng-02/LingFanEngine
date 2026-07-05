using LingFanEngine.Abstractions.Entities.Enums;

namespace LingFanEngine.Abstractions.Scripting;

/// <summary>
/// 场景脚本注册信息 — 由 CSharpScripts 注册，GameLoop 导航时调用
/// <para>场景名 + SceneType + Run() 委托，实现分层解耦</para>
/// </summary>
public class SceneScriptEntry
{
    /// <summary>场景标识名</summary>
    public required string SceneName { get; init; }

    /// <summary>场景类型（Game=存档入栈，Menu=不入栈）</summary>
    public SceneType SceneType { get; init; } = SceneType.Game;

    /// <summary>执行入口（Run() 中自上而下执行场景构建 + 对话流）</summary>
    public required Func<Task> Runner { get; init; }

    /// <summary>场景变量定义（进入场景时深合并，补缺+修类型）</summary>
    public Dictionary<string, object?>? Defines { get; init; }
}
