using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// LocalScope 作用域键单元测试——锁定 let/local 的 file / scene / label 三维隔离语义：
/// <list type="bullet">
///   <item>file 顶层 let → 文件生命作用域（跨文件不冲突）；</item>
///   <item>scene / label 包裹的 let → 各自独立作用域，互不冲突；</item>
///   <item>JS 语义：内层可见外层（文件→场景→子标签），内层不外泄到兄弟/外层；</item>
///   <item>引擎内部 _local___for_* / _local___switch_* 保持非作用域化（精确键）。</item>
/// </list>
/// </summary>
public class LocalScopeTests
{
    private static StateContainer Scope(string? file = null, string? scene = null, string? label = null)
    {
        var s = new StateContainer();
        if (file != null) s.Set(StateKeys.Scene.CurrentFile, file);
        if (scene != null) s.Set(StateKeys.Scene.CurrentName, scene);
        if (label != null) s.Set(StateKeys.Scene.CurrentLabel, label);
        return s;
    }

    [Fact]
    public void Write_AtFileScope_UsesFileScopedKey()
    {
        var s = Scope(file: "A");
        LocalScope.Write(s, "_local_x", 1);
        s.Get<object>("_local_A_x").Should().Be(1);
    }

    [Fact]
    public void CrossFile_SameLocalName_DoNotCollide()
    {
        var a = Scope(file: "A");
        var b = Scope(file: "B");
        LocalScope.Write(a, "_local_x", 1);
        LocalScope.Write(b, "_local_x", 2);

        LocalScope.Read(a, "x").Should().Be(1);
        LocalScope.Read(b, "x").Should().Be(2);
        // 精确键互不污染
        a.Get<object>("_local_B_x").Should().BeNull();
        b.Get<object>("_local_A_x").Should().BeNull();
    }

    [Fact]
    public void SameFile_DifferentScenes_AreIndependent()
    {
        var s1 = Scope(file: "A", scene: "S1");
        var s2 = Scope(file: "A", scene: "S2");
        LocalScope.Write(s1, "_local_x", 10);
        LocalScope.Write(s2, "_local_x", 20);

        LocalScope.Read(s1, "x").Should().Be(10);
        LocalScope.Read(s2, "x").Should().Be(20);
    }

    [Fact]
    public void FileLevelLocal_VisibleInsideScene_OuterToInner()
    {
        var s = Scope(file: "A");           // 文件顶层
        LocalScope.Write(s, "_local_x", 100);

        s.Set(StateKeys.Scene.CurrentName, "S1"); // 进入场景 S1
        // 场景内读取应回退到文件级
        LocalScope.Read(s, "x").Should().Be(100);
        // 且场景内写入落在场景作用域，不污染文件级
        LocalScope.Write(s, "_local_y", 7);
        s.Get<object>("_local_S_A_S1_y").Should().Be(7);
        s.Get<object>("_local_A_y").Should().BeNull();
    }

    [Fact]
    public void SceneLevelLocal_VisibleInsideSubLabel_InnerSeesOuter()
    {
        var s = Scope(file: "A", scene: "S1");
        LocalScope.Write(s, "_local_x", 5);            // 场景级

        s.Set(StateKeys.Scene.CurrentLabel, "L");      // 进入子标签 L
        LocalScope.Read(s, "x").Should().Be(5);        // 回退到场景级

        LocalScope.Write(s, "_local_x", 6);            // 子标签内写入
        s.Get<object>("_local_L_A_S1_L_x").Should().Be(6);
        LocalScope.Read(s, "x").Should().Be(6);        // 内层优先

        s.Remove(StateKeys.Scene.CurrentLabel);       // 退出子标签回到场景级
        LocalScope.Read(s, "x").Should().Be(5);        // 恢复场景级
    }

    [Fact]
    public void SubLabelLocal_DoesNotLeakToSiblingLabel()
    {
        var s = Scope(file: "A", scene: "S1", label: "L1");
        LocalScope.Write(s, "_local_x", 1);

        s.Set(StateKeys.Scene.CurrentLabel, "L2");     // 兄弟子标签
        LocalScope.Read(s, "x").Should().BeNull();
    }

    [Fact]
    public void GlobalVariable_NotScoped()
    {
        var s = Scope(file: "A", scene: "S1", label: "L");
        LocalScope.Write(s, "gold", 99);               // 非 _local_ 前缀
        s.Get<object>("gold").Should().Be(99);
        LocalScope.Read(s, "gold").Should().Be(99);
    }

