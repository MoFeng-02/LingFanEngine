using System.Collections.Concurrent;
using FluentAssertions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using Xunit;

namespace LingFanEngine.Tests.Core;

public class StateContainerTests
{
    [Fact]
    public void Set_Get_RoundTrip()
    {
        var container = new StateContainer();
        container.Set("key", 42);
        var value = container.Get<int>("key");
        value.Should().Be(42);
    }

    [Fact]
    public void Get_NonExistent_ReturnsDefault()
    {
        var container = new StateContainer();
        var value = container.Get<int>("nonexistent");
        value.Should().Be(0);
    }

    [Fact]
    public void Get_NonExistent_ReturnsNullForReferenceType()
    {
        var container = new StateContainer();
        var value = container.Get<string>("nonexistent");
        value.Should().BeNull();
    }

    [Fact]
    public void Get_WrongType_ReturnsDefault()
    {
        var container = new StateContainer();
        // 存入不可转换类型（Dictionary 无法转为 int）
        container.Set("key", new Dictionary<string, object?>());
        var value = container.Get<int>("key");
        value.Should().Be(0); // default(int)
    }

    [Fact]
    public void Get_ConvertibleType_Succeeds()
    {
        // 字符串 "42" 可转换为 int 42（存档反序列化后的常见场景）
        var container = new StateContainer();
        container.Set("key", "42");
        var value = container.Get<int>("key");
        value.Should().Be(42);
    }

