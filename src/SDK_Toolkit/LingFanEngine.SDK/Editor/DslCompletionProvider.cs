using System;
using System.Collections.Generic;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Avalonia.Media;
using LingFanEngine.Dsl.LanguageService;

namespace LingFanEngine.SDK.Editor;

/// <summary>
/// DSL 代码补全数据源——委托 <see cref="IDslLanguageService.GetCompletion"/>（与 LSP 同一单一真相源 DslGrammar + ProjectIndex）。
/// <para>统一后，关键字/参数/枚举/过渡/缓动/对齐/资源路径/行内标记/C# 命令联动 全部由语言服务驱动，不再重复实现
/// （旧版 DslCompletionProvider 的硬编码字符串切片逻辑已退役，修复了 call→标签名、缺失过渡别名、资源/对齐无补全等问题）。</para>
/// </summary>
public sealed class DslCompletionProvider
{
    // 颜色（与 LSP 语义类别一致）
    private static readonly IBrush s_keywordColor = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush s_paramColor = new SolidColorBrush(Color.Parse("#9CDCFE"));
    private static readonly IBrush s_variableColor = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush s_sceneColor = new SolidColorBrush(Color.Parse("#DCDCAA"));
    private static readonly IBrush s_valueColor = new SolidColorBrush(Color.Parse("#CE9178"));

    private readonly IDslLanguageService _service;

    public DslCompletionProvider(IDslLanguageService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <summary>
    /// 根据光标位置获取补全列表（委托语言服务，单一真相源）。
    /// </summary>
    public IEnumerable<ICompletionData> GetCompletions(TextDocument document, int offset, string filePath)
    {
        // 保证语言服务持有当前文件最新源码（无脏区时为全量重建，单文件开销可忽略）。
        _service.UpdateDocument(filePath, document.Text);
        var items = _service.GetCompletion(filePath, offset);
        foreach (var it in items)
        {
            var insert = it.InsertText;
            // 变量引用保留旧编辑器 UX：补上闭合 }（{ 已由编辑器输入，} 不自动补）。
            // 行内标记（如 color=）不加 }，由用户自输。
            if (it.Kind == "variable") insert = it.InsertText + "}";
            yield return new DslCompletionData(it.DisplayText, insert, KindToBrush(it.Kind), it.Detail ?? "", it.ReplaceStart);
        }
    }

    private static IBrush KindToBrush(string kind) => kind switch
    {
        "statement" or "command" => s_keywordColor,
        "parameter" => s_paramColor,
        "variable" or "tag" => s_variableColor,
        "scene" or "label" or "character" or "func" or "style" => s_sceneColor,
        _ => s_valueColor, // enum / resource / 其它值
    };
}

/// <summary>DSL 补全数据项</summary>
public class DslCompletionData : ICompletionData
{
    public DslCompletionData(string text, string completionText, IBrush color, string description, int replaceStart = -1)
    {
        Text = text;
        CompletionText = completionText;
        Color = color;
        DescriptionText = description;
        ReplaceStart = replaceStart;
    }

    /// <summary>精确替换起点（绝对偏移）。-1 表示由编辑器按词边界探测。资源/命令等含分隔符候选用它覆盖默认词边界，避免前缀重复拼回。</summary>
    public int ReplaceStart { get; }

    public IBrush Color { get; }
    public string CompletionText { get; }
    public string DescriptionText { get; }

    // ICompletionData
    public object Content => Text;
    public object Description => DescriptionText;
    public Avalonia.Media.IImage? Image => null;
    public double Priority { get; set; }
    public string Text { get; }

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs e)
    {
        textArea.Document.Replace(completionSegment, CompletionText);
    }
}
