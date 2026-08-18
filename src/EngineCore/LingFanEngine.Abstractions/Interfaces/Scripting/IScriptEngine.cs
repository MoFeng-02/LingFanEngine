using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Abstractions.Interfaces.Scripting;

/// <summary>
/// 脚本执行结果
/// </summary>
public readonly record struct ScriptResult(
    bool Success,
    IReadOnlyList<ICommand> Commands,
    string? Error = null,
    IReadOnlyDictionary<string, int>? Labels = null
);

/// <summary>
/// 脚本引擎接口
/// <para>将 DSL/Lua 等脚本编译为 ICommand 列表，投入命令管道消费。</para>
/// <para>实现此接口可接入任意脚本语言（DSL、Lua、Python 等）。</para>
/// </summary>
public interface IScriptEngine
{
    /// <summary>
    /// 引擎名称：如 "LingFanDSL"、"Lua"
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 编译脚本文本为 ICommand 列表
    /// </summary>
    /// <param name="script">脚本源代码</param>
    /// <param name="filePath">源文件标识（可选）——用于局部变量（let/local）跨文件作用域隔离；为空表示无文件上下文</param>
    /// <returns>编译结果（命令列表或错误信息）</returns>
    ScriptResult Compile(string script, string? filePath = null);

    /// <summary>
    /// 异步编译（适用 Lua 等需要 IO 的场景）
    /// </summary>
    ValueTask<ScriptResult> CompileAsync(string script, CancellationToken ct = default);
}
