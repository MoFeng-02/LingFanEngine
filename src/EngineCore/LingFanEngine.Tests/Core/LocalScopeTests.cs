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
    public void FileName_SingleLetter_S_NotMisclassifiedAsScoped()
    {
        // 文件名恰为 "S"：文件级键不得被 IsScopedLocal 误判为场景级（进 scene 被误清）
        var key = LocalScope.Key("S", null, null, "x");
        key.Should().Be("_local_SS_x");
        LocalScope.IsScopedLocal(key).Should().BeFalse();

        // 读写与清理闭环：ClearFileLevel 能清到（前缀加倍后仍精确匹配）
        var s = Scope(file: "S");
        LocalScope.Write(s, "_local_x", 1);
        LocalScope.Read(s, "x").Should().Be(1);
        LocalScope.ClearFileLevel(s, "S");
        LocalScope.Read(s, "x").Should().BeNull();
    }

    [Fact]
    public void FileName_SingleLetter_L_NotMisclassifiedAsScoped()
    {
        var key = LocalScope.Key("L", null, null, "x");
        key.Should().Be("_local_LL_x");
        LocalScope.IsScopedLocal(key).Should().BeFalse();
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

    [Fact]
    public void CrossFile_Isolation_SameNamedLet_ResolvesPerFile()
    {
        // 跨文件隔离：文件 A 与文件 B 各有同名 let "x"，互不可见（类 JS 模块级隔离）。
        var s = new StateContainer();
        s.Set(StateKeys.Scene.CurrentFile, "a.story");
        LocalScope.Write(s, "_local_x", 1);   // A 的局部
        s.Set(StateKeys.Scene.CurrentFile, "b.story");
        LocalScope.Write(s, "_local_x", 2);   // B 的局部（同名，独立键）

        s.Set(StateKeys.Scene.CurrentFile, "a.story");
        LocalScope.Read(s, "x").Should().Be(1);   // A 读 A 的
        s.Set(StateKeys.Scene.CurrentFile, "b.story");
        LocalScope.Read(s, "x").Should().Be(2);   // B 读 B 的（绝不泄漏 A 的 1）
    }

    [Fact]
    public void CrossFile_Isolation_LocalOnlyDeclaredInFileA_NotLeakedToB()
    {
        // 文件 B 读取只在文件 A 声明的局部 let "y"：必须返回 null（不泄漏），
        // 且不能经由 Read 的全局兜底（state.Get("y")）误命中 A 的局部键。
        var s = new StateContainer();
        s.Set(StateKeys.Scene.CurrentFile, "a.story");
        LocalScope.Write(s, "_local_y", 99);   // 仅文件 A 声明

        s.Set(StateKeys.Scene.CurrentFile, "b.story");
        LocalScope.Read(s, "y").Should().BeNull();   // 不泄漏到 B、全局也无 y
    }

    [Fact]
    public void CrossFile_Isolation_SceneScopeOfFileA_NotVisibleInFileB()
    {
        // 文件 A 的场景级局部 _local_S_<A>_S1_x 在文件 B 作用域下不可见（文件+场景三维隔离）。
        var s = new StateContainer();
        s.Set(StateKeys.Scene.CurrentFile, "a.story");
        s.Set(StateKeys.Scene.CurrentName, "S1");
        LocalScope.Write(s, "_local_x", 11);   // A 的场景 S1 局部

        s.Set(StateKeys.Scene.CurrentFile, "b.story");
        s.Set(StateKeys.Scene.CurrentName, "S1");
        LocalScope.Read(s, "x").Should().BeNull();   // B 的同名场景局部不存在

        // 即便在文件 A 内、但换一个场景 S2，原 S1 场景局部也不泄漏
        s.Set(StateKeys.Scene.CurrentFile, "a.story");
        s.Set(StateKeys.Scene.CurrentName, "S2");
        LocalScope.Read(s, "x").Should().BeNull();
    }

    [Fact]
    public void ClearFileLevel_RemovesOnlyThatFile_FileLevelLocals()
    {
        // 锁定「离开文件即清」的精确语义：ClearFileLevel 只清指定文件的文件级局部，
        // 不影响其他文件、也不误伤场景/标签级局部。
        var s = new StateContainer();
        s.Set(StateKeys.Scene.CurrentFile, "a.dsl");
        LocalScope.Write(s, "_local_cfg", 1);     // A 文件级
        s.Set(StateKeys.Scene.CurrentName, "S1");
        LocalScope.Write(s, "_local_sx", 10);      // A 场景级（键 _local_S_a_dsl_S1_sx）
        s.Set(StateKeys.Scene.CurrentFile, "b.dsl");
        s.Remove(StateKeys.Scene.CurrentName);  // 离开场景，回到文件级（否则会写成场景级键）
        LocalScope.Write(s, "_local_cfg", 2);      // B 文件级（键 _local_b_dsl_cfg）

        LocalScope.ClearFileLevel(s, "a.dsl");     // 离开文件 A

        s.ContainsKey("_local_a_dsl_cfg").Should().BeFalse();  // A 文件级被清
        s.Get<object>("_local_b_dsl_cfg").Should().Be(2);      // B 文件级保留
        s.Get<object>("_local_S_a_dsl_S1_sx").Should().Be(10); // A 场景级未被误伤
    }
}
