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
}

/// <summary>
/// 翻译器抽象——单条/批量文本翻译。
/// <para>实现：<see cref="ManualTranslator"/>（占位）/ <see cref="AiTranslator"/>（OpenAI 兼容 LLM）/ <see cref="AnthropicTranslator"/>（Claude）/ <see cref="ApiTranslator"/>（翻译 API）。</para>
/// </summary>
public interface ITranslator
{
    /// <summary>翻译单条文本</summary>
    /// <param name="text">原文</param>
    /// <param name="targetLang">目标语言（自然语言，如「日语」「English」「法语」；AI 模式直接据此生成对应语言，API 模式由配置里的语言码决定）</param>
    /// <param name="sourceLang">源语言（自然语言，如「中文」「English」；留空表示让翻译器自动检测）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>译文；失败或无法翻译时返回 null（调用方回退原文）</returns>
    Task<string?> TranslateAsync(string text, string targetLang, string sourceLang = "", CancellationToken ct = default);

    /// <summary>
    /// 批量翻译文本（AI/API 模式核心——单次请求翻译多条，大幅降低成本与延迟）。
    /// <para>按入参顺序返回译文数组；失败条目返回 null。默认实现逐条调用 <see cref="TranslateAsync"/>。</para>
    /// </summary>
    /// <param name="texts">原文列表</param>
    /// <param name="targetLang">目标语言（自然语言，如「日语」「English」）</param>
    /// <param name="sourceLang">源语言（自然语言，留空=自动检测）</param>
    /// <param name="ct">取消令牌</param>
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
/// 翻译同步服务——扫描 .story + C# 全部可翻译文本，增量维护翻译文件，支持自动翻译。
/// <para>与引擎运行时 <c>I18nService</c> 约定完全一致：翻译文件为 <c>Lang/{lang}/main.json</c> 或 <c>Lang/{lang}.json</c>，
/// 键=原文，值=译文；找不到译文时引擎回退原文。</para>
/// </summary>
public interface ITranslationService
{
    /// <summary>
    /// 扫描项目全部可翻译文本（.story DSL + C# 源码 API 文本）。
    /// <para>覆盖：say/菜单/选项/输入/通知/存档标题 + UI 元素 text/button/checkbox + C# SayAsync/ExtendAsync/ShowMenuAsync/ChoiceAsync/InputAsync/Notify/AddButton/AddMenu/AddText/SetScene。</para>
    /// </summary>
    /// <param name="projectDir">项目根目录（含 Resources/）</param>
    /// <returns>去重后的可翻译文本集合</returns>
    Task<IReadOnlyList<string>> ScanTranslatableTextsAsync(string projectDir);

    /// <summary>
    /// 为指定语言同步翻译文件——保留已有译文、追加新增文本、标记已删除文本。
    /// <para>生成 <c>Lang/{lang}/main.json</c>（项目根目录形式，与 I18nService 目录加载约定一致）。
    /// 新增条目 value=原文占位，由人工/机器翻译填充。</para>
    /// </summary>
    Task<TranslationSyncResult> SyncAsync(string projectDir, string lang);

    /// <summary>
    /// 自动翻译并同步——扫描 → 翻译器翻译新增条目 → 写回翻译文件。
    /// <para>已有译文保留不重翻；新增条目经 <paramref name="translator"/> 翻译，翻译失败回退原文。</para>
    /// </summary>
    /// <param name="projectDir">项目根目录</param>
    /// <param name="lang">目标语言代码（如 en-US）</param>
    /// <param name="translator">翻译器（Manual=占位 / Ai / Api）</param>
    /// <param name="ct">取消令牌</param>
    Task<TranslationSyncResult> SyncWithTranslatorAsync(string projectDir, string lang, ITranslator translator, string sourceLang = "", CancellationToken ct = default);

    /// <summary>
    /// 自动翻译并同步（带进度回调）。
    /// <para>扫描 → 批量翻译新增条目 → 写回翻译文件。progress 回调：已完成数/总数。</para>
    /// </summary>
    Task<TranslationSyncResult> SyncWithTranslatorAsync(
        string projectDir, string lang, ITranslator translator,
        IProgress<TranslationProgress>? progress, string sourceLang = "", CancellationToken ct = default);

    /// <summary>
    /// 自动翻译并同步（带强制重翻译开关 + 进度回调）。
    /// <para><paramref name="forceRetranslate"/>=true 时忽略已有译文，全部条目重新翻译；
    /// =false 时跳过已翻译条目（保留已有译文，仅翻译新增/未翻译）。</para>
    /// </summary>
    Task<TranslationSyncResult> SyncWithTranslatorAsync(
        string projectDir, string lang, ITranslator translator,
        IProgress<TranslationProgress>? progress, bool forceRetranslate, string sourceLang = "", CancellationToken ct = default);

    /// <summary>
    /// 自动翻译并同步（带强制重翻译开关，无进度回调）。
    /// </summary>
    Task<TranslationSyncResult> SyncWithTranslatorAsync(
        string projectDir, string lang, ITranslator translator,
        bool forceRetranslate, string sourceLang = "", CancellationToken ct = default);

    /// <summary>列出项目中已有的翻译键集合（未翻译=值等于原文）</summary>
    Task<IReadOnlyDictionary<string, bool>> GetTranslationStatusAsync(string projectDir, string lang);
}

/// <summary>翻译进度</summary>
public readonly record struct TranslationProgress(int Completed, int Total, string? CurrentText);

/// <summary>翻译同步结果统计</summary>
public sealed class TranslationSyncResult
{
    /// <summary>新增文本数（已写入占位/译文）</summary>
    public int Added { get; set; }

    /// <summary>已存在且保留的翻译数</summary>
    public int Kept { get; set; }

    /// <summary>扫描到的原文总数（去重后）</summary>
    public int Scanned { get; set; }

    /// <summary>上次翻译文件中已不存在的键数（标记删除，不删除）</summary>
    public int Removed { get; set; }

    /// <summary>本次实际翻译成功的条数</summary>
    public int Translated { get; set; }

    /// <summary>翻译失败回退原文的条数</summary>
    public int Failed { get; set; }

    /// <summary>输出文件路径</summary>
    public string? OutputPath { get; set; }
}
