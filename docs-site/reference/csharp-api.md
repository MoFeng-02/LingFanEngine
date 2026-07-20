# C# API 参考

灵泛引擎的 C# API 用于编写游戏扩展逻辑、自定义场景和 UI 面板。

## 命名空间速查

| 命名空间 | 说明 |
|:---|:---|
| `LingFanEngine.Abstractions.Interfaces.Core` | 核心接口 |
| `LingFanEngine.Abstractions.Interfaces.Scripting` | 脚本相关 |
| `LingFanEngine.Abstractions.Interfaces.Media` | 音频/视频 |
| `LingFanEngine.Abstractions.Interfaces.Saves` | 存档 |
| `LingFanEngine.Abstractions.Interfaces.Entry` | 入口层 |
| `LingFanEngine.Abstractions.Interfaces.Events` | 事件调度 |
| `LingFanEngine.Abstractions.EngineOptions` | 配置选项 |
| `LingFanEngine.Abstractions` | StateKeys、模型 |
| `LingFanEngine.Games` | StoryScript 基类 |
| `LingFanEngine.Views` | 视图层（SceneView、DialogBox） |

## IGameController

游戏控制器，C# 端的主 API。

### 对话

```csharp
Task SayAsync(string text, string? speaker = null, string? template = null);
Task ExtendDialogAsync(string text);
Task WaitForClickAsync();
```

### 过渡与等待

```csharp
Task TransitionAsync(string effect, double duration = 0.5);
Task SkipableWaitAsync(double seconds);
Task WaitForConditionAsync(Func<bool> condition);
```

### 菜单与输入

```csharp
Task<int> ShowMenuAsync(string prompt, params string[] options);
Task<string> InputAsync(string prompt);
Task<object?> CallScreenAsync(string screenName);
```

### 导航

```csharp
Task NavigateAsync(string target);
Task BackAsync();
Task ForwardAsync();
```

### NVL

```csharp
void EnterNvl();
Task EnterNvlAsync();
void ClearNvl();
Task ClearNvlAsync();
void ExitNvl();       // 退出并清空
Task ExitNvlAsync();
bool IsNvlActive { get; }
string GetNvlText();
```

### 视频

```csharp
Task<bool> PlayCutsceneAsync(string path, bool skipable = true, float volume = 1.0f, CancellationToken ct = default);
```

### 时间事件

```csharp
Task SetTimeEventAsync(
    string id,
    int hour,
    Func<Task>? callback = null,
    bool once = false,
    string[]? weekdays = null,
    int? minute = null,
    int? day = null);

void UnregisterEvent(string id);
```

### 存档

```csharp
void Save(string slot);
Task SaveAsync(string slot);
void Load(string slot);
Task LoadAsync(string slot);
void ClearStack();         // 清空场景堆栈
Task ClearStackAsync();
```

## IStateContainer

状态容器，所有运行时状态的存储。

```csharp
T? Get<T>(string key);
void Set<T>(string key, T value);
bool ContainsKey(string key);
bool Remove(string key);
IEnumerable<string> Keys { get; }
event Action<string>? ValueChanged;
```

### 点分路径

```csharp
State.Set("player.gold", 100);
State.Set("player.inventory.weapon", "剑");
var gold = State.Get<int>("player.gold");
```

## StoryScript

C# 场景基类。

```csharp
public abstract class StoryScript
{
    public abstract string SceneName { get; }
    public virtual string SceneType => "game";

    protected IStateContainer State { get; }
    protected ICommandPipeline Pipeline { get; }
    protected ISceneRegistry SceneRegistry { get; }

    public abstract Task RunAsync();

    // C# API 方法（通过 IGameController）
    protected Task SayAsync(string text, string? speaker = null, string? template = null);
    protected Task NavigateAsync(string target);
    // ... 其他 IGameController 方法

    protected void CreateSceneCheckpoint();  // 创建回溯检查点
}
```

### 示例

```csharp
public class MyScene : StoryScript
{
    public override string SceneName => "cs_my_scene";

    public override async Task RunAsync()
    {
        await SayAsync("欢迎！", speaker: "系统");
        var choice = await ShowMenuAsync("选择", "A", "B");
        if (choice == 0)
            State.Set("story.choice", "A");
        await NavigateAsync("next_scene");
    }
}
```

## ICommand / ICommandHandler

自定义命令系统。

### 定义命令

```csharp
public readonly record struct MyCommand(int Value) : ICommand;
```

### 处理器

```csharp
public class MyCommandHandler : ICommandHandler<MyCommand>, IDefaultCommandHandler
{
    public void Handle(MyCommand cmd, ICommandContext ctx)
    {
        // 处理逻辑
        ctx.State.Set("result", cmd.Value * 2);
    }
}
```

