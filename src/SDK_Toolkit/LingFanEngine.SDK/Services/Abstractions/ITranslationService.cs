using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>翻译执行模式</summary>
public enum TranslationMode
{
    /// <summary>人工翻译——生成 value=原文占位，由人工填充</summary>
    Manual,

    /// <summary>AI 智能翻译——调用 LLM API（OpenAI 兼容接口）批量翻译</summary>
    Ai,

    /// <summary>专业翻译 API——调用 DeepL/Google 等翻译服务</summary>
    Api,

    /// <summary>AI Agent——让 LLM 经 tool calling 理解项目、决定翻译，写入走确定性路径 + 人在环审批</summary>
    Agent,
}

/// <summary>
/// 翻译文件输出布局（引擎语言根内部自由组织，见 docs-site/cookbook/如何做多语言.md）。
/// <para>Flat=扁平（可选按场景并列）；Mirrored=镜像 Stories 子文件夹（逐 story 一个 json）；SingleFile=单个大文件。</para>
/// </summary>
public enum TranslationLayout
{
    /// <summary>扁平（默认）：Lang/{lang}/main.json（全局/UI）+ 按场景 Lang/{lang}/{scene}.json（扁平并列）</summary>
    Flat = 0,

    /// <summary>子文件夹分类：Lang/{lang}/{sceneDir}/{scene}.json 镜像 Stories；main.json 兜底</summary>
    Mirrored = 1,

    /// <summary>单个大文件：Lang/{lang}.json</summary>
    SingleFile = 2,
}

/// <summary>扫描文本来源种类</summary>
public enum ScannedTextKind
{
    /// <summary>来自某个 .story 文件（可归到某场景/文件）</summary>
    Story,

    /// <summary>来自 C# 侧 / 无法归类到某 story 的 UI 文本（归入 main.json）</summary>
    Ui,
}

/// <summary>
/// 带来源的扫描条目——记录可翻译文本、种类、以及所属 .story 相对 Stories 根的路径（null = UI/全局/C#）。
/// <para>SourceStory 形如 <c>title/title_main.story</c>；用于 Mirrored 布局按 story 路由到
/// <c>Lang/{lang}/title/title_main.json</c>。</para>
/// </summary>
public sealed record ScannedText(string Text, ScannedTextKind Kind, string? SourceStory);

/// <summary>
/// 翻译器抽象——单条/批量文本翻译。
/// <para>实现：<see cref="ITranslator"/>（Manual=占位 / Ai=OpenAI 兼容 LLM / Anthropic=Claude / Api=DeepL 风格）。</para>
/// </summary>
public interface ITranslator
{
    /// <summary>翻译单条文本；失败或无法翻译返回 null</summary>
    Task<string?> TranslateAsync(string text, string targetLang, string sourceLang = "", CancellationToken ct = default);

    /// <summary>批量翻译（AI/API 单请求多条的 30× 成本摊薄）；按入参顺序返回，失败条为 null</summary>
    Task<IReadOnlyList<string?>> TranslateBatchAsync(IReadOnlyList<string> texts, string targetLang, string sourceLang = "", CancellationToken ct = default);
}

/// <summary>翻译 API 配置（DeepL 风格）</summary>
public sealed class ApiTranslatorConfig
{
    /// <summary>API 端点（如 DeepL https://api-free.deepl.com/v2/translate）</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>API Key</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>目标语言代码（DeepL 如 EN-US / JA）</summary>
    public string TargetLangCode { get; set; } = "EN-US";
}

/// <summary>
/// 翻译同步服务——扫描 .story + C# 全部可翻译文本，按所选布局（Flat/Mirrored/SingleFile）增量维护翻译文件，
/// 支持 Manual/AI/API 三种模式翻译。
/// <para>写入走 <see cref="IFileEditor"/>（原子写 + diff + 备份/回滚）：<see cref="PrepareSyncAsync"/> 只"准备"出编辑
/// （不落盘），经整轮 diff 审批后由 <see cref="ApplyEditsAsync"/> 提交。</para>
/// <para>与引擎 I18nService 约定一致：语言根目录 <c>Lang/{lang}/</c>（locale code），键=原文，值=译文。</para>
/// </summary>
public interface ITranslationService
{
    /// <summary>
    /// 扫描项目全部可翻译文本（.story DSL + C#），返回带来源条目以便按布局路由。
    /// </summary>
    Task<IReadOnlyList<ScannedText>> ScanTranslatableTextsAsync(string projectDir);

    /// <summary>
    /// 准备一次翻译同步（不落盘）：扫描 → 路由到目标文件 → 翻译新增/待翻 → 构建每文件 <see cref="FileEdit"/>。
    /// </summary>
    /// <returns>结果含 <see cref="TranslationSyncResult.PendingEdits"/> 与 <see cref="TranslationSyncResult.PreviewText"/>（整轮 diff 预览）</returns>
    Task<TranslationSyncResult> PrepareSyncAsync(
        string projectDir, string lang, TranslationLayout layout, ITranslator translator,
        IProgress<TranslationProgress>? progress, bool forceRetranslate, string sourceLang, CancellationToken ct);

    /// <summary>提交一批已准备、已审批的编辑（原子写 + 备份；部分失败时回滚已提交项）。返回成功提交次数。</summary>
    Task<int> ApplyEditsAsync(IReadOnlyList<FileEdit> edits, CancellationToken ct = default);

    /// <summary>回滚一批已提交的编辑（用 .bak 恢复 / 删除新建文件）。返回成功回滚次数。</summary>
    Task<int> RollbackEditsAsync(IReadOnlyList<FileEdit> edits, CancellationToken ct = default);

    /// <summary>列出项目中已有翻译键集合（未翻译=值等于原文）。</summary>
    Task<IReadOnlyDictionary<string, bool>> GetTranslationStatusAsync(string projectDir, string lang);
}

/// <summary>翻译进度</summary>
public readonly record struct TranslationProgress(int Completed, int Total, string? CurrentText);

/// <summary>翻译同步结果统计</summary>
public sealed class TranslationSyncResult
{
    /// <summary>新增文本数（译文/占位已计入）</summary>
    public int Added { get; set; }

    /// <summary>已存在且保留的翻译数</summary>
    public int Kept { get; set; }

    /// <summary>扫描到的原文总数（带来源条目，去重后）</summary>
    public int Scanned { get; set; }

    /// <summary>已有翻译文件中扫描不到的键数（UI 文本/已删旧文本，仅统计不删除）</summary>
    public int Removed { get; set; }

    /// <summary>本次实际翻译成功的条数</summary>
    public int Translated { get; set; }

    /// <summary>翻译失败回退原文的条数</summary>
    public int Failed { get; set; }

    /// <summary>输出语言根目录路径（如 {project}/Lang/en-US）</summary>
    public string? OutputPath { get; set; }

    /// <summary>待审批的每文件编辑集（<see cref="PrepareSyncAsync"/> 产物，未落盘）</summary>
    public IReadOnlyList<FileEdit>? PendingEdits { get; set; }

    /// <summary>整轮 diff 预览文本（<see cref="IFileEditor.RenderDiff"/> 拼接），供审批展示</summary>
    public string PreviewText { get; set; } = "";
}