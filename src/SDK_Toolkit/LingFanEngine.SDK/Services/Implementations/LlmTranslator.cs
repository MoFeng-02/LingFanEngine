using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.AI;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 统一 LLM 翻译器——持有 <see cref="IModelClient"/>（OpenAI 兼容 / Anthropic 由工厂分发）与高级配置，
/// 翻译逻辑全部委托共享的 <see cref="TranslationBatchExecutor"/>。替代原 <c>AiTranslator/AnthropicTranslator</c> 双实现。
/// </summary>
public sealed class LlmTranslator : ITranslator
{
    private readonly IModelClient _client;
    private readonly ModelAdvancedConfig _advanced;
    private readonly UsageStats _usage = new();

    /// <summary>本次翻译累计用量（输入/输出 tokens、请求次数）。</summary>
    public UsageStats Usage => _usage;

    /// <summary>每批翻译完成后的进度回调（由宿主在调用前注入，用于实数时反馈）。</summary>
    public IProgress<TranslationProgress>? Progress { get; set; }

    /// <summary>创建 LLM 翻译器。</summary>
    public LlmTranslator(IModelClient client, ModelAdvancedConfig advanced)
    {
        _client = client ?? throw new System.ArgumentNullException(nameof(client));
        _advanced = advanced ?? throw new System.ArgumentNullException(nameof(advanced));
    }

    /// <inheritdoc/>
    public async Task<string?> TranslateAsync(string text, string targetLang, string sourceLang = "", CancellationToken ct = default)
    {
        var batch = await TranslateBatchAsync([text], targetLang, sourceLang, ct).ConfigureAwait(false);
        return batch.Count > 0 ? batch[0] : null;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string?>> TranslateBatchAsync(
        IReadOnlyList<string> texts, string targetLang, string sourceLang = "", CancellationToken ct = default)
        => TranslationBatchExecutor.TranslateAsync(_client, _advanced, texts, targetLang, sourceLang, ct, _usage, Progress);
}