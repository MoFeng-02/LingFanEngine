using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Entities.Scenes;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Abstractions.Interfaces.Dlc;

/// <summary>
/// 注册路由（场景）的委托——DLC 通过此函数向引擎注册自定义场景。
/// </summary>
/// <param name="path">路由路径（场景名）</param>
/// <param name="scene">场景实体</param>
public delegate void RegisterRouteDelegate(string path, SceneEntity scene);

/// <summary>
/// 注册时间事件的委托——DLC 通过此函数向引擎注册定时触发的事件。
/// </summary>
/// <param name="evt">时间事件实体</param>
public delegate void RegisterEventDelegate(TimeEventEntity evt);

/// <summary>
/// 获取只读游戏状态的委托——DLC 通过此函数查询引擎运行时状态。
/// </summary>
/// <returns>只读游戏状态快照</returns>
public delegate IReadOnlyGameState GetGameStateDelegate();

/// <summary>
/// 日志委托——DLC 通过此函数输出日志信息到引擎日志系统。
/// </summary>
/// <param name="message">日志消息</param>
public delegate void LogDelegate(string message);

/// <summary>
/// DLC 导出的入口函数签名
/// <para>DLC 原生库必须导出此函数，主程序调用时传入 API 函数表。</para>
/// </summary>
/// <param name="registerRoute">注册路由回调</param>
/// <param name="registerEvent">注册事件回调</param>
/// <param name="getGameState">获取游戏状态回调</param>
/// <param name="log">日志回调</param>
public delegate void InitializePluginDelegate(
    RegisterRouteDelegate registerRoute,
    RegisterEventDelegate registerEvent,
    GetGameStateDelegate getGameState,
    LogDelegate log);
