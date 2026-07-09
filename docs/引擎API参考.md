# 灵泛引擎 API 参考

> 更新日期：2026-07-09 | 含 Phase 12-27 全部 API（回溯系统/角色定义/Say 遮罩/场景元素揭示/存档完善/视频系统/C# 场景回溯修复/技术债务修复/日志系统/超时配置/虚拟分辨率缩放/通用交互系统）

---

## 目录

1. [架构总览](#一架构总览)
2. [DI 注册入口](#二di-注册入口)
3. [核心接口](#三核心接口)
4. [IGameController — 游戏控制器](#四igamecontroller--游戏控制器)
5. [命令系统](#五命令系统)
6. [StoryScript 基类](#六storyscript-基类)
7. [状态容器与 StateKeys](#七状态容器与-statekeys)
8. [存档系统](#八存档系统)
9. [音频系统](#九音频系统)
10. [视频系统](#十视频系统)
11. [DSL 执行器与故事加载](#十一dsl-执行器与故事加载)
12. [引擎配置选项](#十二引擎配置选项)
13. [日志系统](#十三日志系统)
14. [自定义对话框](#十四自定义对话框)
15. [辅助服务](#十五辅助服务)

---

## 一、架构总览

灵泛引擎采用 **接口驱动 + DI 注入** 架构，所有服务通过 `IServiceCollection` 注册，消费方通过接口解析依赖。

```
┌─────────────────────────────────────────────┐
│              Entry / Demo 层                 │
│  (MainWindow, OverlayManager, CSharpScripts) │
├─────────────────────────────────────────────┤
│         LingFanEngine.Abstractions           │
│  (接口定义、模型、StateKeys、Options)         │
├─────────────────────────────────────────────┤
│           LingFanEngine 实现层                │
│  (GameLoop, Handlers, DslExecutor, 等)       │
└─────────────────────────────────────────────┘
```

### 命名空间速查

| 命名空间 | 说明 |
|---------|------|
| `LingFanEngine.Abstractions.Interfaces.Core` | 核心接口（状态、命令、场景、循环、日志等） |
| `LingFanEngine.Abstractions.Interfaces.Scripting` | DSL/脚本相关接口 |
| `LingFanEngine.Abstractions.Interfaces.Media` | 音频/视频/Live2D 接口 |
| `LingFanEngine.Abstractions.Interfaces.Saves` | 存档/配置接口 |
| `LingFanEngine.Abstractions.Interfaces.Entry` | 入口层接口（命令服务、I18N、事件聚合） |
| `LingFanEngine.Abstractions.Interfaces.Events` | 事件调度接口 |
| `LingFanEngine.Abstractions.EngineOptions` | `LingFanEngineOptions`、`LayoutScaleMode` |
| `LingFanEngine.Abstractions` | `StateKeys`、模型实体 |
| `LingFanEngine.Abstractions.Serialization` | `ObjectDictionaryConverter`（AOT 安全） |

---

## 二、DI 注册入口

### AddLingFanEngine

```csharp
using LingFanEngine.Extensions;

// 基础注册
services.AddLingFanEngine();

// 带配置
services.AddLingFanEngine(opt =>
{
    opt.GameVersion = "2.0.0";
    opt.DesktopTargetFps = 120;
    opt.SaveDirectory = "Saves";
    opt.DefaultAutoStopBgm = true;
    opt.MaxImageCacheSize = 128;          // 图片缓存上限
    opt.BlockingTimeoutSeconds = 120;     // 阻塞 API 超时
    opt.InteractionTimeoutSeconds = 300;  // 交互 API 超时
    opt.DesignWidth = 1920;               // 虚拟分辨率
    opt.DesignHeight = 1080;
    opt.ScaleMode = LayoutScaleMode.Stretch;
});

// 注册默认命令服务（可选）
services.AddDefaultCommandService();
```

### 注册的服务一览

| 接口 | 实现类 | 生命周期 |
|------|--------|---------|
| `IStateContainer` | `StateContainer` | Singleton |
| `ICommandPipeline` | `CommandPipeline` | Singleton |
| `IGameLoop` | `GameLoop` | Singleton |
| `IGameController` | `GameController` | Singleton |
| `ICommandDispatcher` | `CommandDispatcher` | Singleton |
| `ISceneRegistry` | `SceneRegistry` | Singleton |
| `ISceneStack` | `SceneStack` | Singleton |
| `ITransitionEngine` | `TransitionEngine` | Singleton |
| `ITweenEngine` | `TweenEngine` | Singleton |
| `IDslExecutor` | `DslExecutor` | Singleton |
| `IStoryRegistry` | `StoryRegistry` | Singleton |
| `IStoryLoader` | `StoryLoader` | Singleton |
| `IScriptEngine` | `LingFanDslEngine` | Singleton |
| `IAudioManager` | `AudioManager` | Singleton |
| `IVideoManager` | `VideoManager` | Singleton |
| `ISaveService` | `BinarySaveService` | Singleton |
| `IConfigService` | `JsonConfigService` | Singleton |
| `IPreferencesService` | `PreferencesService` | Singleton |
| `II18nService` | `I18nService` | Singleton |
| `IEventAggregator` | `EventAggregator` | Singleton |
| `IEventScheduler` | `EventScheduler` | Singleton |
| `IGalleryService` | `GalleryService` | Singleton |
| `IDebugConsoleService` | `DebugConsoleService` | Singleton |
| `IJsonValueConverter` | `JsonValueConverter` | Singleton |
| `IEngineLogger` | `DebugEngineLogger` | Singleton |
| `IDialogBoxFactory` | `DefaultDialogBoxFactory` | Singleton |
| `IDefaultCommandHandler` | 所有内置 Handler | Singleton (多个) |

---

## 三、核心接口

### IStateContainer — 状态容器（SSOT）

所有运行时状态的唯一真相源。Key 为 `string`，支持点号路径访问嵌套字典。

```csharp
public interface IStateContainer
{
    void Set<T>(string key, T value);
    T? Get<T>(string key);
    bool TryGet<T>(string key, out T? value);
    bool ContainsKey(string key);
    bool Remove(string key);
    IEnumerable<string> Keys { get; }
    IReadOnlyDictionary<string, object?> GetSnapshot();
    void Clear();
}
```

**用法示例：**

```csharp
// 读取
var gold = state.Get<int>("player.gold");
var name = state.Get<string>("player.name") ?? "默认名";

// 写入
state.Set("player.gold", 100);
state.Set("player.hp", 50);

// 增量修改
state.Set("player.gold", state.Get<int>("player.gold") + 50);

// 点号路径 — 自动遍历嵌套字典
state.Set("npc.merchant.favorability", 10);
// 等价于 state["npc"]["merchant"]["favorability"] = 10

// 尝试获取
if (state.TryGet<int>("player.hp", out var hp))
    Console.WriteLine($"HP: {hp}");
```

> `Get<T>` 支持安全类型转换：如果存储值为 `long` 但请求 `int`，会自动 `Convert.ChangeType`。如果是 `JsonElement`（反序列化场景），会自动提取正确类型的值。

### ICommandPipeline — 命令管道

基于 `Channel` 的无锁异步队列，所有命令通过此管道投递到主循环。

```csharp
public interface ICommandPipeline
{
    ValueTask SendAsync(ICommand command, CancellationToken ct = default);
    IAsyncEnumerable<ICommand> ReceiveAllAsync(CancellationToken ct = default);
    bool TryRead(out ICommand command);
    int Count { get; }
    float TimeScale { get; set; }
    void Complete();
}
```

### ICommandContext — 命令处理上下文

由 `GameLoop` 构造并传入每个命令处理器，提供所有引擎依赖。

```csharp
public interface ICommandContext
{
    // 依赖
    IStateContainer State { get; }
    ICommandPipeline Pipeline { get; }
    ISceneRegistry? SceneRegistry { get; }
    ISceneStack? SceneStack { get; }
    IStoryRegistry? StoryRegistry { get; }
    IDslExecutor? DslExecutor { get; }
    ITransitionEngine? TransitionEngine { get; }
    IAudioManager? AudioManager { get; }
    IVideoManager? VideoManager { get; }          // Phase 22: 视频管理器
    ISaveService? SaveService { get; }
    LingFanEngineOptions Options { get; }
    Func<byte[]?>? CaptureThumbnail { get; }

    // 查询
    bool TryGetScriptEntry(string sceneName, out SceneScriptEntry? entry);

    // 共享操作
    void ResetInteractionState();
    void ClearLocalVariables();
    SaveData? BuildSaveData();
    void ApplySaveData(SaveData data);
    void ReportException(Exception ex, string source);
}
```

### ICommandDispatcher — 命令分发器

按命令类型路由到注册的处理器，AOT 兼容（无反射）。

> **AOT 安全性说明**：
> - `Register<TCommand>` 使用 `typeof(TCommand)` 作为 key，编译时已知类型
> - `Dispatch` 使用 `command.GetType()` 查找，运行时返回的 `Type` 对象与 `typeof(T)` 是同一引用
> - `ConcurrentDictionary` 默认使用 ReferenceEquals 比较 Type，不依赖反射
> - `RegisterDefaultHandlers` 使用显式 switch 类型匹配，完全 AOT 安全

```csharp
public interface ICommandDispatcher
{
    void Register<TCommand>(ICommandHandler<TCommand> handler) where TCommand : ICommand;
    void Register<TCommand>(Action<TCommand, ICommandContext> handler) where TCommand : ICommand;
    void Dispatch(ICommand command, ICommandContext ctx);
    bool IsRegistered<TCommand>() where TCommand : ICommand;
}
```

### ICommandHandler\<TCommand\> — 命令处理器

```csharp
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    void Handle(TCommand command, ICommandContext ctx);
}
```

### IDefaultCommandHandler — 标记接口

实现此接口的处理器会被 DI 容器自动收集并注册到 `GameLoop`。

```csharp
public interface IDefaultCommandHandler { }
```

### IGameLoop — 游戏主循环

```csharp
public interface IGameLoop : IDisposable    // Phase 27: 实现 IDisposable
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    bool IsRunning { get; }
    int TargetFps { get; set; }
    event Action<double>? OnFrame;       // 帧回调
    event Action<Exception, string>? OnException;  // 错误回调
}
```

> **Phase 27 IDisposable**：`Dispose()` 取消 `_stopCts` + 等待 `_loopTask`（2s 超时）+ 取消 `_workerCts` + 完成工作 Channel + Dispose CTS + Dispose 管道。

### ISceneRegistry — 场景注册表

```csharp
public interface ISceneRegistry
{
    void RegisterScene(string sceneName, SceneEntity scene);
    void Register(string sceneName, params UIElementEntity[] elements);
    SceneEntity? FindScene(string sceneName);
    IEnumerable<string> RegisteredScenes { get; }
    bool HasScene(string sceneName);
}
```

### ISceneStack — 场景堆栈

管理导航历史，支持前后跳转和完整状态回退。

```csharp
public interface ISceneStack
{
    int MaxDepth { get; set; }
    int Count { get; }
    IReadOnlyList<SceneSnapshot> Snapshot { get; }
    void Push(string sceneName);
    SceneSnapshot? Back();
    SceneSnapshot? Forward();
    SceneSnapshot? Peek();
    void Clear();
    void Restore(IReadOnlyList<SceneSnapshot> snapshot);
}
```

### ITransitionEngine — 过渡动画引擎

```csharp
public interface ITransitionEngine
{
    bool IsActive { get; }
    void StartTransition(TransitionEntity? transition);
    void Update(double deltaTime);
    void CompleteTransition();
}
```

### ITweenEngine — 补间引擎

```csharp
public interface ITweenEngine
{
    int ActiveCount { get; }
    void AddTween(Tween tween);
    void Update(double deltaTime, float timeScale);
    void Clear();
}
```

**Tween 类：**

```csharp
public class Tween
{
    public required string TargetKey { get; init; }   // 状态容器中的 Key
    public required double From { get; init; }
    public required double To { get; init; }
    public required double Duration { get; init; }
    public EasingType Easing { get; init; } = EasingType.Linear;
    public double Delay { get; init; }
    public string? TargetKeyY { get; init; }   // Y 坐标补间（可选）
    public double? FromY { get; init; }
    public double? ToY { get; init; }
}
```

---

## 四、IGameController — 游戏控制器

`IGameController` 是 C# 端的主命令 API，封装了所有常用游戏操作。每个方法有 **fire-and-forget**（void）和 **async**（Task）两个版本。

> **Phase 26 C# 场景回溯保护**：所有阻塞 API 检测到回放代次过期时抛出 `CSharpSceneReplayCancelledException`，异常沿 async 调用链传播到 `Runner()` 终止后续同步代码。开发者无需在 `Run()` 中手动处理。

> **Phase 27 超时可配置**：所有阻塞 API 的超时时间可通过 `LingFanEngineOptions` 配置（见第十二章）。

### 获取实例

```csharp
// 通过 DI 解析
var ctrl = services.GetRequiredService<IGameController>();

// 在 StoryScript 中，通过基类属性访问
protected IGameController Ctrl { get; }
```

### 导航

| 方法 | 说明 |
|------|------|
| `Navigate(sceneName)` / `NavigateAsync(sceneName)` | 导航到指定场景 |
| `Back()` / `BackAsync()` | 返回上一场景 |
| `Forward()` / `ForwardAsync()` | 前进到下一场景 |
| `ClearStack()` / `ClearStackAsync()` | 清空导航堆栈 |

### 对话

| 方法 | 说明 |
|------|------|
| `Say(text, speaker?, ...)` / `SayAsync(...)` | 显示对话，async 版等待用户点击 |
| `ExtendDialogAsync(append)` | 追加文本到当前对话（对标 Ren'Py extend） |
| `WaitForClickAsync()` | 等待用户点击后返回（对标 Ren'Py pause()） |
| `SkipableWaitAsync(seconds)` | 可跳过等待（对标 Ren'Py pause(N)） |
| `SignalComplete(key)` | 通知某个等待键已完成（UI 层点击时调用，零延迟唤醒） |
| `DefineCharacter(key, name?, color?, font?, textColor?, textFont?)` | 定义角色对话样式 |

`Say` / `SayAsync` 完整参数：

```csharp
ctrl.SayAsync(text, speaker?, speakerColor?, textColor?,
    typewriter = true,
    wPct?, hPct?,         // 对话栏宽高百分比
    marginL?, marginB?,   // 对话栏偏移
    clickable = false)    // Phase 17: true=禁用模态遮罩，允许场景按钮交互
```

`DefineCharacter` 用法：

```csharp
// 定义角色样式
ctrl.DefineCharacter("boss", name: "魔王", color: "#FF4444", font: "SimHei");
ctrl.DefineCharacter("hero", name: "勇者", color: "#44AAFF", textColor: "#FFFFFF");

// say 自动匹配 speaker → __char_{key} 字典的样式
await ctrl.SayAsync("你来了...", "boss");  // 自动使用红色名字
```

### 变量

| 方法 | 说明 |
|------|------|
| `Set(key, value)` / `SetAsync(key, value)` | 设置变量 |
| `Define(key, value)` / `DefineAsync(key, value)` | 定义变量（不存在时才写入） |
| `MergeDefSets(dict)` / `MergeDefSetsAsync(dict)` | 深合并变量定义（补缺+修类型） |

### 过渡

| 方法 | 说明 |
|------|------|
| `Transition(type, duration=0.5)` / `TransitionAsync(type, duration=0.5)` | 触发过渡动画 |

### 场景元素

| 方法 | 说明 |
|------|------|
| `Show(target, x=0, y=0)` / `ShowAsync(...)` | 显示立绘/图片 |
| `Hide(target)` / `HideAsync(target)` | 隐藏立绘 |
| `Background(path)` / `BackgroundAsync(path)` | 设置背景图 |

### 音频

| 方法 | 说明 |
|------|------|
| `PlayBgm(path, volume=0.8, fadeIn=0, autoStop?)` / `PlayBgmAsync(...)` | 播放 BGM |
| `StopBgm(fadeOut=0)` / `StopBgmAsync(...)` | 停止 BGM |
| `PlaySe(path, volume=0.6)` / `PlaySeAsync(...)` | 播放音效 |
| `StopSe()` / `StopSeAsync()` | 停止音效 |
| `PlayVoice(path, volume=1.0, autoStop?)` / `PlayVoiceAsync(...)` | 播放语音 |
| `StopVoice()` / `StopVoiceAsync()` | 停止语音 |

### 视频

| 方法 | 说明 |
|------|------|
| `PlayVideo(path, volume=1.0, loop=false, autoPlay=true)` / `PlayVideoAsync(...)` | 播放视频 |
| `StopVideo()` / `StopVideoAsync()` | 停止视频 |
| `PauseVideo()` / `PauseVideoAsync()` | 暂停视频 |
| `ResumeVideo()` / `ResumeVideoAsync()` | 恢复视频 |
| `SeekVideo(position)` / `SeekVideoAsync(position)` | 跳转视频 |
| `PlayCutsceneAsync(path, skipable=true, volume=1.0, ct)` | 播放全屏过场动画（阻塞） |

> `PlayCutsceneAsync` 返回 `Task<bool>`：`true`=用户跳过，`false`=自然结束。
> 超时由 `InteractionTimeoutSeconds` 控制（默认 300s）。

### 菜单与输入

| 方法 | 说明 |
|------|------|
| `ShowMenuAsync(prompt, options[])` | 显示菜单，返回选中索引 |
| `InputAsync(prompt, options?)` | 显示输入框，返回用户输入 |

### Call Screen（Phase 20-24）

| 方法 | 说明 |
|------|------|
| `CallScreenAsync(sceneName, ct, params (Key, Value)[] parameters)` | 调用 UI 场景并等待返回，支持传参 |
| `SetScreenResult(result)` | UI 场景内设置返回值 |
| `GetScreenParam<T>(key)` | 在 UI 场景中获取传入参数 |
| `HasScreen(sceneName)` | 检查场景是否正在显示 |
| `GetCurrentScreen()` | 获取当前显示的场景名 |

```csharp
// C# 中调用界面并传参
var result = await Ctrl.CallScreenAsync("item_select",
    CancellationToken.None,
    ("items", new[] { "药水", "武器", "防具" }),
    ("gold", 100));

// UI 场景中获取参数
var items = Ctrl.GetScreenParam<string[]>("items");
var gold = Ctrl.GetScreenParam<int>("gold");
```

### 回溯时间线（Phase 16/16.1 + 19 + 26）

| 方法 | 说明 |
|------|------|
| `Rollback()` / `RollbackAsync()` | 后退到上一个检查点 |
| `Rollforward()` / `RollforwardAsync()` | 前进到下一个检查点 |
| `RollbackTo(index)` / `RollbackToAsync(index)` | 回溯到指定检查点 |
| `BlockRollback()` | 阻止后续检查点创建（Phase 24） |
| `FixRollback()` | 恢复检查点创建（Phase 24） |

> 统一线性时间线：say/menu/input/wait/wait_skipable/pause/call_screen/scene_idle/navigate 自动创建检查点。
> 新交互截断未来检查点。`IsReplay` 控制回溯重展示不记录历史。
> C# 场景通过 `CreateSceneCheckpoint()` 创建场景级检查点（CommandIndex=-1），回溯时通过 `OnCSharpSceneReplay` 回调重新执行 `StoryScript.Run()`。

### 对话框窗口管理（Phase 24）

| 方法 | 说明 |
|------|------|
| `ShowWindow()` | 强制显示对话框（对标 Ren'Py window show） |
| `HideWindow()` | 强制隐藏对话框（对标 Ren'Py window hide） |
| `SetWindowAuto()` | 对话框自动模式（对标 Ren'Py window auto） |

### 播放控制

| 方法 | 说明 |
|------|------|
| `ToggleSkip()` / `ToggleSkipAsync()` | 切换跳过模式 |
| `ToggleAuto()` / `ToggleAutoAsync()` | 切换自动模式 |
| `SetAutoDelay(delay)` | 设置自动模式延迟（秒） |

### 效果

| 方法 | 说明 |
|------|------|
| `Shake(intensity=10, duration=0.5)` / `ShakeAsync(...)` | 屏幕震动 |

### 对话历史

| 方法 | 说明 |
|------|------|
| `ToggleHistory()` | 显示/隐藏历史面板 |
| `ClearHistory()` | 清空对话历史 |
| `GetHistory()` | 获取历史列表 |

### NVL 模式

| 方法 | 说明 |
|------|------|
| `EnterNvl()` / `EnterNvlAsync()` | 进入 NVL 模式 |
| `ClearNvl()` / `ClearNvlAsync()` | 清空 NVL 文本并退出 |
| `IsNvlActive` | NVL 是否激活 |
| `GetNvlText()` | 获取 NVL 累积文本 |
| `GetNvlSpeakers()` | 获取 NVL 累积说话者列表 |

### CG 鉴赏

| 方法 | 说明 |
|------|------|
| `UnlockGallery(id, imagePath, title?, sceneName?)` | 解锁 CG |
| `IsGalleryUnlocked(id)` | 检查 CG 是否已解锁 |
| `GetGalleryUnlocked()` | 获取已解锁列表 |
| `ToggleGallery()` | 显示/隐藏鉴赏面板 |

### 调试

| 方法 | 说明 |
|------|------|
| `DebugLog(message, level="Info")` / `DebugLogAsync(...)` | 记录调试日志 |
| `GetDebugLogs()` | 获取日志列表 |
| `ClearDebugLogs()` | 清空日志 |
| `SetDebugEnabled(enabled)` | 开关调试模式 |
| `ToggleDebugConsole()` | 显示/隐藏调试面板 |

### 偏好设置

| 方法 | 说明 |
|------|------|
| `SetVolume(channel, volume)` | 设置音量（master/bgm/se/voice） |
| `GetVolume(channel)` | 获取音量 |
| `SetMuted(muted)` | 设置静音 |
| `SetTextSpeed(charsPerSecond)` | 设置打字机速度 |

### 状态重置

| 方法 | 说明 |
|------|------|
| `ResetGameState()` / `ResetGameStateAsync()` | 重置全部游戏状态（返回主菜单时调用） |

> `ResetGameStateCommand` 投递到管道后：清除所有 `__` 系统变量 + 用户变量 + 检查点 + 场景堆栈 + 对话历史。

---

## 五、命令系统

### ICommand — 命令基接口

```csharp
public interface ICommand
{
    DateTimeOffset Timestamp { get; }
    CommandPriority Priority { get; }
}

public enum CommandPriority
{
    Background = 0,  // 背景逻辑
    Normal = 1,      // 普通逻辑
    High = 2,        // 高优先级
    Render = 3       // 渲染相关（最高）
}
```

### 内置命令清单

所有命令均为 `readonly record struct`，实现 `ICommand`。

#### 核心命令（Commands.cs 中定义）

| 命令 | 关键属性 | 说明 |
|------|---------|------|
| `SetVariableCommand` | `Key`, `Value`, `IsDefine` | 设置/定义变量 |
| `NavigateCommand` | `Path`, `SceneName?`, `EntryLabel?` | 导航到场景/label |
| `ShowDialogCommand` | `Text`, `Speaker?`, `SpeakerColor?`, `TextColor?`, `TypewriterEnabled`, `Clickable`, `SideImage?` | 显示对话（Phase 17: `Clickable`；Phase 24: `SideImage`） |
| `ExtendDialogCommand` | `Append` | 追加对话文本 |
| `PlayBgmCommand` | `Path`, `Volume`, `FadeIn`, `FadeOut`, `AutoStop?` | 播放 BGM |
| `StopBgmCommand` | — | 停止 BGM |
| `PlaySeCommand` | `Path`, `Volume` | 播放音效 |
| `PlayVoiceCommand` | `Path`, `Volume`, `AutoStop?` | 播放语音 |
| `BgmQueueCommand` | `Path`, `Volume`, `CrossFadeDuration` | BGM 交叉淡入 |
| `WaitCommand` | `Seconds`, `IsSkipable` | 等待（Phase 19: `IsSkipable`） |
| `HardPauseCommand` | — | 等待用户点击 |
| `ClearStackCommand` | — | 清空场景堆栈 |
| `MergeDefinesCommand` | `Defines` | 深合并变量定义 |
| `ResetGameStateCommand` | — | 重置全部游戏状态 |
| `PlayVideoCommand` | `Path`, `Volume`, `Loop`, `AutoPlay` | 播放视频（Phase 22） |
| `StopVideoCommand` | — | 停止视频（Phase 22） |
| `PauseVideoCommand` | — | 暂停视频（Phase 22） |
| `ResumeVideoCommand` | — | 恢复视频（Phase 22） |
| `SeekVideoCommand` | `Position` | 跳转视频（Phase 22） |
| `CutsceneCommand` | `Path`, `Skipable`, `Volume` | 过场动画（Phase 22） |

#### 脚本命令（ScriptingCommands.cs 中定义）

| 命令 | 关键属性 | 说明 |
|------|---------|------|
| `JumpCommand` | `TargetLabel`, `TargetIndex` | 标签跳转 |
| `BranchCommand` | `Condition?`, `SkipCount`, `HasMatched` | 条件分支 |
| `MenuCommand` | `Prompt?`, `Options` | 菜单选择 |
| `BuildSceneCommand` | `SceneName?`, `RawElements` | 场景构建 |
| `TransitionCommand` | `Type`, `Duration` | 过渡动画 |
| `ShowHideCommand` | `Target`, `X`, `Y`, `IsShow`, `IsBackground`, `Tag?`, `Transition?`, `TransitionDuration?` | 显示/隐藏元素（Phase 25: `Transition`/`TransitionDuration`） |
| `InputCommand` | `Prompt`, `StoreKey`, `Options?` | 用户输入 |
| `SaveLoadCommand` | `SlotId`, `IsSave` | 存档/读档 |
| `EvalCommand` | `Expression` | 表达式求值 |
| `SceneCommand` | `SceneName` | 清空堆栈并切换场景 |
| `BackCommand` | — | 返回上一场景 |
| `ForwardCommand` | — | 前进 |
| `RollbackToCommand` | `TargetCheckpointIndex` | 回溯到指定检查点 |
| `CallCommand` | `TargetLabel` | 调用子过程 |
| `ReturnCommand` | — | 从 call 返回 |
| `AnimateCommand` | `Target`, `Property`, `TargetValue`, `Duration`, `Easing`, `RepeatCount` | 控件动画 |
| `ShakeCommand` | `Intensity`, `Duration` | 屏幕震动 |
| `ToggleSkipCommand` | — | 切换跳过模式 |
| `ToggleAutoCommand` | — | 切换自动模式 |
| `UnlockGalleryCommand` | `Id`, `ImagePath`, `Title?`, `SceneName?` | 解锁 CG |
| `DebugLogCommand` | `Message`, `Level` | 调试日志 |
| `NvlCommand` | `IsClear` | NVL 模式 |
| `EndCommand` | — | 块结束哨兵 |
| `NavToLabelCommand` | `TargetLabel` | 按钮导航到 label |
| `ShowElementCommand` | `Element` | 场景元素按序揭示 |
| `RollbackCommand` | — | 回溯到上一个检查点 |
| `RollforwardCommand` | — | 前进到下一个检查点 |
| `CallScreenCommand` | `SceneName`, `StoreKey?`, `Params?` | 调用 UI 场景（Phase 24: `Params`） |

### 自定义命令处理器

```csharp
// 1. 定义命令
public readonly record struct MyCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Data { get; init; }
    public MyCommand() { }
}

// 2. 定义处理器（实现 IDefaultCommandHandler 可被 DI 自动注册）
public class MyHandler : ICommandHandler<MyCommand>, IDefaultCommandHandler
{
    public void Handle(MyCommand cmd, ICommandContext ctx)
    {
        ctx.State.Set("my.data", cmd.Data);
    }
}

// 3. 注册（在 ServiceCollectionExtensions 中）
services.AddSingleton<IDefaultCommandHandler, MyHandler>();

// 4. 使用
await pipeline.SendAsync(new MyCommand { Data = "hello" });
```

### 字符串命令（CommandService）

用于按钮 `cmd="do_xxx"` 场景：

```csharp
// 注册
cmdService.RegisterCommand("do_gold_add", async (value, ct) =>
{
    var gold = state.Get<int>("player.gold") + 50;
    state.Set("player.gold", gold);
    await Task.CompletedTask;
});

// 执行
await cmdService.ExecuteAsync("do_gold_add", null);
```

---

## 六、StoryScript 基类

`StoryScript` 是所有 C# 场景脚本的抽象基类。

```csharp
public abstract class StoryScript
{
    protected IGameController Ctrl { get; }
    protected IStateContainer? _state;
    protected ICommandPipeline? _pipeline;
    protected ISceneRegistry? _sceneRegistry;

    public abstract string SceneName { get; }
    public virtual SceneType SceneType => SceneType.Game;
    public abstract Task Run();
    public virtual Dictionary<string, object?> InDefines() => [];

    public void Initialize(IGameController ctrl, IStateContainer state,
        ICommandPipeline pipeline, ISceneRegistry sceneRegistry);
}
```

### 定义场景

```csharp
public class MyScene : StoryScript
{
    public override string SceneName => "my_scene";

    public override async Task Run()
    {
        SetScene("bg.jpg", "我的场景");
        await Ctrl.TransitionAsync("FadeIn", 0.5);
        await Ctrl.SayAsync("欢迎来到我的场景！", "向导");
        // ...
    }

    public override Dictionary<string, object?> InDefines() => new()
    {
        ["player.gold"] = 100,
        ["player.hp"] = 50
    };
}
```

### 基类辅助方法

| 方法 | 说明 |
|------|------|
| `SetScene(bgPath, title?, ...)` | 设置背景 + 标题 |
| `AddText(text, x, y, fontSize?, color?, halign?)` | 添加文本元素 |
| `AddButton(label, x, y, w, h, nav?, cmd?, ...)` | 添加按钮 |
| `AddImage(source, x, y, w?, h?, opacity?, ...)` | 添加图片 |
| `AddMenu(prompt, (label, target)[])` | 添加选择菜单 |
| `AddElement(UIElementEntity)` | 添加自定义元素 |

### 注册场景

```csharp
// 在 CSharpScripts.RegisterAll 中
var script = new MyScene();
script.Initialize(ctrl, state, pipeline, sceneRegistry);
gameLoop?.RegisterScriptEntry(new SceneScriptEntry
{
    SceneName = script.SceneName,
    SceneType = script.SceneType,
    Runner = () => script.Run(),
    Defines = script.InDefines()
});
```

> **Phase 26 C# 场景回溯保护**：Game 类型 C# 场景通过 `CreateSceneCheckpoint()` 纳入回溯时间线。当用户在 C# 场景中回溯/前进时，旧的 `Runner()` 通过 `CSharpSceneReplayCancelledException` 异常被彻底终止——所有阻塞 API 检测到回放代次过期时抛出异常，异常沿 async 调用链传播，跳过 `Run()` 中后续所有同步代码，防止 C# 场景控件泄漏。开发者无需在 `Run()` 中手动处理。

---

## 七、状态容器与 StateKeys

### 变量命名约定

| 规则 | 说明 |
|------|------|
| `__` 前缀 | 系统内部变量，不存档 |
| `_local_` 前缀 | 局部变量，场景切换时清除 |
| 无前缀 | 用户变量，自动存档 |

### StateKeys 分类

| 分类 | 前缀/键 | 说明 |
|------|---------|------|
| `StateKeys.Scene` | `__scene_*` / `__current_scene_*` | 场景状态 |
| `StateKeys.Dialog` | `__current_dialog_*` / `__dialog_*` / `__dialog_clickable` | 对话状态（Phase 17: `Clickable`） |
| `StateKeys.Characters` | `__char_{key}` | 角色定义（含 `side` 侧脸图，Phase 24） |
| `StateKeys.Transition` | `__transition_*` | 过渡动画 |
| `StateKeys.Dsl` | `__dsl_*` / `__dsl_csharp_replay_gen` | DSL 执行器 + 回放代次（Phase 26） |
| `StateKeys.Menu` | `__menu_*` | 菜单 |
| `StateKeys.Input` | `__input_*` | 用户输入 |
| `StateKeys.Audio` | `__current_bgm_*` / `__bgm_*` | 音频 |
| `StateKeys.Video` | `__video_*` | 视频状态（Phase 22） |
| `StateKeys.GameTime` | `__game_time_*` | 游戏时间 |
| `StateKeys.Playback` | `__skip_*` / `__auto_*` / `__seen_say_indices` | 跳过/自动模式 |
| `StateKeys.Preferences` | `__pref_*` | 偏好设置 |
| `StateKeys.Rollback` | `__rollback_*` / `__rollback_blocked_until` | 回溯检查点 + 阻止标记（Phase 16/24） |
| `StateKeys.History` | `__dialog_history*` | 对话历史 |
| `StateKeys.Gallery` | `__gallery_*` | CG 鉴赏 |
| `StateKeys.Debug` | `__debug_*` | 调试控制台 |
| `StateKeys.Nvl` | `__nvl_*` | NVL 模式 |
| `StateKeys.Shake` | `__shake_*` | 屏幕震动 |
| `StateKeys.Animation` | `__anim_*` | 控件级动画 |
| `StateKeys.CallStack` | `__call_stack` | call/return 调用栈 |
| `StateKeys.Story` | `__story_*` | 故事加载 |
| `StateKeys.Styles` | `__style_{name}` | 样式表（Phase 20） |
| `StateKeys.Screen` | `__screen_*` / `__screen_params` | call_screen 状态 + 传参（Phase 20-24） |
| `StateKeys.Performance` | `__perf_*` | 性能监控（Phase 20） |
| `StateKeys.UiTags` | `__tag` / `__runtime` / `__notify` | UI 层控件 Tag |

### 用户变量命名空间建议

| 命名空间 | 示例 | 说明 |
|---------|------|------|
| `player.*` | `player.gold`, `player.hp` | 玩家属性 |
| `npc.*` | `npc.merchant.name` | NPC 属性 |
| `story.*` | `story.progress` | 剧情状态 |
| `chapter*.*` | `chapter1.flag_door` | 章节状态 |
| `sandbox.*` | `sandbox.battle_count` | 沙盒/测试 |

### 深合并规则（MergeIntoState）

| 状态 | 行为 |
|------|------|
| 变量不存在 | 写入默认值 |
| 变量存在且类型匹配 | 跳过（保留游戏进度） |
| 变量存在但类型不匹配或为 null | 覆盖为默认值（自愈） |
| 字典嵌套 | 递归合并到叶子节点 |

---

## 八、存档系统

### ISaveService

```csharp
public interface ISaveService
{
    Task<SaveData?> LoadAsync(string slotId);
    Task SaveAsync(string slotId, SaveData data);
    Task DeleteAsync(string slotId);
    Task<IEnumerable<SaveSlotInfo>> GetAllSaveSlotsAsync();
    Task<bool> ExistsAsync(string slotId);
}
```

### 存档数据结构

```csharp
public class SaveData
{
    public string SaveVersion { get; set; }
    public string GameVersion { get; set; }
    public DateTime SavedAt { get; set; }
    public string CurrentScene { get; set; }
    public string SaveName { get; set; }
    public byte[]? Thumbnail { get; set; }       // 场景缩略图
    public List<SceneSnapshot>? SceneStack { get; set; }
    public List<UIElementEntity>? SceneElements { get; set; }
    public List<UIElementEntity>? RuntimeElements { get; set; }
    public Dictionary<string, object?> State { get; set; }  // 用户变量
    public int DslCurrentIndex { get; set; }     // DSL 执行位置
    public string? DslCurrentFile { get; set; }
    public List<RollbackCheckpoint>? RollbackCheckpoints { get; set; }
}
```

### 存档范围

- **保存**：所有无 `__` 前缀的变量 + 场景名 + 场景堆栈 + 场景元素 + DSL 执行位置 + 回溯检查点
- **排除**：所有 `__` 前缀的系统变量
- **加密**：AES-256（可选，通过 `AddDefaultSaveService(dir, key, iv)` 启用）
- **文件位置**：`{SaveDirectory}/{slotId}.save`
- **AOT 安全**：使用 `LfJsonContext`（source generator）+ `ObjectDictionaryConverter`（AOT 安全回退）

### IConfigService — 系统配置

不随用户存档变动的配置（语言、音量、帧率等）：

```csharp
public interface IConfigService
{
    T? Get<T>(string key);
    void Set<T>(string key, T value);
    void Load();
    void Save();
}
```

---

## 九、音频系统

### IAudioManager

三层音频架构：Master → BGM / SE / Voice

```csharp
public interface IAudioManager : IDisposable
{
    float MasterVolume { get; set; }      // 0~1
    bool MasterMuted { get; set; }
    float BgmVolume { get; set; }
    float SeVolume { get; set; }
    float VoiceVolume { get; set; }

    void PlayBgm(string filePath, float volume = 0.8f, bool loop = true);
    Task PlayBgmAsync(string filePath, float volume = 0.8f, bool loop = true);
    Task QueueBgmAsync(string path, float volume, double crossFadeDuration);
    Task StopBgmAsync();
    Task StopSeAsync();
    void PlaySe(string filePath, float volume = 1.0f);
    void PlayVoice(string filePath, float volume = 1.0f);
    void StopVoice();
    Task PauseAllAsync();
    Task ResumeAllAsync();
    Task StopAllAsync();
}
```

有效音量 = `MasterMuted ? 0 : groupVol × MasterVolume`

场景切换时按 `autoStop` 标记（`null` = 跟随 `LingFanEngineOptions.DefaultAutoStopBgm`）决定是否停 BGM/Voice，SE 允许跨场景。

音频后端使用 LibVLCSharp（跨平台），Browser/WASM 自动降级为 NullAsyncAudioPlayer。

---

## 十、视频系统

### IVideoManager

通过 StateContainer 状态键驱动 SceneView 中的 GpuMediaPlayer 控件。

```csharp
public interface IVideoManager
{
    float Volume { get; set; }           // 音量 (0~1)
    bool IsFinished { get; }             // 是否已播放结束（只读）
    event Action? OnFinished;            // 播放结束事件

    void Play(string path, float volume = 1.0f, bool loop = false, bool autoPlay = true);
    void Stop();
    void Pause();
    void Resume();
    void Seek(TimeSpan position);
    void PlayCutscene(string path, bool skipable = true, float volume = 1.0f);
}
```

### 视频状态键（StateKeys.Video）

| 状态键 | 类型 | 说明 |
|--------|------|------|
| `CurrentPath` | string | 当前视频路径 |
| `IsPlaying` | bool | 是否正在播放 |
| `IsPaused` | bool | 是否暂停 |
| `Volume` | float | 音量 (0~1) |
| `Loop` | bool | 是否循环 |
| `AutoPlay` | bool | 是否自动播放 |
| `SeekPosition` | double? | 跳转位置 |
| `Duration` | double | 总时长 |
| `Position` | double | 当前位置 |
| `IsFinished` | bool | 是否播放结束 |
| `CutsceneActive` | bool | 过场模式激活 |
| `CutsceneSkipped` | bool | 用户跳过 |
| `CutsceneSkipable` | bool | 是否可跳过 |

### 音视频分离架构

GpuMediaPlayer 永久静音（`Volume=0`），视频纯视觉，音频走 AudioManager。播放结束检测由 SceneView 回写 `IsFinished=true`，VideoManager 每帧 `PollFinished()` 检查并触发 `OnFinished` 事件。

---

## 十一、DSL 执行器与故事加载

### IDslExecutor

```csharp
public interface IDslExecutor
{
    void SetStoryRegistry(IStoryRegistry registry);
    void LoadCommands(IReadOnlyList<ICommand> commands, IReadOnlyDictionary<string, int>? labels = null, bool preserveCheckpoints = false);
    void Start();
    void StartFromLabel(string label);
    void Stop();
    bool IsRunning { get; }

    // 统一线性回溯时间线
    bool CanRollback();
    bool CanRollforward();
    bool RollbackTo(int targetPos);
    bool Rollback();
    bool Rollforward();
    void ClearCheckpoints();
}
```

> `LoadCommands` 的 `preserveCheckpoints` 参数：true=保留现有检查点（跨场景导航时用），false=清除（新故事/读档）。
> 回溯检查点在 say/menu/input/wait/wait_skipable/pause/call_screen/scene_idle/navigate 时自动创建。

### IStoryRegistry

```csharp
public interface IStoryRegistry
{
    int RegisteredCount { get; }
    int LoadedCount { get; }
    void Scan();                         // 扫描 Stories 目录
    bool LoadScene(string sceneName);     // 按需懒加载
    (IReadOnlyList<ICommand>? Commands, IReadOnlyDictionary<string, int>? Labels)
        GetCompiledResult(string sceneName);
    bool LoadSceneFromFile(string filePath);
    bool ReloadFile(string filePath);     // 热重载单个文件
    bool ReloadAll();                     // 热重载全部
    string? FindFileByLabel(string label);
    bool EnsureLabelLoaded(string label);
    void RegisterAllDefines();
    bool CanLoad(string sceneName);
}
```

### IScriptEngine

```csharp
public interface IScriptEngine
{
    string Name { get; }
    ScriptResult Compile(string script);
    ValueTask<ScriptResult> CompileAsync(string script, CancellationToken ct = default);
}
```

> **Phase 27 编译错误增强**：
> - 解析阶段：`DSL 解析错误（第 N 行）: {消息}\n  → {原始行内容}`
> - 编译阶段：`DSL 编译错误: {ExceptionType}: {消息}\n  堆栈: {200字摘要}`

### IStoryLoader

```csharp
public interface IStoryLoader
{
    void RegisterDefinesFromJson(StoryFile storyFile, string rawContent);
    (List<(string SceneName, List<UIElementEntity> Elements, string EntryScript)> Scenes, string FlowScript)
        ExtractSceneBlocks(string content);
}
```

---

## 十二、引擎配置选项

```csharp
public class LingFanEngineOptions
{
    // 路径
    public string SaveDirectory { get; set; } = "Saves";
    public string MediaDirectory { get; set; } = "Media";
    public string Live2DDirectory { get; set; } = "Live2D";
    public string ModsDirectory { get; set; } = "Mods";
    public string StoriesDirectory { get; set; } = "Stories";

    // 性能
    public int DesktopTargetFps { get; set; } = 120;
    public int MobileTargetFps { get; set; } = 60;
    public double RenderScale { get; set; } = 1.0;

    // 平台 / 虚拟分辨率（Phase 27）
    public int WindowWidth { get; set; } = 1920;
    public int WindowHeight { get; set; } = 1080;
    public int DesignWidth { get; set; } = 1920;       // 虚拟分辨率宽
    public int DesignHeight { get; set; } = 1080;      // 虚拟分辨率高
    public LayoutScaleMode ScaleMode { get; set; } = LayoutScaleMode.Stretch;
    public bool FullScreen { get; set; }
    public double SafeAreaLeft/Top/Right/Bottom { get; set; }

    // 调试
    public bool ShowPerformanceHud { get; set; } = true;
    public bool EnableHotReload { get; set; } = true;

    // 存档
    public string GameVersion { get; set; } = "1.0.0";
    public Func<string, string>? SaveNameFormatter { get; set; };
    public bool EnableTimeSystem { get; set; };
    public double DefaultTextSpeed { get; set; } = 30;
    public bool AutoClearStackOnMenu { get; set; }

    // 回溯
    public int MaxRollbackCheckpoints { get; set; } = 100;
    public int MaxStepBudget { get; set; } = 200;

    // 场景
    public string TitleSceneName { get; set; } = "title_main";
    public string BackTitleAlias { get; set; } = "back_title";

    // 音频生命周期
    public bool DefaultAutoStopBgm { get; set; } = true;
    public bool DefaultAutoStopVoice { get; set; } = true;

    // 图片缓存（Phase 27）
    public int MaxImageCacheSize { get; set; } = 128;

    // 超时配置（Phase 27）
    public int BlockingTimeoutSeconds { get; set; } = 120;
    public int InteractionTimeoutSeconds { get; set; } = 300;
    public int CutsceneActivationTimeoutSeconds { get; set; } = 5;

    public int GetTargetFps();
    public void WriteSafeAreaToState(IStateContainer state);
}
```

### LayoutScaleMode 枚举（Phase 27）

| 模式 | 说明 |
|------|------|
| `Contain` | 等比缩放，内容完全可见，不足部分留黑边 |
| `Cover` | 等比缩放，填满窗口，超出的边缘被裁切 |
| `Stretch` | 独立 X/Y 缩放填满窗口（默认，VN 游戏推荐） |

---

## 十三、日志系统（Phase 27）

### IEngineLogger

```csharp
public interface IEngineLogger
{
    void Log(EngineLogLevel level, string message, Exception? exception = null);
}

public enum EngineLogLevel { Debug, Info, Warning, Error }
```

扩展方法：

```csharp
logger.LogDebug("消息");
logger.LogInfo("消息");
logger.LogWarning("消息");
logger.LogError("消息", exception);
```

### 默认实现

`DebugEngineLogger`：Debug 模式输出到 `Debug.WriteLine`，Release 模式静默。

### DI 注册

```csharp
// 默认注册（引擎内部已注册）
services.TryAddSingleton<IEngineLogger, DebugEngineLogger>();

// 替换为自定义实现
services.AddSingleton<IEngineLogger, MyFileLogger>();
```

---

## 十四、自定义对话框

### IDialogBox 接口

```csharp
public interface IDialogBox
{
    bool IsComplete { get; }           // 打字机是否完成
    bool IsPausedByTag { get; }        // 是否被 {w}/{p} 标签暂停
    void SetText(string text, string? speaker = null);
    void Advance(double deltaSeconds); // 每帧推进打字机
    void SkipToEnd();                  // 跳到末尾
    void Hide();
    Control AsControl();               // 获取 Avalonia 控件
}
```

### IDialogBoxFactory

```csharp
public interface IDialogBoxFactory
{
    IDialogBox Create(IStateContainer state);
}
```

### 注册自定义对话框

```csharp
// 实现 IDialogBox + IDialogBoxFactory
public class MyDialogBox : IDialogBox { /* ... */ }
public class MyDialogBoxFactory : IDialogBoxFactory
{
    public IDialogBox Create(IStateContainer state) => new MyDialogBox(state);
}

// DI 注册替换
services.AddSingleton<IDialogBoxFactory, MyDialogBoxFactory>();
```

---

## 十五、辅助服务

### IPreferencesService

```csharp
public interface IPreferencesService
{
    float MasterVolume { get; set; }       // 0~1
    float BgmVolume { get; set; }
    float SeVolume { get; set; }
    float VoiceVolume { get; set; }
    bool MasterMuted { get; set; }
    double TextSpeed { get; set; }         // 字符/秒
    double AutoForwardDelay { get; set; }  // 秒
    bool SkipUnseen { get; set; }
    bool Fullscreen { get; set; }
}
```

### II18nService

```csharp
public interface II18nService
{
    void SwitchLanguage(string lang);
    string Translate(string original);
}
```

### IEventAggregator

```csharp
public interface IEventAggregator
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
    void Publish<TEvent>(TEvent evt) where TEvent : class;
    Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class;
}
```

### IEventScheduler

```csharp
public interface IEventScheduler : IDisposable
{
    void RegisterEvent(TimeEventEntity evt);
    void RegisterEvents(IEnumerable<TimeEventEntity> events);
    bool RemoveEvent(TimeEventEntity evt);
    void ClearEvents();
    int EventCount { get; }
}
```

### ICommandService

```csharp
public interface ICommandService
{
    ValueTask SendCommandAsync(ICommand command, CancellationToken ct = default);
    void RegisterCommand(string commandName, Func<object?, CancellationToken, Task> handler);
    Task ExecuteAsync(string commandName, object? commandValue, CancellationToken ct = default);
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
    void Publish<TEvent>(TEvent evt) where TEvent : class;
    Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class;
}
```

### IJsonValueConverter

```csharp
public interface IJsonValueConverter
{
    T? Convert<T>(object? value);
    object? Convert(object? value, Type targetType);
}
```

用于 `StateContainer` 的 `Get<T>` 安全类型转换：处理 `JsonElement` → 目标类型、数值类型安全扩展转换（如 `long` → `int`）。
