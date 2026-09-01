using Avalonia.Controls.Documents;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Views;

/// <summary>
/// 对话框核心逻辑引擎——打字机/内联标记/NVL累积/标签处理
/// <para>不依赖任何控件结构，供 DialogBox 和 UI 层模板共享。</para>
/// <para>Phase 65：从 DialogBox 提取，消除核心逻辑重复。</para>
/// <para>2026-09 重写：</para>
/// <para>· 标签在 SetText 时一次预解析：{w}/{p}/{w=N} → 暂停点数组（可见字符坐标），
/// {fast} → 快进点，{b}/{color=...} → 分段样式表（<see cref="Segments"/>）。
/// 推进期 O(1) 消费，不再每帧扫描、不改写原文。</para>
/// <para>· 打字机在「纯可见文本」上推进（所有标签不占索引）——
/// speed 语义精确等于 可见字符/秒（旧版实际约快 1.7 倍且突发式显字）；
/// 渲染层用 Segments 增量构建 Run，无需每帧重新解析 markup。</para>
/// <para>· 代理对（emoji/扩展汉字）安全切割；标点停顿（句读节奏感）；
/// {w=N} 定时暂停自动恢复；speed=0 / typewriter=false / instant=true 立即显示。</para>
/// <para>· NVL：追加检测按可见坐标换算；溢出裁剪经 SkipHint 传递（只打字新行）；
/// 回溯重放（rollback）由上层检测后直接 SkipToEnd。</para>
/// <para>· 点击消费标志（TryConsumeClick）收敛于此——DialogBox 点击与
/// SceneView 键盘/背景点击共用同一防重入闸门。</para>
/// </summary>
public class DialogEngine
{
    private readonly IStateContainer _state;

    // ── 文本与打字机状态 ──
    /// <summary>原始全文（含所有标签原文——FullText 语义保持「所设文本」不变）</summary>
    private string _fullText = "";
    /// <summary>纯可见文本（所有标签已剥离）——打字机索引即此文本坐标</summary>
    private string _cleanText = "";
    /// <summary>打字机在 _cleanText 上的位置</summary>
    private int _charIndex;
    private double _typeTimer;
    /// <summary>打字机计时基准（SetText 时的 _charIndex）——target 相对此基准计算。
    /// <para>修复（2026-09，NVL 点击推进延迟）：target 原为绝对坐标（_typeTimer*speed 从 0 起算），
    /// NVL 追加场景 _charIndex 从 cleanSkip 起步（如累积 60 字），打字机需空等
    /// cleanSkip/speed 秒（60字@60cps = 1 秒）才恢复——用户感知为点击后新句延迟出现。</para></summary>
    private int _typeBaseIndex;

    // ── 标签预扫描（SetText 时一次解析） ──
    private List<PausePoint> _pausePoints = [];
    private int _pausePointIdx;
    /// <summary>快进点（-1=无）：发现 {fast} 即从当前显示点起立即显示全部剩余</summary>
    private int _fastPos = -1;

    /// <summary>暂停点：打字机显示到此位置停住；秒数非 null 则到时自动恢复</summary>
    private readonly record struct PausePoint(int CleanPos, double? AutoSeconds);

    // ── NVL 状态 ──
    private int _nvlSkipLength;
    private string _nvlPrevText = "";

    // ── 停顿计时 ──
    private double _punctPauseRemaining;
    private double _autoPauseTimer;
    private double _activeAutoSeconds;

    // ── 点击消费（防陈旧点击重复触发 Complete） ──
    private bool _clickConsumed;

    /// <summary>默认打字机速度（可见字符/秒）</summary>
    public double TypeSpeed { get; set; } = 60;

    /// <summary>打字机是否已完成</summary>
    public bool IsComplete => _charIndex >= _cleanText.Length;

    /// <summary>是否被 {w}/{p} 标签暂停</summary>
    public bool IsPausedByTag { get; private set; }

    /// <summary>当前完整文本（原文，标签保留——与 SetText 所设一致）</summary>
    public string FullText => _fullText;

