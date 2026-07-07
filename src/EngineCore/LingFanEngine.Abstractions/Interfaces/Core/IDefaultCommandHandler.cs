namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 标记接口 — 实现此接口的命令处理器会被引擎自动注册为默认处理器。
/// <para>AOT 安全：不使用反射或程序集扫描，由 DI 容器手动注册实现此接口的类型。</para>
/// </summary>
public interface IDefaultCommandHandler { }
