using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 人工翻译器——返回原文占位，由人工/编辑器填充翻译文件。
/// </summary>
public sealed class ManualTranslator : ITranslator
{
    /// <inheritdoc/>
    public Task<string?> TranslateAsync(string text, string targetLang, string sourceLang = "", CancellationToken ct = default)
        => Task.FromResult<string?>(text); // 占位：值=原文

    /// <inheritdoc/>
    public Task<IReadOnlyList<string?>> TranslateBatchAsync(
        IReadOnlyList<string> texts, string targetLang, string sourceLang = "", CancellationToken ct = default)
    {
        var result = new string?[texts.Count];
        for (var i = 0; i < texts.Count; i++)
            result[i] = texts[i];
        return Task.FromResult<IReadOnlyList<string?>>(result);
    }
}