    /// <summary>当前打字机位置（可见文本索引）</summary>
    public int CharIndex => _charIndex;

    /// <summary>NVL 模式下已显示的文本长度（可见文本坐标）</summary>
    public int NvlSkipLength => _nvlSkipLength;

    /// <summary>NVL 模式下上一帧的文本</summary>
    public string NvlPrevText => _nvlPrevText;

    /// <summary>
    /// 预解析的渲染分段（SetText 时生成）：可见文本按样式切段的扁平列表。
    /// <para>渲染层据此增量构建 Run——打字机推进只增长最后一段的文本，无需重新解析。</para>
    /// </summary>
    public IReadOnlyList<Segment> Segments { get; private set; } = [];

    /// <summary>渲染分段：可见文本 + 该段的合并样式（嵌套标签样式已叠加）</summary>
    public readonly record struct Segment(string Text, SegmentStyle Style);

    /// <summary>分段样式（内联标签合并结果）</summary>
    public sealed record SegmentStyle(
        bool Bold = false, bool Italic = false, bool Underline = false,
        string? Color = null, string? Font = null, double? Size = null)
    {
        /// <summary>空样式（无任何内联修饰）</summary>
        public static SegmentStyle Empty { get; } = new();
    }

    public DialogEngine(IStateContainer state)
    {
        _state = state;
    }

    // ========== 文本设置 ==========

    /// <summary>
    /// 设置文本（ADV 模式），重置打字机（从零开始）并预解析标签
    /// </summary>
    public void SetText(string text) => SetTextCore(text, 0);

    /// <summary>
    /// 设置 NVL 文本，处理累积/追加检测
    /// <para>返回需要立即显示的旧文本长度（可见坐标，0=从零开始）</para>
    /// <param name="rawSkipHint">溢出裁剪提示：已显示部分的原文长度（-1=无提示，
    /// 走前缀追加检测）。DialogHandlers 滚动裁剪旧行后经状态键传入。</param>
    /// </summary>
    public int SetNvlText(string text, int rawSkipHint = -1)
    {
        int rawSkip;
        if (rawSkipHint >= 0 && rawSkipHint <= text.Length)
        {
            // 溢出裁剪：保留的旧行已是已显示内容
            rawSkip = rawSkipHint;
        }
        else if (!string.IsNullOrEmpty(_nvlPrevText) && text.StartsWith(_nvlPrevText) && text.Length > _nvlPrevText.Length)
        {
            // 文本被追加——跳过已有部分，只打字机新内容
            rawSkip = _nvlPrevText.Length;
        }
        else
        {
            // 文本完全变化（nvl clear 后新开始 / 回溯回退）——从零开始
            rawSkip = 0;
        }
        var cleanSkip = ComputeVisibleLength(text, rawSkip);
        SetTextCore(text, cleanSkip);
        _nvlPrevText = text;
        _nvlSkipLength = cleanSkip;
        return cleanSkip;
    }

    private void SetTextCore(string text, int cleanSkip)
    {
        _fullText = text;
        _typeTimer = 0;
        IsPausedByTag = false;
        _punctPauseRemaining = 0;
        _autoPauseTimer = 0;
        _clickConsumed = false;
        _charIndex = Math.Min(cleanSkip, int.MaxValue);
        ParseTags();
        _charIndex = Math.Min(_charIndex, _cleanText.Length);
        // 计时基准 = 起始位置（NVL 追加/溢出裁剪跳过 >0）——Advance 的 target 相对此基准，
        // 打字机立即恢复（而非空等 timer 追平已跳过的字符数）
        _typeBaseIndex = _charIndex;
        _nvlSkipLength = Math.Min(cleanSkip, _cleanText.Length);
        // NVL 追加段的暂停点已过——消费掉
        while (_pausePointIdx < _pausePoints.Count && _pausePoints[_pausePointIdx].CleanPos <= _charIndex)
            _pausePointIdx++;
        _state.Set(StateKeys.Dialog.TypewriterDone, false);
    }

