using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Rendering;
using LingFanEngine.SDK.Dsl.Highlight;

namespace LingFanEngine.SDK.Editor;

/// <summary>
/// DSL 语法高亮转换器——将 Highlighter 的 HighlightToken 映射到 AvaloniaEdit 颜色。
/// <para>实现 IVisualLineTransformer，在 AvaloniaEdit 渲染每行时着色。</para>
/// <para>使用防抖机制：源码变化后延迟 150ms 再重新分词，避免每次按键都全量解析。</para>
/// </summary>
public class DslHighlightingTransformer : IVisualLineTransformer
{
    private string _source = "";
    private List<HighlightToken> _tokens = [];
    private bool _dirty = true;
    private long _lastSetTime;  // Stopwatch.GetTimestamp() of last SetSource call
    private const int DebounceMs = 150;

    // ===== 配色方案（暗背景 #1E1E1E 高对比、色相分散、互不撞色）=====
    // 设计原则：每个色相代表一类语义；只把"语义紧耦合"的类别归为同色
    //（如 SceneName 与 Navigation 同青——场景就是导航目标；Label 与 ControlFlow 同紫——标签是跳转目标）。
    // 重点保证用户最关心的四类互不相同：变量(绿) / 字符串(暖金) / UI展示(红) / UI交互(橙) / UI容器(青绿)。

    // —— 结构 / 中性 ——
    private static readonly IBrush s_commentBrush = new SolidColorBrush(Color.Parse("#6A737D"));     // 注释：灰蓝
    private static readonly IBrush s_plainBrush = new SolidColorBrush(Color.Parse("#A6ACCD"));       // 默认前景
    private static readonly IBrush s_symbolBrush = new SolidColorBrush(Color.Parse("#A6ACCD"));       // 符号 (){}:,
    private static readonly IBrush s_operatorBrush = new SolidColorBrush(Color.Parse("#89DDFF"));     // 运算符 = + > ：青（结构连接，与导航同族）

    // —— 语句关键字（6 个语义家族，各自独立色相，互不撞色）——
    private static readonly IBrush s_keywordBrush = new SolidColorBrush(Color.Parse("#82AAFF"));      // 蓝：主语句/显示系统 say/show/hide/transition/save/debug/character/style
    private static readonly IBrush s_controlFlowBrush = new SolidColorBrush(Color.Parse("#C792EA"));  // 紫：控制流 if/while/for/jump/call/return/menu/label/input/wait/skip/auto/pause
    private static readonly IBrush s_navigationBrush = new SolidColorBrush(Color.Parse("#89DDFF"));   // 青：导航 scene/navigate/call_screen/back/forward
    private static readonly IBrush s_dataOpBrush = new SolidColorBrush(Color.Parse("#FFCB6B"));       // 琥珀：数据操作 set/define/let/local/array/dict
    private static readonly IBrush s_uiElementBrush = new SolidColorBrush(Color.Parse("#F07178"));    // 红：UI 展示元素 text/image/background/portrait/sprite/live2d/video/narrator/speaker/dialog
    private static readonly IBrush s_uiContainerBrush = new SolidColorBrush(Color.Parse("#73D0A8"));  // 青绿：UI 容器 panel/vbox/hbox/container/scrollview（与展示型红区分）
    private static readonly IBrush s_uiInteractiveBrush = new SolidColorBrush(Color.Parse("#FFB454")); // 橙：UI 交互 button/input/checkbox/slider（与展示型红区分）
    private static readonly IBrush s_mediaBrush = new SolidColorBrush(Color.Parse("#FF8A65"));        // 橙红：媒体 bgm/se/video/cutscene/ambient

    // —— 命名引用（tier2：与语义家族配对，但彼此区分）——
    private static readonly IBrush s_variableBrush = new SolidColorBrush(Color.Parse("#9CCC65"));     // 绿：{变量} 引用（重点区分对象，与字符串暖金明显不同）
    private static readonly IBrush s_characterNameBrush = new SolidColorBrush(Color.Parse("#FF9CAC")); // 粉红：角色名
    private static readonly IBrush s_sceneNameBrush = new SolidColorBrush(Color.Parse("#89DDFF"));    // 青：场景名（=导航，场景是导航目标）
    private static readonly IBrush s_labelBrush = new SolidColorBrush(Color.Parse("#C792EA"));        // 紫：标签名（=控制流，标签是跳转目标）
    private static readonly IBrush s_styleNameBrush = new SolidColorBrush(Color.Parse("#FFCB6B"));     // 琥珀：样式名（=数据操作，样式是声明式数据）
    private static readonly IBrush s_functionBrush = new SolidColorBrush(Color.Parse("#82AAFF"));      // 蓝：函数名（=主语句，函数是可调用单元）
    private static readonly IBrush s_inlineTagBrush = new SolidColorBrush(Color.Parse("#C792EA"));    // 紫：内联标记 {b}{/b}{color=}（=控制流，结构标记）