::: tip IDefaultCommandHandler
实现 `IDefaultCommandHandler` 标记接口的处理器会被引擎自动注册。如果不实现此接口，需手动注册到 `ICommandDispatcher`。
:::

### 注册

```csharp
services.AddSingleton<IDefaultCommandHandler, MyCommandHandler>();
```

## ICommandContext

命令处理上下文。

```csharp
public interface ICommandContext
{
    IStateContainer State { get; }
    ICommandPipeline Pipeline { get; }
    ISceneRegistry? SceneRegistry { get; }
    IGameTimeService? TimeService { get; }
    // ... 其他服务
}
```

## IDialogBox

自定义对话框模板接口。

```csharp
public interface IDialogBox
{
    bool IsComplete { get; }
    bool IsPausedByTag { get; }
    void SetText(string text, string? speaker = null);
    void Advance(double deltaSeconds);
    void SkipToEnd();
    void Hide();
    void ResetNvlState();
    Control AsControl();
}
```

## IDialogBoxFactory

```csharp
public interface IDialogBoxFactory
{
    IDialogBox Create(IStateContainer state);
}
```

## IDialogTemplateRegistry

```csharp
public interface IDialogTemplateRegistry
{
    void Register(string name, IDialogBoxFactory factory);
    void SetDefault(string name);
    IDialogBoxFactory? Resolve(string? name);
}
```

## LingFanEngineOptions

引擎配置。

```csharp
public class LingFanEngineOptions
{
    public string SaveDirectory { get; set; } = "Saves";
    public string MediaDirectory { get; set; } = "Media";
    public string StoriesDirectory { get; set; } = "Stories";
    public int DesktopTargetFps { get; set; } = 120;
    public int MobileTargetFps { get; set; } = 60;
    public int WindowWidth { get; set; } = 1920;
    public int WindowHeight { get; set; } = 1080;
    public int DesignWidth { get; set; } = 1920;
    public int DesignHeight { get; set; } = 1080;
    public bool EnableTimeSystem { get; set; } = false;
    public double DefaultTextSpeed { get; set; } = 30;
    public int MaxRollbackCheckpoints { get; set; } = 100;
    public string TitleSceneName { get; set; } = "title_main";
    public bool DefaultAutoStopBgm { get; set; } = true;
    public bool DefaultAutoStopVoice { get; set; } = true;
    public bool EnableHotReload { get; set; } = true;
    public bool ShowPerformanceHud { get; set; } = true;
    public string GameVersion { get; set; } = "1.0.0";
    // 时间系统
    public double SecondsPerGameMinute { get; set; } = 1.0;
    public int TimeStartDay { get; set; } = 1;
    public int TimeStartHour { get; set; } = 0;
    public int TimeStartMinute { get; set; } = 0;
}
```

## DI 注册

```csharp
public static IServiceCollection AddLingFanEngine(this IServiceCollection services)
{
    services.AddSingleton<IUIThreadDispatcher, AvaloniaUIThreadDispatcher>();
    services.AddSingleton<IAssetAccessor, AvaloniaAssetAccessor>();
    services.AddSingleton<IAsyncWaitService, AsyncWaitService>();
    services.AddSingleton<IEncryptionKeyProvider, NullEncryptionKeyProvider>();
    services.AddSingleton<IEncryptedFileReader, EncryptedFileReader>();
    // ...
    return services;
}
```

## StateKeys

常用状态键常量。

| 键 | 说明 |
|:---|:---|
| `Dialog.Text` | 当前对话框文本 |
| `Dialog.Speaker` | 当前说话者 |
| `Dialog.Clickable` | 对话框可点击 |
| `Dialog.Complete` | 对话完成标记 |
| `Dialog.Template` | 当前模板名 |
| `Nvl.Active` | NVL 模式激活 |
| `Scene.Name` | 当前场景名 |
| `Scene.Type` | 当前场景类型 |
| `Performance.Fps` | 当前 FPS |
| `Skip.Active` | Skip 模式 |
| `Auto.Active` | Auto 模式 |

## IEncryptionKeyProvider

```csharp
public interface IEncryptionKeyProvider
{
    byte[]? GetKey();  // null = 开发期无加密
}
```

## IEncryptedFileReader

```csharp
public interface IEncryptedFileReader
{
    Task<byte[]> ReadAllBytesAsync(string path);
    Task<string> ReadAllTextAsync(string path);
    Stream OpenRead(string path);
    bool IsEncrypted(string path);
    Task<bool> TryDecryptToFile(string path, string tempPath);
    void ReleaseTempFile(string tempPath);
}
```
