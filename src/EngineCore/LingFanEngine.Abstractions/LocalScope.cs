using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Abstractions;

/// <summary>
/// 局部变量（let/local）作用域键工具
/// <para>作用域键格式（不对称设计，保证 ClearLocalVariables 能稳健区分文件级与场景/标签级）：</para>
/// <para>· 文件级（最顶层、未被 scene/label 包裹）：<c>_local_&lt;file&gt;_&lt;name&gt;</c>（保持历史格式，存量零破坏）</para>
/// <para>· 场景级：<c>_local_S_&lt;file&gt;_&lt;scene&gt;_&lt;name&gt;</c></para>
/// <para>· 标签级：<c>_local_L_&lt;file&gt;[_&lt;scene&gt;]_&lt;label&gt;_&lt;name&gt;</c></para>
/// <para>读取回退链：label → scene → file（JS 语义：内层可见外层，内层不外泄，兄弟作用域互不冲突），最后回退全局键。</para>
/// <para>file 来自 executor 在每条命令执行前按命令携带的 SourceFile 写入的 __current_file；
/// scene 来自 __current_scene_name（SceneCommand/NavigateCommand 写入）；
/// label 来自 executor 按 labels 映射反查「最近前置 label」写入的 __current_label。</para>
/// <para>引擎内部局部变量（_local___for_* / _local___switch_*）保持非作用域化，按精确键读写，不参与回退；但随 scene 切换被 ClearLocalVariables 清掉。</para>
/// <para>类 JS 作用域语义：进入 scene 只清场景/标签级局部键（含引擎内部循环键），<b>保留文件级局部</b>（模块级 let 跨 scene 保活）。</para>
/// </summary>
public static class LocalScope
{
    private const string Prefix = "_local_";
    private const string InternalFor = "__for_";
    private const string InternalSwitch = "__switch_";

    /// <summary>把作用域片段清洗为安全键片段（非字母数字统一为下划线）</summary>
    public static string Sanitize(string? part)
    {
        if (string.IsNullOrEmpty(part)) return "";
        var sb = new System.Text.StringBuilder(part.Length);
        foreach (var c in part)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }

    /// <summary>作用域键（不对称设计：文件级保持 _local_&lt;file&gt;_&lt;baseName&gt;，场景/标签级带 S_/L_ 标记，便于 ClearLocalVariables 区分）</summary>
    public static string Key(string? file, string? scene, string? label, string baseName)
    {
        var f = Sanitize(file);
        var s = Sanitize(scene);
        var l = Sanitize(label);
        var sb = new System.Text.StringBuilder(Prefix);
        if (f.Length > 0 && l.Length > 0)
        {
            // 标签作用域：_local_L_<file>[_<scene>]_<label>_<baseName>
            sb.Append('L').Append('_').Append(f);
            if (s.Length > 0) sb.Append('_').Append(s);
            sb.Append('_').Append(l).Append('_').Append(baseName);
        }
        else if (f.Length > 0 && s.Length > 0)
        {
            // 场景作用域：_local_S_<file>_<scene>_<baseName>
            sb.Append('S').Append('_').Append(f).Append('_').Append(s).Append('_').Append(baseName);
        }
        else if (f.Length > 0)
        {
            // 文件作用域：保持历史格式 _local_<file>_<baseName>（进入 scene 不清此键）。
            // 文件名恰为 S/L 时加倍（S→SS）：否则文件级键 _local_S_x / _local_L_x 会被
            // IsScopedLocal 误判为场景/标签级而在进 scene 时被 ClearLocalVariables 误清
            // （仅影响本已损坏的特例，正常文件名键格式不变）
            var fileSeg = f is "S" or "L" ? f + f : f;
            sb.Append(fileSeg).Append('_').Append(baseName);
        }
        else
        {
            // 无文件（理论不触发）：退化为扁平键
            sb.Append(baseName);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 是否「场景/标签作用域」的局部键（进入 scene 应被清掉）。
    /// <para>判定：以 <c>_local_S_</c> 或 <c>_local_L_</c> 开头，或含引擎内部循环键标记（_local___for_ / _local___switch_）。</para>
    /// <para>文件级 <c>_local_&lt;file&gt;_&lt;base&gt;</c> 与无文件扁平键不在列——它们跨 scene 保活（类 JS 模块级 let）。</para>
    /// </summary>
    public static bool IsScopedLocal(string key)
    {
        if (!key.StartsWith(Prefix, System.StringComparison.Ordinal)) return false;
        if (key.StartsWith(Prefix + "S_", System.StringComparison.Ordinal)) return true;
        if (key.StartsWith(Prefix + "L_", System.StringComparison.Ordinal)) return true;
        // 引擎内部循环键也随场景切换清掉（与旧行为一致：旧 ClearLocalVariables 清所有 _local_）
        if (key.Contains(InternalFor) || key.Contains(InternalSwitch)) return true;
        return false;
    }

    /// <summary>
    /// 清掉指定文件的<b>文件级</b>局部键（<c>_local_&lt;file&gt;_&lt;base&gt;</c>），保留场景/标签级
    /// （<c>_local_S_*</c> / <c>_local_L_*</c>）与引擎内部循环键（<c>_local___for_</c> / <c>_local___switch_</c>）。
    /// <para>用途：离开文件（执行流跨到另一文件）时销毁旧文件的文件级 scratch——类 JS 块级作用域，
    /// 「出文件作用域即不存在」，下次进入由顶层 let 自然重建。叙事游戏换章/换图清草稿语义。</para>
    /// <para>安全：前缀 <c>_local_&lt;file&gt;_</c> 不会命中场景/标签级键（后者在 file 段前夹了 S_/L_ 标记），
    /// 且用 !IsScopedLocal 二次把关，避免文件名为 "S"/"L" 等极端情形误清。</para>
    /// </summary>
    public static void ClearFileLevel(IStateContainer state, string file)
    {
        var f = Sanitize(file);
        if (f.Length == 0) return;
        // 文件段处理与 Key() 文件级分支一致（S/L 加倍），确保清理前缀与生成键精确匹配
        var fileSeg = f is "S" or "L" ? f + f : f;
        var prefix = Prefix + fileSeg + "_";
        foreach (var k in state.Keys
                     .Where(k => k.StartsWith(prefix, System.StringComparison.Ordinal)
                              && !IsScopedLocal(k))
                     .ToList())
        {
            state.Remove(k);
        }
    }

    /// <summary>从状态读取当前作用域（file / scene / label）</summary>
    public static (string File, string? Scene, string? Label) Current(IStateContainer state)
    {
        var file = state.Get<string>(StateKeys.Scene.CurrentFile) ?? "";
        var scene = state.Get<string>(StateKeys.Scene.CurrentName);
        var label = state.Get<string>(StateKeys.Scene.CurrentLabel);
        return (file, scene, label);
    }

    /// <summary>读取局部变量：按 label→scene→file 回退，最后回退全局键 <paramref name="name"/></summary>
    /// <para>name 可为裸名（如 let "x" 后读 "x" / "_local_x"）或带 <c>_local_</c> 前缀的引用
    /// （如表达式 <c>{_local_i + 1}</c>、<c>while {_local_i < 3}</c>）——后者剥前缀后按作用域链查，
    /// 与 <see cref="Write"/> 的剥前缀行为对称，保证读写命中同一键（不对称时读到 null，
    /// 循环条件 null&lt;N 恒真导致死循环）。</para>
    public static object? Read(IStateContainer state, string name)
    {
        var (file, scene, label) = Current(state);
        // 与 Write 对称：Write("_local_x") 剥前缀取 baseName "x"，读取带前缀引用时同样剥前缀
        var scopedName = name.StartsWith(Prefix, System.StringComparison.Ordinal)
            ? name[Prefix.Length..]
            : name;
        var baseName = scopedName.Replace('.', '_');
        if (!string.IsNullOrEmpty(label))
        {
            var v = state.Get<object>(Key(file, scene, label, baseName));
            if (v != null) return v;
        }
        if (!string.IsNullOrEmpty(scene))
        {
            var v = state.Get<object>(Key(file, scene, null, baseName));
            if (v != null) return v;
        }
        var top = state.Get<object>(Key(file, null, null, baseName));
        if (top != null) return top;
        return state.Get<object>(name);
    }

    /// <summary>
    /// 写入变量：局部键（_local_ 前缀且非引擎内部 __for_/__switch_）按当前作用域重算键；其余原样写入。
    /// </summary>
    public static void Write(IStateContainer state, string key, object? value)
    {
        if (key.StartsWith(Prefix, System.StringComparison.Ordinal)
            && !key.Contains(InternalFor)
            && !key.Contains(InternalSwitch))
        {
            var baseName = key.Substring(Prefix.Length);
            var (file, scene, label) = Current(state);
            state.Set(Key(file, scene, label, baseName), value);
        }
        else
        {
            state.Set(key, value);
        }
    }

    /// <summary>
    /// 解析变量实际状态键：局部键按当前作用域重算；引擎内部/全局键原样返回。
    /// 供写前判重（define...once）与 Write 共用，确保判重与写入使用同一键。
    /// </summary>
    public static string ResolveKey(IStateContainer state, string key)
    {
        if (key.StartsWith(Prefix, System.StringComparison.Ordinal)
            && !key.Contains(InternalFor)
            && !key.Contains(InternalSwitch))
        {
            var baseName = key.Substring(Prefix.Length);
            var (file, scene, label) = Current(state);
            return Key(file, scene, label, baseName);
        }
        return key;
    }
}
