using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Abstractions;

/// <summary>
/// 局部变量（let/local）作用域键工具
/// <para>作用域键格式：<c>_local_&lt;file&gt;[_&lt;scene&gt;][_&lt;label&gt;]_&lt;name&gt;</c></para>
/// <para>读取回退链：label → scene → file（JS 语义：内层可见外层，内层不外泄，兄弟作用域互不冲突），最后回退全局键。</para>
/// <para>file 来自 executor 在每条命令执行前按命令携带的 SourceFile 写入的 __current_file；
/// scene 来自 __current_scene_name（SceneCommand/NavigateCommand 写入）；
/// label 来自 executor 按 labels 映射反查「最近前置 label」写入的 __current_label。</para>
/// <para>引擎内部局部变量（_local___for_* / _local___switch_*）保持非作用域化，按精确键读写，不参与回退。</para>
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

    /// <summary>作用域键：_local_&lt;file&gt;[_&lt;scene&gt;][_&lt;label&gt;]_&lt;baseName&gt;</summary>
    public static string Key(string? file, string? scene, string? label, string baseName)
    {
        var f = Sanitize(file);
        var s = Sanitize(scene);
        var l = Sanitize(label);
        var sb = new System.Text.StringBuilder(Prefix);
        if (f.Length > 0) sb.Append(f).Append('_');
        if (s.Length > 0) sb.Append(s).Append('_');
        if (l.Length > 0) sb.Append(l).Append('_');
        sb.Append(baseName);
        return sb.ToString();
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
    public static object? Read(IStateContainer state, string name)
    {
        var (file, scene, label) = Current(state);
        var baseName = name.Replace('.', '_');
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
