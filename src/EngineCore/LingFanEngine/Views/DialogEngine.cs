using Avalonia.Controls.Documents;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Views;

/// <summary>
/// 对话框核心逻辑引擎——打字机/内联标记/NVL累积/标签处理
/// <para>不依赖任何控件结构，供 DialogBox 和 UI 层模板共享。</para>
/// <para>Phase 65：从 DialogBox 提取，消除核心逻辑重复。</para>
/// </summary>
public class DialogEngine
{
    private readonly IStateContainer _state;

    // ── 打字机状态 ──
    private string _fullText = "";
    private int _charIndex;
    private double _typeTimer;

    // ── NVL 状态 ──
    private int _nvlSkipLength;
    private string _nvlPrevText = "";

    /// <summary>默认打字机速度（字符/秒）</summary>
    public double TypeSpeed { get; set; } = 60;

    /// <summary>打字机是否已完成</summary>
    public bool IsComplete => _charIndex >= _fullText.Length;

    /// <summary>是否被 {w}/{p} 标签暂停</summary>
    public bool IsPausedByTag { get; private set; }

    /// <summary>当前完整文本</summary>
    public string FullText => _fullText;

    /// <summary>当前打字机位置</summary>
    public int CharIndex => _charIndex;

    /// <summary>NVL 模式下已显示的文本长度（打字机跳过此部分）</summary>
    public int NvlSkipLength => _nvlSkipLength;

    /// <summary>NVL 模式下上一帧的文本</summary>
    public string NvlPrevText => _nvlPrevText;

    public DialogEngine(IStateContainer state)
    {
        _state = state;
    }

    /// <summary>
    /// 设置文本（ADV 模式），重置打字机
    /// </summary>
    public void SetText(string text)
    {
        _fullText = text;
        _charIndex = 0;
        _typeTimer = 0;
        IsPausedByTag = false;
        _nvlSkipLength = 0;
        _nvlPrevText = "";
        _state.Set(StateKeys.Dialog.TypewriterDone, false);
    }

    /// <summary>
    /// 设置 NVL 文本，处理累积/追加检测
    /// <para>返回需要立即显示的旧文本长度（0=从零开始）</para>
    /// </summary>
    public int SetNvlText(string text)
    {
        _fullText = text;
        _charIndex = 0;
        _typeTimer = 0;
        IsPausedByTag = false;
        _state.Set(StateKeys.Dialog.TypewriterDone, false);

        if (!string.IsNullOrEmpty(_nvlPrevText) && text.StartsWith(_nvlPrevText) && text.Length > _nvlPrevText.Length)
        {
            // 文本被追加——跳过已有部分，只打字机新内容
            _nvlSkipLength = _nvlPrevText.Length;
            _charIndex = _nvlSkipLength;
            var speed = _state.Get<double?>(StateKeys.Dialog.TypewriterSpeed) ?? TypeSpeed;
            _typeTimer = _nvlSkipLength / Math.Max(speed, 1.0);
        }
        else
        {
            // 文本完全变化（nvl clear 后新开始）——从零开始打字机
            _nvlSkipLength = 0;
            _charIndex = 0;
        }
        _nvlPrevText = text;
        return _nvlSkipLength;
    }

    /// <summary>
    /// 推进打字机一帧
    /// <para>返回需要显示的 raw 文本（已处理 {w}/{p}/{fast} 和末尾未闭合标签），null=无变化</para>
    /// </summary>
    public string? Advance(double deltaSeconds)
    {
        if (IsComplete) return null;

        // 检查打字机开关
        var twEnabled = _state.ContainsKey(StateKeys.Dialog.TypewriterEnabled)
            ? _state.Get<bool>(StateKeys.Dialog.TypewriterEnabled) : true;
        if (!twEnabled) return SkipToEnd();

        // instant=true 时跳过打字机效果
        if (_state.ContainsKey(StateKeys.Dialog.Instant) && _state.Get<bool>(StateKeys.Dialog.Instant))
            return SkipToEnd();

        if (IsPausedByTag) return null;

        // NVL 模式：跳过已显示的旧文本
        if (_nvlSkipLength > 0 && _charIndex < _nvlSkipLength)
            _charIndex = _nvlSkipLength;

        // {fast} 标签——跳到末尾
        var fastIdx = _fullText.IndexOf("{fast}", StringComparison.Ordinal);
        if (fastIdx >= 0) return SkipToEnd();

        _typeTimer += deltaSeconds;
        var speed = _state.Get<double?>(StateKeys.Dialog.TypewriterSpeed) ?? TypeSpeed;
        int charsToShow = (int)(_typeTimer * speed);
        if (charsToShow > _charIndex)
        {
            _charIndex = Math.Min(charsToShow, _fullText.Length);
            // 中文断词（不在标点中间断开）
            var start = _charIndex;
            while (_charIndex < _fullText.Length && _fullText[_charIndex] != ' ' &&
                   _fullText[_charIndex] != '，' && _fullText[_charIndex] != '。' &&
                   _fullText[_charIndex] != '！' && _fullText[_charIndex] != '？' &&
                   _charIndex - start < 5) _charIndex++;
        }

        var raw = _fullText[..Math.Min(_charIndex, _fullText.Length)];

        // {w} 标签——暂停
        var wIdx = raw.IndexOf("{w}");
        if (wIdx >= 0)
        {
            _charIndex = wIdx;
            IsPausedByTag = true;
            _fullText = _fullText.Remove(wIdx, 3);
            raw = _fullText[..Math.Min(_charIndex, _fullText.Length)];
        }

        // {p} 标签——暂停
        var pIdx = raw.IndexOf("{p}");
        if (pIdx >= 0)
        {
            _charIndex = pIdx;
            IsPausedByTag = true;
            _fullText = _fullText.Remove(pIdx, 3);
            raw = _fullText[..Math.Min(_charIndex, _fullText.Length)];
        }

        // 剥离末尾未闭合的 {tag
        var cleaned = StripTrailingUnclosedTag(raw);

        // 打字机完成时通知状态容器
        if (IsComplete)
            _state.Set(StateKeys.Dialog.TypewriterDone, true);

        return cleaned;
    }