    [Fact]
    public void InternalForSwitchVars_RemainUnscoped()
    {
        var s = Scope(file: "A", scene: "S1", label: "L");
        LocalScope.Write(s, "_local___for_idx_0", 7);  // 引擎内部，排除作用域
        s.Get<object>("_local___for_idx_0").Should().Be(7);
        LocalScope.Read(s, "_local___for_idx_0").Should().Be(7);

        LocalScope.Write(s, "_local___switch_val_3", 8);
        s.Get<object>("_local___switch_val_3").Should().Be(8);

        // 普通用户局部变量仍作用域化（不与内部变量混淆）；当前作用域含 label=L
        LocalScope.Write(s, "_local_for_idx", 1);
        s.Get<object>("_local_L_A_S1_L_for_idx").Should().Be(1);
    }

    [Fact]
    public void ResolveKey_ReturnsScopedKey()
    {
        var s = Scope(file: "A", scene: "S1");
        LocalScope.ResolveKey(s, "_local_x").Should().Be("_local_S_A_S1_x");
        // 全局键原样返回
        LocalScope.ResolveKey(s, "gold").Should().Be("gold");
        // 内部 for 键原样返回
        LocalScope.ResolveKey(s, "_local___for_idx_0").Should().Be("_local___for_idx_0");
    }

    [Fact]
    public void NoScopeState_WritesFlatLocalKey_ForBackwardCompat()
    {
        var s = new StateContainer(); // 无任何作用域键
        LocalScope.Write(s, "_local_x", 42);
        s.Get<object>("_local_x").Should().Be(42);
        LocalScope.Read(s, "x").Should().Be(42);
    }

    [Theory]
    [InlineData("_local_A_x", false)]                 // 文件级：跨 scene 保活
    [InlineData("_local_S_A_S1_x", true)]             // 场景级：应清
    [InlineData("_local_L_A_S1_L_x", true)]           // 标签级：应清
    [InlineData("_local_L_A_L_x", true)]              // 标签级（无场景）：应清
    [InlineData("_local___for_idx_0", true)]          // 引擎内部 for：应清（与旧清所有 _local_ 行为一致）
    [InlineData("_local___switch_val_3", true)]       // 引擎内部 switch：应清
    [InlineData("gold", false)]                        // 全局变量：无关，保活
    [InlineData("_local_x", false)]                    // 无文件扁平键（兼容态）：保活
    public void IsScopedLocal_DiscriminatesFileLevelFromScoped(string key, bool expected)
        => LocalScope.IsScopedLocal(key).Should().Be(expected);

    [Fact]
    public void ClearLocalVariables_Contract_KeepsFileLevel_ButClearsScoped()
    {
        // 复刻 GameLoop.ClearLocalVariables 的精确逻辑（它委托给 LocalScope.IsScopedLocal）。
        // 用引擎真实写入路径产生各作用域键，锁定「文件级 let 跨 scene 保活、场景/标签/内部局部被清」这一新不变量。
        var s = new StateContainer();
        s.Set(StateKeys.Scene.CurrentFile, "story.dsl");

        LocalScope.Write(s, "_local_cfg", 1);          // 文件顶层 let → 文件生命作用域
        s.Set(StateKeys.Scene.CurrentName, "S1");       // 进入场景
        LocalScope.Write(s, "_local_sx", 10);           // 场景级 let
        LocalScope.Write(s, "_local___for_i", 0);       // 引擎内部 for 循环键（精确键、非作用域）
        s.Set("gold", 999);                             // 全局变量（非 _local_）

        var scoped = s.Keys.Where(LocalScope.IsScopedLocal).ToList();
        foreach (var k in scoped) s.Remove(k);

        // 文件级保活（类 JS 模块级 let，跨 scene 不丢）
        s.Get<object>("_local_story_dsl_cfg").Should().Be(1);
        // 全局保活
        s.Get<object>("gold").Should().Be(999);
        // 场景级被清
        s.ContainsKey("_local_S_story_dsl_S1_sx").Should().BeFalse();
        // 引擎内部循环键随场景切换被清（与旧行为一致）
        s.ContainsKey("_local___for_i").Should().BeFalse();
    }

    [Fact]
    public void FileLevelLocal_SurvivesSceneEntry_AndVisibleInsideScene()
    {
        // 端到端语义：文件级 let 进入 scene 后仍存在且对场景内可见（JS 模块级变量）。
        var s = new StateContainer();
        s.Set(StateKeys.Scene.CurrentFile, "story.dsl");

        LocalScope.Write(s, "_local_cfg", 7);           // 文件级
        // 进入场景 S1 并清局部（复刻 GameLoop 场景切换）
        s.Set(StateKeys.Scene.CurrentName, "S1");
        var scoped = s.Keys.Where(LocalScope.IsScopedLocal).ToList();
        foreach (var k in scoped) s.Remove(k);

        // 文件级仍在
        s.Get<object>("_local_story_dsl_cfg").Should().Be(7);
        // 场景内读取回退到文件级
        LocalScope.Read(s, "cfg").Should().Be(7);
    }
}
