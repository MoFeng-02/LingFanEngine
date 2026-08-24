using LingFanEngine.DslCore;

namespace LingFanEngine.Dsl.LanguageService;

/// <summary>
/// 语义分类器——把词法 <see cref="DslToken"/> 派生为 <see cref="SemanticCategory"/>。
/// <para>全部依据 DslCore 词汇数据（DslKeywords 子集 / DslUiElementCategories），不引入任何硬编码。</para>
/// </summary>
public static class DslSemanticClassifier
{
    /// <summary>对单个 token 做语义分类（需要源文本以取得关键字文本）。</summary>
    public static SemanticCategory Classify(in DslToken token, ReadOnlySpan<char> source)
    {
        switch (token.Kind)
        {
            case DslTokenKind.Comment:
                return SemanticCategory.Comment;
            case DslTokenKind.String:
                return SemanticCategory.String;
            case DslTokenKind.Number:
                return SemanticCategory.Number;
            case DslTokenKind.Symbol:
                return SemanticCategory.Symbol;
            case DslTokenKind.Identifier:
            {
                var text = token.GetText(source).ToString();
                if (DslKeywordDocs.IsBuiltinFunction(text)) return SemanticCategory.Function;
                return SemanticCategory.Identifier;
            }
            case DslTokenKind.Keyword:
            {
                var text = token.GetText(source).ToString();
                if (DslKeywords.ControlFlow.Contains(text)) return SemanticCategory.ControlFlow;
                if (DslKeywords.Navigation.Contains(text)) return SemanticCategory.Navigation;
                if (DslKeywords.DataOp.Contains(text)) return SemanticCategory.DataOp;
                if (DslKeywords.Media.Contains(text)) return SemanticCategory.Media;
                if (DslKeywords.Display.Contains(text)) return SemanticCategory.Display;
                if (DslKeywords.SaveLoad.Contains(text)) return SemanticCategory.SaveLoad;
                if (DslKeywords.Chapter.Contains(text)) return SemanticCategory.Chapter;
                if (DslKeywords.Rollback.Contains(text)) return SemanticCategory.Rollback;
                if (DslKeywords.Playback.Contains(text)) return SemanticCategory.Playback;
                if (DslKeywords.TimeEvent.Contains(text)) return SemanticCategory.TimeEvent;
                if (DslKeywords.Notify.Contains(text)) return SemanticCategory.Notify;
                if (DslKeywords.UiEnhance.Contains(text)) return SemanticCategory.UiEnhance;

                var ui = DslUiElementCategories.Classify(text);
                if (ui == DslUiElementCategory.Container) return SemanticCategory.UiContainer;
                if (ui == DslUiElementCategory.Interactive) return SemanticCategory.UiInteractive;
                if (ui == DslUiElementCategory.Display) return SemanticCategory.UiDisplay;

                if (DslKeywords.Parameters.Contains(text)) return SemanticCategory.Parameter;
                if (DslKeywords.ElementAttributes.Contains(text)) return SemanticCategory.ElementAttribute;
                if (DslKeywords.Literals.Contains(text)) return SemanticCategory.Literal;
                return SemanticCategory.Keyword;
            }
            default:
                return SemanticCategory.Unknown;
        }
    }
}
