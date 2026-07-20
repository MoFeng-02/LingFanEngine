# 12 · 用 C# 扩展

DSL 适合写剧情，但有些逻辑用 C# 更合适——复杂的战斗系统、自定义 UI、外部 API 调用等。本章讲解如何用 C# 扩展游戏。

## StoryScript 基类

继承 `StoryScript` 创建 C# 场景：

```csharp
using LingFanEngine.Games;

public class BattleScene : StoryScript
{
    public override string SceneName => "cs_battle";
    public override string SceneType => "game";

    public override async Task RunAsync()
    {
        await SayAsync("野怪出现！", speaker: "系统");
        var enemyHp = Random.Shared.Next(30, 50);
        await SayAsync($"敌人 HP: {enemyHp}", speaker: "系统");

        var damage = Random.Shared.Next(10, 30);
        enemyHp -= damage;
        await SayAsync($"造成 {damage} 点伤害！", speaker: "系统");

        if (enemyHp <= 0)
        {
            await SayAsync("敌人被击败！", speaker: "系统");
            State.Set("player.exp", State.Get<int>("player.exp") + 30);
        }
        else
        {
            await SayAsync("敌人还在。", speaker: "系统");
        }

        await NavigateAsync("town");
    }
}
```

## 注册 C# 场景

在 `ServiceExtensions.cs` 中注册：

```csharp
public static IServiceCollection AddGameScripts(this IServiceCollection services)
{
    services.AddSingleton<StoryScript, BattleScene>();
    services.AddSingleton<StoryScript, ShopScene>();
    return services;
}
```

然后在 DSL 中用 `navigate` 跳转：

```dsl
button "战斗" nav="cs_battle"
```

::: tip 双模平权
DSL 和 C# 可以互相跳转。DSL 场景可以 `navigate` 到 C# 场景，C# 场景可以 `NavigateAsync` 到 DSL 场景。
:::

## C# API 一览

### 对话

```csharp
await SayAsync("对话文本", speaker: "老张");
await ExtendDialogAsync("追加文本");  // 不换行追加
await WaitForClickAsync();             // 等待点击
```

### 模板

```csharp
await SayAsync("内心独白", speaker: "旁白", template: "center");
```

### 过渡与等待

```csharp
await TransitionAsync("fade", duration: 1.5);
await SkipableWaitAsync(2.0);  // 可跳过的等待
```

### 菜单与输入

```csharp
var choice = await ShowMenuAsync("你要怎么做？", "战斗", "逃跑");
if (choice == 0) { /* 战斗 */ }

var input = await InputAsync("请输入名字：");
State.Set("player.name", input);
```

### UI 面板

```csharp
var result = await CallScreenAsync("save_panel");
if (result?.ToString() == "saved")
{
    await SayAsync("存档成功！", speaker: "系统");
}
```

### 视频

```csharp
await PlayCutsceneAsync("Video/intro.mp4");
```

### NVL

```csharp
await EnterNvlAsync();
await SayAsync("NVL 文本", speaker: "旁白", template: "fullscreen");
await ClearNvlAsync();
await ExitNvlAsync();
```

## 状态访问

```csharp
// 读取
var gold = State.Get<int>("player.gold");
var name = State.Get<string>("player.name");

// 写入
State.Set("player.gold", gold + 50);
State.Set("story.met_elder", true);

// 检查存在
if (State.ContainsKey("player.weapon"))
{
    var weapon = State.Get<string>("player.weapon");
}
```

## 自定义命令

### 定义命令

```csharp
public readonly record struct CalculateDamageCommand(
    int BaseAttack,
    int Defense
) : ICommand
{
    public int Result { get; init; }
}
```

### 处理器

```csharp
public class CalculateDamageHandler : ICommandHandler<CalculateDamageCommand>, IDefaultCommandHandler
{
    public void Handle(CalculateDamageCommand cmd, ICommandContext ctx)
    {
        var damage = Math.Max(1, cmd.BaseAttack - cmd.Defense);
        ctx.State.Set("_local_damage", damage);
    }
}
```

### 注册

```csharp
services.AddSingleton<IDefaultCommandHandler, CalculateDamageHandler>();
```

### DSL 中调用

通过 C# 桥接，DSL 可以间接触发自定义命令。

## 场景互跳

### DSL → C#

```dsl
navigate "cs_battle"   // 跳到 C# 场景
```

### C# → DSL

```csharp
await NavigateAsync("town_entrance");  // 跳到 DSL 场景
```

### C# → C#

```csharp
await NavigateAsync("cs_shop");
```

## 回溯与 C# 场景

C# 场景通过 `CreateSceneCheckpoint()` 创建检查点：

```csharp
public override async Task RunAsync()
{
    await SayAsync("战斗开始前");
    CreateSceneCheckpoint();  // 创建检查点

    // 战斗逻辑
    await SayAsync("战斗结束");
}
```

回溯时，引擎通过异常机制终止旧 Runner，从检查点重新执行。

::: tip 回溯安全
C# 场景的回溯通过 `CSharpSceneReplayCancelledException` 实现。确保你的代码能正确处理异常中断——避免在 `try-finally` 中做不可逆操作。
:::

## 动手练习

写一个简单的 C# 战斗场景：

```csharp
public class SimpleBattle : StoryScript
{
    public override string SceneName => "cs_battle";
    public override string SceneType => "game";

    public override async Task RunAsync()
    {
        var enemyHp = Random.Shared.Next(30, 50);
        await SayAsync($"野怪出现！HP: {enemyHp}", speaker: "系统");

        while (enemyHp > 0)
        {
            CreateSceneCheckpoint();

            var damage = Random.Shared.Next(10, 30);
            enemyHp -= damage;
            await SayAsync($"造成 {damage} 伤害！敌人剩余 {Math.Max(0, enemyHp)} HP", speaker: "系统");

            if (enemyHp <= 0) break;

            var enemyDmg = Random.Shared.Next(5, 15);
            var playerHp = State.Get<int>("player.hp") - enemyDmg;
            State.Set("player.hp", Math.Max(0, playerHp));
            await SayAsync($"你受到 {enemyDmg} 伤害！", speaker: "系统");
        }

        await SayAsync("战斗胜利！", speaker: "系统");
        State.Set("player.exp", State.Get<int>("player.exp") + 30);
        await NavigateAsync("town");
    }
}
```

## 下一步

C# 扩展掌握了！下一章学习[自定义 UI 面板](./13-自定义UI面板)——用 C# 制作存档、设置等界面。