    /// <summary>跳到打字机末尾，返回完整文本</summary>
    public string SkipToEnd()
    {
        _charIndex = _fullText.Length;
        IsPausedByTag = false;
        _state.Set(StateKeys.Dialog.TypewriterDone, true);
        return _fullText;
    }

    /// <summary>重置 NVL 模式内部状态（场景切换或退出 NVL 模式时调用）</summary>
    public void ResetNvlState()
    {
        _nvlSkipLength = 0;
        _nvlPrevText = "";
    }

    /// <summary>解除 {w}/{p} 暂停，继续打字机（不跳到末尾）</summary>
    public void ResumeFromPause() => IsPausedByTag = false;

    // ========== 静态工具：内联标记 ==========

    /// <summary>去掉末尾未闭合的 {xxx 片段（不包含 }），防止渲染时字符泄露</summary>
    public static string StripTrailingUnclosedTag(string raw)
    {
        int lastOpen = raw.LastIndexOf('{');
        if (lastOpen < 0) return raw;
        int closeAfter = raw.IndexOf('}', lastOpen);
        if (closeAfter >= 0) return raw; // 闭合标签完整，不需要裁剪
        return raw[..lastOpen]; // 截断到最后一个 { 之前
    }

    /// <summary>
    /// 解析内联标记并填充到 InlineCollection（供模板的 TextBlock 调用）
    /// <para>支持 {b}{i}{u}{color=#xxx}{font=xxx}{size=N} 嵌套</para>
    /// </summary>
    public static void ApplyInlineMarkup(InlineCollection inlines, string raw)
    {
        inlines.Clear();
        var text = raw.Replace("{w}", "").Replace("{fast}", "").Replace("{p}", "");
        ParseInline(inlines, text, 0, text.Length, null);
    }

    /// <summary>
    /// 递归解析内联标记，支持嵌套标签如 {b}{color=#FFD700}秘密{/color}{/b}
    /// </summary>
    private static int ParseInline(InlineCollection inlines, string text, int start, int end, InlineStyle? parentStyle)
    {
        int pos = start;
        while (pos < end)
        {
            if (text[pos] == '{')
            {
                int close = text.IndexOf('}', pos);
                if (close < 0 || close >= end) { AppendRun(inlines, text[pos..end], parentStyle); return end; }
                var tag = text[(pos + 1)..close];
                var tagName = tag.Contains('=') ? tag[..tag.IndexOf('=')] : tag;
                var attr = tag.Contains('=') ? tag[(tag.IndexOf('=') + 1)..] : null;

                // 闭合标签 → 返回（让调用者处理）
                if (tagName.StartsWith('/'))
                    return close + 1;

                // 开放标签 → 递归解析内部内容，合并样式
                var childStyle = MergeStyle(parentStyle, tagName, attr);
                int afterTag = close + 1;
                int nextClose = ParseInline(inlines, text, afterTag, end, childStyle);
                pos = nextClose;
            }
            else
            {
                // 普通文本——找到下一个 { 或 end
                int nextBrace = text.IndexOf('{', pos);
                if (nextBrace < 0 || nextBrace >= end) nextBrace = end;
                AppendRun(inlines, text[pos..nextBrace], parentStyle);
                pos = nextBrace;
            }
        }
        return pos;
    }

    /// <summary>合并内联样式（子标签叠加父标签的样式）</summary>
    private static InlineStyle? MergeStyle(InlineStyle? parent, string tagName, string? attr)
    {
        var s = parent ?? new InlineStyle();
        return tagName switch
        {
            "b" => s with { Bold = true },
            "i" => s with { Italic = true },
            "u" => s with { Underline = true },
            "color" => attr != null ? s with { Color = attr } : s,
            "font" => attr != null ? s with { Font = attr } : s,
            "size" => double.TryParse(attr, out var fs) ? s with { Size = fs } : s,
            _ => s
        };
    }

    private static void AppendRun(InlineCollection inlines, string text, InlineStyle? style)
    {
        if (string.IsNullOrEmpty(text)) return;
        var run = new Run(text);
        if (style != null)
        {
            if (style.Bold) run.FontWeight = FontWeight.Bold;
            if (style.Italic) run.FontStyle = FontStyle.Italic;
            if (style.Underline) run.TextDecorations = TextDecorations.Underline;
            if (style.Color != null) run.Foreground = new SolidColorBrush(Color.Parse(style.Color));
            if (style.Font != null) run.FontFamily = new FontFamily(style.Font);
            if (style.Size.HasValue) run.FontSize = style.Size.Value;
        }
        inlines.Add(run);
    }

    /// <summary>内联样式数据（支持嵌套叠加）</summary>
    private record InlineStyle(
        bool Bold = false, bool Italic = false, bool Underline = false,
        string? Color = null, string? Font = null, double? Size = null);
}
