using System.Collections.Generic;
using System.IO;

namespace LingFanEngine.Dsl.ProjectIndex;

/// <summary>C# 符号索引：扫描宿主 <c>.cs</c>，提取命令注册 / 状态键 / 场景导航目标 / 资源引用，
/// 支撑 DSL↔C# 跨语言联动（<c>button cmd=</c> 命令名、<c>{var}</c> 变量、<c>nav=</c> 场景、资源对照查找）。
/// <para>纯文本扫描（关键字 + 引号定位），AOT 友好——零反射、无动态代码生成。为降低噪声，
/// 仅抽取「首个实参即字符串字面量」的调用（如 <c>state.Set("key", ...)</c>；<c>state.Set(StateKeys.X, ...)</c> 因首参非字面量而跳过）。</para>
/// </summary>
public sealed class CsSymbolIndex
{
    private readonly HashSet<string> _commands = new(StringComparer.Ordinal);
    private readonly HashSet<string> _variables = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sceneTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CsRef>> _resourceRefs = new(StringComparer.Ordinal);

    /// <summary>全量扫描项目根下所有 <c>.cs</c>（跳过 bin/obj 等噪声目录）。</summary>
    public void Scan(string rootPath)
    {
        _commands.Clear();
        _variables.Clear();
        _sceneTargets.Clear();
        _resourceRefs.Clear();
        foreach (var f in ProjectScanner.Enumerate(rootPath, "*.cs"))
        {
            string text;
            try { text = File.ReadAllText(f); }
            catch { continue; }
            ScanFile(f, text);
        }
    }

    private void ScanFile(string file, string text)
    {
        CollectFirstArgString(file, text, "RegisterCommand(", _commands);
        CollectFirstArgString(file, text, "RegisterCommandAsync(", _commands);
        CollectFirstArgString(file, text, "state.Set(", _variables);
        CollectFirstArgString(file, text, "state.Get(", _variables);
        CollectFirstArgString(file, text, "Navigate(", _sceneTargets);
        CollectPathAssignments(file, text);
    }

    /// <summary>抽取「marker 后首个实参为字符串字面量」调用里的那个字符串（如 <c>RegisterCommand("x")</c> / <c>state.Get&lt;int&gt;("x")</c>）。</summary>
    private void CollectFirstArgString(string file, string text, string marker, HashSet<string> sink)
    {
        var idx = 0;
        while ((idx = text.IndexOf(marker, idx, StringComparison.Ordinal)) >= 0)
        {
            var p = idx + marker.Length; // marker 已含 '('
            // 跳过可选泛型 <...>
            if (p < text.Length && text[p] == '<')
            {
                var close = text.IndexOf('>', p);
                if (close < 0) break;
                p = close + 1;
                while (p < text.Length && (text[p] == ' ' || text[p] == '\t')) p++;
                if (p >= text.Length || text[p] != '(') { idx = p; continue; }
                p++;
            }
            // 首个实参前跳过空白；要求首参即 " 开头的字面量（滤除 state.Set(StateKeys.X, "v") 之类）
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t')) p++;
            if (p >= text.Length || text[p] != '"') { idx = p; continue; }
            var end = text.IndexOf('"', p + 1);
            if (end < 0) break;
            var value = text.Substring(p + 1, end - p - 1);
            if (value.Length > 0) sink.Add(value);
            idx = end + 1;
        }
    }

    /// <summary>抽取 <c>Path = "xxx"</c> 形式的资源引用（C# 媒体命令等），记录出处行号。</summary>
    private void CollectPathAssignments(string file, string text)
    {
        const string marker = "Path";
        var idx = 0;
        while ((idx = text.IndexOf(marker, idx, StringComparison.Ordinal)) >= 0)
        {
            // 仅匹配独立标识符 Path（拒 file/Full/Rel 等含 Path 子串的单词）
            if (idx > 0)
            {
                var prev = text[idx - 1];
                if (char.IsLetterOrDigit(prev) || prev == '_') { idx++; continue; }
            }
            var p = idx + marker.Length;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t')) p++;
            if (p >= text.Length || text[p] != '=') { idx = p; continue; }
            p++;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t')) p++;
            if (p >= text.Length || text[p] != '"') { idx = p; continue; }
            var end = text.IndexOf('"', p + 1);
            if (end < 0) break;
            var value = text.Substring(p + 1, end - p - 1).Replace('\\', '/');
            if (value.Length > 0)
            {
                if (!_resourceRefs.TryGetValue(value, out var list))
                    _resourceRefs[value] = list = new List<CsRef>();
                list.Add(new CsRef(file, CountLine(text, p)));
            }
            idx = end + 1;
        }
    }

    private static int CountLine(string text, int offset)
    {
        var line = 1;
        for (var i = 0; i < offset && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    public IReadOnlyCollection<string> CommandNames => _commands;
    public IReadOnlyCollection<string> VariableKeys => _variables;
    public IReadOnlyCollection<string> SceneTargets => _sceneTargets;

    /// <summary>某资源相对路径被哪些 C# 文件引用（资源对照查找 / 未来悬停跳转用）。</summary>
    public IReadOnlyCollection<CsRef> GetResourceReferences(string relativePath)
        => _resourceRefs.TryGetValue(relativePath.Replace('\\', '/'), out var list)
            ? list : System.Array.Empty<CsRef>();
}

/// <summary>C# 侧对某个资源的引用位置（文件 + 行号）。</summary>
public readonly struct CsRef
{
    public CsRef(string file, int line) { File = file; Line = line; }
    public string File { get; }
    public int Line { get; }
}
