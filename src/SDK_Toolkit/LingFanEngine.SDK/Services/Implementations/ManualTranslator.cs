using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 人工翻译器——返回空字符串占位（译文留空），由人工/外部编辑器填充翻译文件。
/// </summary>
public sealed class ManualTranslator : ITranslator
{
    /// <inheritdoc/>
    public Task<string?> TranslateAsync(string text, string targetLang, string sourceLang = "", CancellationToken ct = default)
        => Task.FromResult<string?>(""); // 占位：译文留空，等待人工填充

    /// <inheritdoc/>
    public Task<IReadOnlyList<string?>> TranslateBatchAsync(
        IReadOnlyList<string> texts, string targetLang, string sourceLang = "", CancellationToken ct = default)
    {
        var result = new string?[texts.Count];
        for (var i = 0; i < texts.Count; i++)
            result[i] = "";
        return Task.FromResult<IReadOnlyList<string?>>(result);
    }
}