    [Fact]
    public void TryGet_Existing_ReturnsTrue()
    {
        var container = new StateContainer();
        container.Set("key", 42);
        var success = container.TryGet<int>("key", out var value);
        success.Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void TryGet_NonExistent_ReturnsFalse()
    {
        var container = new StateContainer();
        var success = container.TryGet<int>("nonexistent", out var value);
        success.Should().BeFalse();
        value.Should().Be(0);
    }

    [Fact]
    public void TryGet_WrongType_ReturnsFalse()
    {
        var container = new StateContainer();
        // 存入不可转换类型
        container.Set("key", new Dictionary<string, object?>());
        var success = container.TryGet<int>("key", out var value);
        success.Should().BeFalse();
        value.Should().Be(0);
    }

    [Fact]
    public void ContainsKey_True_AfterSet()
    {
        var container = new StateContainer();
        container.Set("key", 42);
        container.ContainsKey("key").Should().BeTrue();
    }

    [Fact]
    public void ContainsKey_False_ForNonExistent()
    {
        var container = new StateContainer();
        container.ContainsKey("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void Remove_Success()
    {
        var container = new StateContainer();
        container.Set("key", 42);
        container.Remove("key").Should().BeTrue();
        container.ContainsKey("key").Should().BeFalse();
    }

    [Fact]
    public void Remove_NonExistent_ReturnsFalse()
    {
        var container = new StateContainer();
        container.Remove("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void GetSnapshot_IsImmutable()
    {
        var container = new StateContainer();
        container.Set("key", 42);
        var snapshot = container.GetSnapshot();

        snapshot.Should().NotBeNull();
        snapshot.Should().HaveCount(1);
        snapshot.ContainsKey("key").Should().BeTrue();

        // 修改容器不应影响快照
        container.Remove("key");
        snapshot.ContainsKey("key").Should().BeTrue();
        container.ContainsKey("key").Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_Access_NoCorruption()
    {
        var container = new StateContainer();
        var tasks = new ConcurrentBag<int>();

        // 10 个线程并发写入
        var writeTasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
        {
            for (var j = 0; j < 100; j++)
            {
                var key = $"thread_{i}_iter_{j}";
                container.Set(key, i * 100 + j);
            }
        })).ToArray();

        await Task.WhenAll(writeTasks);

        // 验证所有值都正确写入
        for (var i = 0; i < 10; i++)
        {
            for (var j = 0; j < 100; j++)
            {
                var key = $"thread_{i}_iter_{j}";
                var value = container.Get<int>(key);
                value.Should().Be(i * 100 + j);
            }
        }
    }

    [Fact]
    public void Keys_ReflectsCurrentState()
    {
        var container = new StateContainer();
        container.Set("key1", 1);
        container.Set("key2", 2);
        container.Set("key3", 3);

        var keys = container.Keys.ToList();
        keys.Should().HaveCount(3);
        keys.Should().Contain(["key1", "key2", "key3"]);
    }

    [Fact]
    public void Clear_RemovesAllKeys()
    {
        var container = new StateContainer();
        container.Set("key1", 1);
        container.Set("key2", 2);
        container.Clear();

        container.Keys.Should().BeEmpty();
        container.Get<int>("key1").Should().Be(0);
        container.Get<int>("key2").Should().Be(0);
    }

    // ========== 点分路径遍历测试 ==========

    [Fact]
    public void Get_NestedDict_ByPath_ReturnsValue()
    {
        var container = new StateContainer();
        container.Set("player", new Dictionary<string, object?> { ["hp"] = 100, ["name"] = "hero" });

        container.Get<object>("player.hp").Should().Be(100);
        container.Get<string>("player.name").Should().Be("hero");
    }

    [Fact]
    public void Get_DeepNestedDict_ByPath_ReturnsValue()
    {
        var container = new StateContainer();
        container.Set("player", new Dictionary<string, object?>
        {
            ["stats"] = new Dictionary<string, object?> { ["hp"] = 50, ["mp"] = 30 }
        });

        container.Get<object>("player.stats.hp").Should().Be(50);
        container.Get<object>("player.stats.mp").Should().Be(30);
    }

    [Fact]
    public void Get_Path_NotFound_ReturnsDefault()
    {
        var container = new StateContainer();
        container.Set("player", new Dictionary<string, object?> { ["hp"] = 100 });

        container.Get<int>("player.mp").Should().Be(0); // 不存在的叶节点
        container.Get<int>("enemy.hp").Should().Be(0);  // 不存在的根节点
    }

    [Fact]
    public void Get_FlatKey_Priority_OverPath()
    {
        // 扁平键 "player.hp" 优先于嵌套路径 player → hp
        var container = new StateContainer();
        container.Set("player", new Dictionary<string, object?> { ["hp"] = 100 });
        container.Set("player.hp", 999); // 扁平键

        container.Get<int>("player.hp").Should().Be(999); // 扁平键优先
    }

    [Fact]
    public void Get_Path_ThroughNonDict_ReturnsDefault()
    {
        var container = new StateContainer();
        container.Set("player", "a string"); // 不是字典

        container.Get<int>("player.hp").Should().Be(0);
    }

    [Fact]
    public void Set_NestedDict_ByPath_UpdatesLeaf()
    {
        var container = new StateContainer();
        container.Set("player", new Dictionary<string, object?> { ["hp"] = 100, ["mp"] = 50 });

        container.Set("player.hp", 80);

        // 验证通过路径读取
        container.Get<int>("player.hp").Should().Be(80);
        // 验证原始字典也被更新
        var dict = container.Get<Dictionary<string, object?>>("player")!;
        dict["hp"].Should().Be(80);
        // 其他键不受影响
        dict["mp"].Should().Be(50);
    }

    [Fact]
    public void Set_DeepNestedDict_ByPath_UpdatesLeaf()
    {
        var container = new StateContainer();
        container.Set("player", new Dictionary<string, object?>
        {
            ["stats"] = new Dictionary<string, object?> { ["hp"] = 100 }
        });

        container.Set("player.stats.hp", 75);

        container.Get<int>("player.stats.hp").Should().Be(75);
    }

    [Fact]
    public void Set_Path_ParentNotExists_FallsBackToFlatKey()
    {
        // 父级不存在时，回退为扁平键存储
        var container = new StateContainer();

        container.Set("player.hp", 100);

        // 应该作为扁平键存储
        container.Get<int>("player.hp").Should().Be(100);
        // 不应该创建 player 字典
        container.ContainsKey("player").Should().BeFalse();
    }

    [Fact]
    public void TryGet_NestedDict_ByPath_ReturnsTrue()
    {
        var container = new StateContainer();
        container.Set("player", new Dictionary<string, object?> { ["hp"] = 100 });

        var success = container.TryGet<int>("player.hp", out var value);
        success.Should().BeTrue();
        value.Should().Be(100);
    }

    [Fact]
    public void TryGet_Path_NotFound_ReturnsFalse()
    {
        var container = new StateContainer();
        container.Set("player", new Dictionary<string, object?> { ["hp"] = 100 });

        container.TryGet<int>("player.mp", out _).Should().BeFalse();
    }

    [Fact]
    public void ContainsKey_NestedDict_ByPath_ReturnsTrue()
    {
        var container = new StateContainer();
        container.Set("player", new Dictionary<string, object?> { ["hp"] = 100 });

        container.ContainsKey("player.hp").Should().BeTrue();
        container.ContainsKey("player.mp").Should().BeFalse();
        container.ContainsKey("player").Should().BeTrue();
    }

    [Fact]
    public void Remove_NestedDict_ByPath_RemovesLeaf()
    {
        var container = new StateContainer();
        container.Set("player", new Dictionary<string, object?> { ["hp"] = 100, ["mp"] = 50 });

        container.Remove("player.hp").Should().BeTrue();
        container.ContainsKey("player.hp").Should().BeFalse();
        // 父字典和其他键不受影响
        container.ContainsKey("player").Should().BeTrue();
        container.ContainsKey("player.mp").Should().BeTrue();
    }

    [Fact]
    public void Remove_Path_NotFound_ReturnsFalse()
    {
        var container = new StateContainer();
        container.Set("player", new Dictionary<string, object?> { ["hp"] = 100 });

        container.Remove("player.mp").Should().BeFalse();
        container.Remove("enemy.hp").Should().BeFalse();
    }

    [Fact]
    public void Get_NestedDict_WithDictionaryStringObject_Works()
    {
        // 测试 Dictionary<string, object>（非 nullable）也能被路径遍历
        var container = new StateContainer();
        container.Set("config", new Dictionary<string, object> { ["volume"] = 0.8 });

        container.Get<double>("config.volume").Should().Be(0.8);
    }

    [Fact]
    public void Set_NestedDict_ByPath_PreservesOtherKeys()
    {
        // 模拟 MergeIntoState 的场景：已有字典，通过路径更新单个值
        var container = new StateContainer();
        container.Set("player", new Dictionary<string, object?>
        {
            ["hp"] = 50,
            ["name"] = "hero",
            ["inventory"] = new List<object?> { "sword", "potion" }
        });

        container.Set("player.hp", 100);

        var dict = container.Get<Dictionary<string, object?>>("player")!;
        dict["hp"].Should().Be(100);
        dict["name"].Should().Be("hero");
        dict["inventory"].Should().NotBeNull();
    }
}