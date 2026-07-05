# ICommandService 使用指南

## 概述

`ICommandService` 是引擎的通信核心，提供命令注册/执行、事件订阅/发布功能。

游戏逻辑层通过此接口与引擎通信：发送命令、订阅事件。

---

## 注入方式

```csharp
// Program.cs

// 1. 注册引擎核心（不包含命令服务）
services.AddLingFanEngine();

// 2. 注册默认命令服务（如需使用）
services.AddDefaultCommandService();

// 3. 如需自定义命令服务，开发者自行注册
services.AddSingleton<ICommandService, MyCustomCommandService>();

// 获取服务
var commandService = serviceProvider.GetRequiredService<ICommandService>();
```

---

## 注入方式详解

### 1. 注册引擎核心（不含命令）

```csharp
services.AddLingFanEngine();
services.AddLingFanEngine(opt => opt.SaveDirectory = "MySaves");
```

只注册 `LingFanEngineOptions`，不绑定命令实现。

### 2. 注册默认命令服务

```csharp
services.AddDefaultCommandService();
```

如需使用引擎默认的 `CommandService`，单独调用此方法。

### 3. 自定义命令服务

```csharp
// 实现 ICommandService 接口
public class MyCommandService : ICommandService { ... }

// 自行注册（覆盖默认）
services.AddSingleton<ICommandService, MyCommandService>();
```

开发者可完全自定义命令逻辑，引擎不绑定任何实现。

---

## 1. 泛型命令模式（强类型）

### 定义命令类

```csharp
public class ShowDialogCommand
{
    public required string Text { get; set; }
    public string? Speaker { get; set; }
}

public class PlayBgmCommand
{
    public required string Path { get; set; }
    public float Volume { get; set; } = 1.0f;
}
```

### 注册命令处理器

```csharp
// 同步命令
commandService.RegisterCommand<ShowDialogCommand>(cmd =>
{
    Console.WriteLine($"[{cmd.Speaker}] {cmd.Text}");
});

// 异步命令
commandService.RegisterCommandAsync<PlayBgmCommand>(async cmd =>
{
    await AudioPlayer.PlayAsync(cmd.Path, cmd.Volume);
});
```

### 执行命令

```csharp
commandService.Execute(new ShowDialogCommand { Text = "你好世界", Speaker = "旁白" });
await commandService.ExecuteAsync(new PlayBgmCommand { Path = "bgm/main.mp3" });
```

---

## 2. 字符串命令模式（对应 BaseEntity.Command）

引擎层通过 `BaseEntity.Command` 字符串触发命令，游戏逻辑层注册处理器。

### 注册命令处理器

```csharp
commandService.RegisterCommand("ShowDialog", param =>
{
    var cmd = (ShowDialogCommand)param;  // 强转为具体类型
    Console.WriteLine($"[{cmd.Speaker}] {cmd.Text}");
});

commandService.RegisterCommandAsync("PlayBgm", async (param, ct) =>
{
    var cmd = (PlayBgmCommand)param;
    await AudioPlayer.PlayAsync(cmd.Path, cmd.Volume, ct);
});
```

### 执行命令（引擎层）

```csharp
// entity.Command 是字符串，entity.CommandValue 是 object
if (!string.IsNullOrEmpty(entity.Command))
{
    commandService.Execute(entity.Command, entity.CommandValue);
}
```

### 流程

```
游戏逻辑层 RegisterCommand("ShowDialog", handler)
    ↓
ICommandService 注册
    ↓
引擎层遍历 Entity
    ↓
如果 entity.Command 有值 → Execute(entity.Command, entity.CommandValue)
```

---

## 3. 事件订阅与发布

### 定义事件类

```csharp
public class SceneChangedEvent
{
    public required string FromScene { get; set; }
    public required string ToScene { get; set; }
}

public class DialogEndedEvent
{
    public required string DialogId { get; set; }
}
```

### 订阅事件

```csharp
// 返回 IDisposable，可手动取消订阅
using var subscription = commandService.Subscribe<SceneChangedEvent>(evt =>
{
    Console.WriteLine($"场景切换: {evt.FromScene} → {evt.ToScene}");
});
```

### 发布事件

```csharp
// 同步发布
commandService.Publish(new SceneChangedEvent { FromScene = "title", ToScene = "chapter1" });

// 异步发布
await commandService.PublishAsync(new DialogEndedEvent { DialogId = "d001" });
```

---

## 4. 取消操作（CancellationToken）

```csharp
using var cts = new CancellationTokenSource();

// 异步命令支持取消
await commandService.ExecuteAsync(new PlayBgmCommand { Path = "bgm/battle.mp3" }, cts.Token);

// 事件发布也支持取消
await commandService.PublishAsync(new SceneChangedEvent { ... }, cts.Token);

// 取消订阅
subscription.Dispose();
```

---

## 模式对比

| 场景 | 推荐方式 |
|------|----------|
| 游戏逻辑层定义命令 | **泛型模式**（类型安全，编译器检查） |
| 引擎层执行 Entity 命令 | **字符串模式**（从 `Command` 字段读取） |
| 跨模块通信 | **事件模式**（发布/订阅解耦） |
| 需要取消操作 | 异步方法 + `CancellationToken` |

---

## 设计原则

1. **引擎层不管逻辑** — 只负责触发 `BaseEntity.Command`，具体处理由游戏逻辑层注入
2. **AOT 友好** — 无反射、无动态 IL 生成，全部编译时确定
3. **数据驱动** — 元数据定义结构，逻辑层定义行为