    /// <summary>
    /// 一次解析全文标签：
    /// <para>· 剥离全部标签生成纯可见文本 _cleanText；</para>
    /// <para>· {w}/{p}/{w=N} → 暂停点（可见坐标）；{fast} → 快进点；</para>
    /// <para>· {b}/{i}/{u}/{color}/{font}/{size} → 分段样式表 Segments（嵌套合并）。</para>
    /// </summary>
    private void ParseTags()
    {
        _pausePoints = [];
        _pausePointIdx = 0;
        _fastPos = -1;

        var text = _fullText;
        var sb = new System.Text.StringBuilder(text.Length);
        var segs = new List<Segment>();
        var pauses = new List<PausePoint>();
        var style = SegmentStyle.Empty;

        int pos = 0;
        while (pos < text.Length)
        {
            if (text[pos] != '{')
            {
                // 普通可见文本——到下一个 { 或末尾
                int nextBrace = text.IndexOf('{', pos);
                if (nextBrace < 0) nextBrace = text.Length;
                var chunk = text[pos..nextBrace];
                segs.Add(new Segment(chunk, style));
                sb.Append(chunk);
                pos = nextBrace;
                continue;
            }
            var close = text.IndexOf('}', pos);
            if (close < 0)
            {
                // 未闭合 {——按可见文本保留（罕见；不参与样式）
                var chunk = text[pos..];
                segs.Add(new Segment(chunk, style));
                sb.Append(chunk);
                break;
            }
            var tag = text.AsSpan(pos + 1, close - pos - 1);
            if (tag.SequenceEqual("w"))
            {
                pauses.Add(new PausePoint(sb.Length, null));
            }
            else if (tag.StartsWith("w=") && double.TryParse(tag[2..], out var sec))
            {
                pauses.Add(new PausePoint(sb.Length, sec));
            }
            else if (tag.SequenceEqual("p"))
            {
                pauses.Add(new PausePoint(sb.Length, null));
            }
            else if (tag.SequenceEqual("fast"))
            {
                _fastPos = sb.Length;
            }
            else if (tag.StartsWith('/'))
            {
                // 闭合标签——弹出该样式维度（线性扫描近似：仅恢复到父样式）
                style = CloseStyle(style, tag.ToString());
            }
            else
            {
                // 开放样式标签——压入样式（嵌套合并）
                style = OpenStyle(style, tag.ToString());
            }
            pos = close + 1;
        }

        _cleanText = sb.ToString();
        _pausePoints = pauses;
        Segments = segs;
    }

