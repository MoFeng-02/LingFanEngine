# ISaveService 使用指南

## 概述

`ISaveService` 是引擎的存档核心，提供存档的加载、保存、删除等操作。

引擎默认提供二进制加密存档（JSON + AES-256），也支持开发者自定义加密逻辑。

---

## 注入方式

```csharp
// Program.cs

// 1. 注册引擎核心服务（不包含存档服务）
services.AddLingFanEngine();

// 2. 注册默认存档服务（如需使用）
services.AddDefaultSaveService("Saves");

// 或使用指定密钥
services.AddDefaultSaveService("Saves", key, iv);

// 3. 如需自定义存档服务，开发者自行注册
services.AddSingleton<ISaveService, MyCustomSaveService>();

// 获取服务
var commandService = serviceProvider.GetRequiredService<ICommandService>();
var saveService = serviceProvider.GetRequiredService<ISaveService>();
```

---

## 注入方式详解

### 1. 注册引擎核心（不含存档）

```csharp
services.AddLingFanEngine();
services.AddLingFanEngine(opt => opt.SaveDirectory = "MySaves");
```

只注册 `ICommandService` 和 `LingFanEngineOptions`，不绑定存档实现。

### 2. 注册默认存档服务

```csharp
// 使用默认加密（基于机器信息生成密钥）
services.AddDefaultSaveService("Saves");

// 指定加密密钥
services.AddDefaultSaveService("Saves", key, iv);
```

如需使用引擎默认的 `BinarySaveService`，单独调用此方法。

### 3. 自定义存档服务

```csharp
// 实现 ISaveService 接口
public class MySaveService : ISaveService { ... }

// 自行注册（覆盖默认）
services.AddSingleton<ISaveService, MySaveService>();
```

开发者可完全自定义存档逻辑，引擎不绑定任何实现。

---

## 存档配置

```csharp
public class LingFanEngineOptions
{
    /// <summary>
    /// 存档存储目录（默认 "Saves"）
    /// </summary>
    public string SaveDirectory { get; set; } = "Saves";
}
```

---

## 核心接口

### ISaveService

| 方法 | 说明 |
|------|------|
| `Task<SaveData?> LoadAsync(string slotId)` | 加载存档 |
| `Task SaveAsync(string slotId, SaveData data)` | 保存存档 |
| `Task DeleteAsync(string slotId)` | 删除存档 |
| `Task<IEnumerable<SaveSlotInfo>> GetAllSaveSlotsAsync()` | 获取所有存档槽信息 |
| `Task<bool> ExistsAsync(string slotId)` | 检查存档是否存在 |

### IEncryption

| 方法 | 说明 |
|------|------|
| `byte[] Encrypt(byte[] data)` | 加密数据 |
| `byte[] Decrypt(byte[] data)` | 解密数据 |

---

## 存档数据模型

### SaveData

```csharp
public class SaveData
{
    public Guid Id { get; set; }                    // 存档唯一标识
    public required string GameVersion { get; set; } // 游戏版本号
    public string? Name { get; set; }                // 存档名称
    public DateTimeOffset CreateTime { get; set; }  // 创建时间
    public DateTimeOffset UpdateTime { get; set; }  // 修改时间
    public int MaxHistoryLength { get; set; }        // 最大回溯长度（默认20）
    public RouterState CurrentRouterState { get; set; }      // 当前路由状态
    public List<RouterState> HistoryRouterStates { get; set; } // 历史回溯列表
}
```

### RouterState

```csharp
public class RouterState
{
    public required string Path { get; set; }              // 路由路径
    public int CurrentSceneIndex { get; set; }             // 当前场景索引
    public List<SceneState> SceneStates { get; set; }      // 场景状态列表
}
```

### SceneState

```csharp
public class SceneState
{
    public required string SceneName { get; set; }         // 场景名称
    public Dictionary<string, bool> InteractionStates { get; set; } // 交互状态
}
```

### SaveSlotInfo（轻量，用于列表展示）

```csharp
public class SaveSlotInfo
{
    public required string SlotId { get; set; }      // 存档槽标识
    public string? Name { get; set; }                // 存档名称
    public DateTimeOffset CreateTime { get; set; }   // 创建时间
    public DateTimeOffset UpdateTime { get; set; }    // 修改时间
    public byte[]? Thumbnail { get; set; }           // 缩略图（可选）
    public string? GameVersion { get; set; }          // 游戏版本
}
```

---

## 基本使用

### 保存存档

```csharp
var saveData = new SaveData
{
    GameVersion = "1.0.0",
    Name = "第一章完成",
    CurrentRouterState = new RouterState
    {
        Path = "/chapter1/ending",
        CurrentSceneIndex = 3,
        SceneStates = new List<SceneState>
        {
            new SceneState { SceneName = "intro", InteractionStates = new Dictionary<string, bool> { ["choice1"] = true } }
        }
    }
};

await saveService.SaveAsync("slot_001", saveData);
```

### 加载存档

```csharp
var data = await saveService.LoadAsync("slot_001");
if (data != null)
{
    Console.WriteLine($"加载存档: {data.Name}");
    Console.WriteLine($"当前路由: {data.CurrentRouterState.Path}");
}
```

### 删除存档

```csharp
await saveService.DeleteAsync("slot_001");
```

### 获取存档列表

```csharp
var slots = await saveService.GetAllSaveSlotsAsync();
foreach (var slot in slots)
{
    Console.WriteLine($"{slot.SlotId}: {slot.Name} ({slot.UpdateTime})");
}
```

### 检查存档是否存在

```csharp
if (await saveService.ExistsAsync("slot_001"))
{
    // 存档存在
}
```

---

## 自定义加密

实现 `IEncryption` 接口即可自定义加密逻辑：

```csharp
public class MyEncryption : IEncryption
{
    public byte[] Encrypt(byte[] data)
    {
        // 自定义加密逻辑
        return MyEncryptAlgorithm(data);
    }

    public byte[] Decrypt(byte[] data)
    {
        // 自定义解密逻辑
        return MyDecryptAlgorithm(data);
    }
}

// 使用
services.AddSingleton<IEncryption>(new MyEncryption());
services.AddLingFanEngine();
```

---

## 默认加密说明

引擎默认使用 **AES-256-CBC** 加密，密钥基于本机机器名和用户名生成：

- 优点：本机存档不会被其他电脑轻易读取
- 缺点：重装系统或换电脑后存档无法解密

如需跨平台共享存档或更强的加密，请自行实现 `IEncryption`。

---

## 设计原则

1. **引擎层只管存储** — 不关心数据结构含义，由游戏逻辑层解释
2. **AOT 友好** — 使用具体类型，无反射、无动态 IL 生成
3. **开发者可扩展** — 加密逻辑、存储格式均可自定义