    // —— 值 / 字面量 ——
    private static readonly IBrush s_stringBrush = new SolidColorBrush(Color.Parse("#E6C07B"));       // 暖金：字符串字面量（与变量绿明显区分）
    private static readonly IBrush s_propertyNameBrush = new SolidColorBrush(Color.Parse("#B2CCFF")); // 浅蓝：属性名 key=
    private static readonly IBrush s_propertyValueBrush = new SolidColorBrush(Color.Parse("#FFCB6B")); // 琥珀：属性值（枚举值 fade/EaseOutQuad）
    private static readonly IBrush s_colorValueBrush = new SolidColorBrush(Color.Parse("#FFCB6B"));    // 琥珀：颜色值 #RRGGBB
    private static readonly IBrush s_pathValueBrush = new SolidColorBrush(Color.Parse("#E6C07B"));     // 暖金：路径值（含扩展名，=字符串）
    private static readonly IBrush s_numberBrush = new SolidColorBrush(Color.Parse("#B5CEA8"));       // 浅灰绿：数字（与变量纯绿区分）

    // —— 诊断 ——
    private static readonly IBrush s_infoBrush = new SolidColorBrush(Color.Parse("#82AAFF"));         // 蓝：信息级下划线

    /// <summary>设置源码并标记需要重新分词</summary>
    public void SetSource(string source)
    {
        _source = source;
        _lastSetTime = Stopwatch.GetTimestamp();
        _dirty = true;
    }

    /// <summary>标记需要重新计算（下次渲染时刷新）</summary>
    public void Invalidate()
    {
        _dirty = true;
    }

    private void EnsureTokens()
    {
        if (!_dirty) return;
        // 防抖：编辑过程中高频重绘时，距上次 SetSource 不足 150ms 先用旧 token 渲染。
        // 但首次（_tokens 尚为空）必须立即分词——否则打开文件后不滚动/不编辑就永远不刷新，
        // 整篇文本都会是默认前景色，表现为"配色都是一个色"（之前的根因）。
        var elapsed = Stopwatch.GetElapsedTime(_lastSetTime, Stopwatch.GetTimestamp());
        if (_tokens.Count > 0 && elapsed.TotalMilliseconds < DebounceMs) return;

        _tokens = string.IsNullOrEmpty(_source)
            ? []
            : Highlighter.GetHighlights(_source);
        _dirty = false;
    }

    public void Transform(ITextRunConstructionContext context, IList<VisualLineElement> elements)
    {
        EnsureTokens();
        if (_tokens.Count == 0) return;

        var visualLine = context.VisualLine;
        var lineStartOffset = visualLine.FirstDocumentLine.Offset;
        var lineEndOffset = visualLine.LastDocumentLine.Offset + visualLine.LastDocumentLine.Length;

        // 为当前行构建 offset → color 的段映射
        var segments = new List<(int Start, int End, IBrush Brush)>();

        foreach (var token in _tokens)
        {
            var tokenStart = token.Start;
            var tokenEnd = token.Start + token.Length;

            // 跳过不在当前行的 token
            if (tokenEnd <= lineStartOffset || tokenStart >= lineEndOffset)
                continue;

            // 裁剪到当前行范围内
            var clampedStart = System.Math.Max(tokenStart, lineStartOffset) - lineStartOffset;
            var clampedEnd = System.Math.Min(tokenEnd, lineEndOffset) - lineStartOffset;

            var brush = GetBrush(token.Category);
            if (brush == null) continue;

            segments.Add((clampedStart, clampedEnd, brush));
        }

        if (segments.Count == 0) return;

        // 对每个元素应用颜色
        foreach (var element in elements)
        {
            if (element is not VisualLineText textElement) continue;

            var elementStart = textElement.RelativeTextOffset;
            var elementEnd = elementStart + textElement.DocumentLength;

            // 查找此元素范围内的颜色——优先使用第一个匹配的段
            IBrush? brush = null;
            foreach (var (segStart, segEnd, segBrush) in segments)
            {
                if (segEnd > elementStart && segStart < elementEnd)
                {
                    brush = segBrush;
                    break;
                }
            }

            if (brush != null)
            {
                textElement.TextRunProperties.SetForegroundBrush(brush);
            }
        }
    }

    private static IBrush? GetBrush(HighlightCategory category)
    {
        return category switch
        {
            HighlightCategory.Keyword => s_keywordBrush,
            HighlightCategory.String => s_stringBrush,
            HighlightCategory.Comment => s_commentBrush,
            HighlightCategory.Variable => s_variableBrush,
            HighlightCategory.Number => s_numberBrush,
            HighlightCategory.Symbol => s_symbolBrush,
            HighlightCategory.Label => s_labelBrush,
            HighlightCategory.StyleName => s_styleNameBrush,
            HighlightCategory.CharacterName => s_characterNameBrush,
            HighlightCategory.PropertyName => s_propertyNameBrush,
            HighlightCategory.PropertyValue => s_propertyValueBrush,
            HighlightCategory.ColorValue => s_colorValueBrush,
            HighlightCategory.PathValue => s_pathValueBrush,
            HighlightCategory.SceneName => s_sceneNameBrush,
            HighlightCategory.InlineTag => s_inlineTagBrush,
            HighlightCategory.Function => s_functionBrush,
            HighlightCategory.Operator => s_operatorBrush,
            HighlightCategory.Info => s_infoBrush,
            HighlightCategory.ControlFlow => s_controlFlowBrush,
            HighlightCategory.Navigation => s_navigationBrush,
            HighlightCategory.DataOp => s_dataOpBrush,
            HighlightCategory.Uielement => s_uiElementBrush,
            HighlightCategory.UiContainer => s_uiContainerBrush,
            HighlightCategory.UiInteractive => s_uiInteractiveBrush,
            HighlightCategory.Media => s_mediaBrush,
            _ => s_plainBrush,
        };
    }
}