    /// <summary>开放标签 → 合并样式（与静态 MergeStyle 同规则）</summary>
    private static SegmentStyle OpenStyle(SegmentStyle s, string tag)
    {
        var eq = tag.IndexOf('=');
        var name = eq < 0 ? tag : tag[..eq];
        var attr = eq < 0 ? null : tag[(eq + 1)..];
        return name switch
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

    /// <summary>闭合标签 → 恢复样式维度（近似：嵌套同名标签按一层处理，对常见单层/两层用法正确）</summary>
    private static SegmentStyle CloseStyle(SegmentStyle s, string tag)
    {
        var name = tag.Length > 1 ? tag[1..] : "";
        return name switch
        {
            "b" => s with { Bold = false },
            "i" => s with { Italic = false },
            "u" => s with { Underline = false },
            "color" => s with { Color = null },
            "font" => s with { Font = null },
            "size" => s with { Size = null },
            _ => s
        };
    }

    /// <summary>计算原文前 rawLimit 个字符对应的可见字符数（所有标签不计宽）</summary>
    private static int ComputeVisibleLength(string text, int rawLimit)
    {
        if (rawLimit <= 0 || text.Length == 0) return 0;
        var limit = Math.Min(rawLimit, text.Length);
        int pos = 0, visible = 0;
        while (pos < limit)
        {
            if (text[pos] == '{')
            {
                var close = text.IndexOf('}', pos);
                if (close < 0) { visible += limit - pos; break; } // 未闭合——按可见算
                pos = close + 1;
                continue;
            }
            pos++;
            visible++;
        }
        // rawLimit 之后的残余不影响；limit 内未闭合标签尾部不重复计
        if (pos > limit) { /* 标签跨过 limit——此段标签字符不计可见 */ }
        return visible;
    }

    // ========== 推进 ==========

    /// <summary>
    /// 推进打字机一帧
    /// <para>返回当前可见文本（纯文本，无标签），null=无变化。</para>
    /// <para>速度语义：speed = 可见字符/秒（精确）；speed&lt;=0 或关闭打字机 = 立即完整显示。</para>
    /// <para>节奏：标点后停顿 <see cref="PunctuationPauseSeconds"/>；{w=N} 到时自动恢复。</para>
    /// </summary>
    public string? Advance(double deltaSeconds)
    {
        if (IsComplete) return null;

        // 打字机全局开关 / instant → 立即完整显示（未设键=默认启用/非 instant）
        var twEnabled = _state.ContainsKey(StateKeys.Dialog.TypewriterEnabled)
            ? _state.Get<bool>(StateKeys.Dialog.TypewriterEnabled) : true;
        if (!twEnabled) return SkipToEnd();
        if (_state.ContainsKey(StateKeys.Dialog.Instant) && _state.Get<bool>(StateKeys.Dialog.Instant))
            return SkipToEnd();

        // {w=N} 定时暂停——到期自动恢复（{w}/{p} 需手动点击 Resume）
        if (IsPausedByTag)
        {
            if (_activeAutoSeconds > 0)
            {
                _autoPauseTimer += deltaSeconds;
                if (_autoPauseTimer < _activeAutoSeconds) return null;
                IsPausedByTag = false;
                _autoPauseTimer = 0;
            }
            else return null;
        }

        var speed = _state.Get<double?>(StateKeys.Dialog.TypewriterSpeed) ?? TypeSpeed;
        if (speed <= 0) return SkipToEnd(); // speed=0 = 立即显示

        // 标点停顿：扣减剩余时间，未到不前进
        if (_punctPauseRemaining > 0)
        {
            _punctPauseRemaining -= deltaSeconds;
            if (_punctPauseRemaining > 0) return null;
        }

        _typeTimer += deltaSeconds;
        var target = _typeBaseIndex + (int)(_typeTimer * speed);
        while (_charIndex < _cleanText.Length && _charIndex < target)
        {
            // 暂停点检查：抵达 → 停住等恢复
            if (_pausePointIdx < _pausePoints.Count && _pausePoints[_pausePointIdx].CleanPos <= _charIndex)
            {
                var pp = _pausePoints[_pausePointIdx];
                _pausePointIdx++;
                IsPausedByTag = true;
                _activeAutoSeconds = pp.AutoSeconds ?? 0;
                _autoPauseTimer = 0;
                break;
            }

            // 代理对安全：高代理必须连着低代理一起显示
            _charIndex++;
            if (char.IsHighSurrogate(_cleanText[_charIndex - 1]) && _charIndex < _cleanText.Length)
                _charIndex++;

            // 标点停顿：刚显示的字符是标点 → 置停顿（下一轮起消耗）
            if (IsPausePunctuation(_cleanText[_charIndex - 1]) && _charIndex < _cleanText.Length)
                _punctPauseRemaining = PunctuationPauseSeconds;
        }

        // {fast}：作者意图为「跳过打字动画」——发现快进标记即从当前显示点起立即显示全部剩余
        if (_fastPos >= 0 && _charIndex < _cleanText.Length)
            return SkipToEnd();

        if (IsComplete)
            _state.Set(StateKeys.Dialog.TypewriterDone, true);

        return VisibleText();
    }

    /// <summary>标点停顿时长（秒）——句读节奏；0 可禁用</summary>
    public double PunctuationPauseSeconds { get; set; } = 0.15;

    /// <summary>标点停顿字符集（中英句读；省略号/破折号亦停）</summary>
    private static bool IsPausePunctuation(char c) => c is
        '，' or '。' or '！' or '？' or '；' or '…' or '—' or ',' or '.' or '!' or '?' or ';';

    /// <summary>当前可见文本（纯文本前缀）</summary>
    public string VisibleText() => _cleanText[.._charIndex];

    /// <summary>跳到打字机末尾，返回完整可见文本</summary>
    public string SkipToEnd()
    {
        _charIndex = _cleanText.Length;
        _pausePointIdx = _pausePoints.Count;
        IsPausedByTag = false;
        _punctPauseRemaining = 0;
        _state.Set(StateKeys.Dialog.TypewriterDone, true);
        return _cleanText;
    }

    /// <summary>重置 NVL 模式内部状态（场景切换或退出 NVL 模式时调用）</summary>
    public void ResetNvlState()
    {
        _nvlSkipLength = 0;
        _nvlPrevText = "";
    }

    /// <summary>解除 {w}/{p} 暂停，继续打字机（不跳到末尾）</summary>
    public void ResumeFromPause()
    {
        IsPausedByTag = false;
        _autoPauseTimer = 0;
    }

    // ========== 点击消费（防陈旧点击重复触发 Complete） ==========

    /// <summary>
    /// 尝试消费一次「前进」点击/按键：SetText 后首次调用成功，其后（直至下一句）返回 false。
    /// <para>统一 DialogBox 点击与 SceneView 键盘/背景点击的防重入闸门
    /// （旧行为：两处各自维护 _clickConsumed，语义重复易漂移）。</para>
    /// </summary>
    public bool TryConsumeClick()
    {
        if (_clickConsumed) return false;
        _clickConsumed = true;
        return true;
    }

    // ========== 静态工具：内联标记（模板兼容 API） ==========

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
    /// 解析内联标记并填充到 InlineCollection（模板/兼容 API——含控制标签的 raw 文本）
    /// <para>支持 {b}{i}{u}{color=#xxx}{font=xxx}{size=N} 嵌套；{w}/{p}/{fast} 剥离</para>
    /// <para>DialogBox 主路径已改用 Segments 增量渲染，此静态 API 供自定义模板使用。</para>
    /// </summary>
    public static void ApplyInlineMarkup(InlineCollection inlines, string raw)
    {
        inlines.Clear();
        var text = raw.Replace("{w}", "").Replace("{fast}", "").Replace("{p}", "");
        // 简化剥离 {w=N}
        if (text.Contains("{w="))
        {
            var sb = new System.Text.StringBuilder(text.Length);
            int pos = 0;
            while (pos < text.Length)
            {
                if (text[pos] == '{' && text.AsSpan(pos).StartsWith("{w="))
                {
                    var close = text.IndexOf('}', pos);
                    if (close < 0) { sb.Append(text[pos..]); break; }
                    pos = close + 1;
                    continue;
                }
                sb.Append(text[pos]);
                pos++;
            }
            text = sb.ToString();
        }
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
        ApplyStyle(run, style != null
            ? new SegmentStyle(style.Bold, style.Italic, style.Underline, style.Color, style.Font, style.Size)
            : SegmentStyle.Empty);
        inlines.Add(run);
    }

    /// <summary>把分段样式应用到 Run（DialogBox 增量渲染共用）</summary>
    internal static void ApplyStyle(Run run, SegmentStyle style)
    {
        if (style.Bold) run.FontWeight = FontWeight.Bold;
        if (style.Italic) run.FontStyle = FontStyle.Italic;
        if (style.Underline) run.TextDecorations = TextDecorations.Underline;
        if (style.Color != null) run.Foreground = new SolidColorBrush(Color.Parse(style.Color));
        if (style.Font != null) run.FontFamily = new FontFamily(style.Font);
        if (style.Size.HasValue) run.FontSize = style.Size.Value;
    }

    /// <summary>内联样式数据（支持嵌套叠加）——静态 markup API 内部用</summary>
    private record InlineStyle(
        bool Bold = false, bool Italic = false, bool Underline = false,
        string? Color = null, string? Font = null, double? Size = null);

    /// <summary>SegmentStyle 空样式单例</summary>
    internal static readonly SegmentStyle EmptyStyle = SegmentStyle.Empty;
}
