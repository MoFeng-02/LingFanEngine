using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>连通性测试结果分级</summary>
public enum ConnectivityStatus
{
    /// <summary>成功（200）</summary>
    Ok,

    /// <summary>网络不可达 / DNS 失败 / 超时</summary>
    NetworkError,

    /// <summary>401 鉴权失败</summary>
    Unauthorized,

    /// <summary>403 无权限（未开通账单/区域限制）</summary>
    Forbidden,

    /// <summary>429 限流</summary>
    RateLimited,

    /// <summary>5xx 服务端错误</summary>
    ServerError,

    /// <summary>其他 4xx（含 400）</summary>
    BadRequest,
}

/// <summary>连通性测试结果</summary>
public sealed class ConnectivityResult
{
    /// <summary>状态分级</summary>
    public ConnectivityStatus Status { get; init; }

    /// <summary>展示消息（含诊断）</summary>
    public string Message { get; init; } = "";

    /// <summary>模型总数（OK 时有效）</summary>
    public int ModelCount { get; init; }

    /// <summary>模型 ID 列表（OK 且端点返回 data[] 时有效；用于自动注册复用供应商列表）</summary>
    public IReadOnlyList<string> ModelIds { get; init; } = Array.Empty<string>();

    /// <summary>是否成功</summary>
    public bool IsSuccess => Status == ConnectivityStatus.Ok;
}

/// <summary>
/// 模型连通性测试——按 provider 分发 <c>GET {BaseUrl}/v1/models</c>。
/// <para>行业做法（OpenAI/Anthropic 统一）：该端点零成本、不消耗 token、立即返回，用于验证 Key 与网络。</para>
/// <para>OpenAI 用 <c>Authorization: Bearer</c>；Anthropic 用 <c>x-api-key</c> + <c>anthropic-version: 2023-06-01</c>（强制头）。</para>
/// </summary>
public sealed class ModelConnectivityService
{
    private readonly IHttpClientFactory? _httpClientFactory;

    /// <summary>创建连通性测试服务</summary>
    public ModelConnectivityService(IHttpClientFactory? httpClientFactory = null)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>测试模型连通性</summary>
    public async Task<ConnectivityResult> TestAsync(ModelConfig model, CancellationToken ct = default)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (string.IsNullOrWhiteSpace(model.BaseUrl))
            return new ConnectivityResult { Status = ConnectivityStatus.NetworkError, Message = "Base URL 为空" };

        // OpenAI 兼容端点的 Base URL 通常已含 /v1（如 https://api.openai.com/v1）；
        // 若直接拼 /v1/models 会重复成 /v1/v1/models（404）。自适应避免。
        // Anthropic 的 Base URL 为 host 根（https://api.anthropic.com），models 端点固定 /v1/models。
        var baseUrl = model.BaseUrl.TrimEnd('/');
        string modelsPath = model.Provider == ModelProvider.Anthropic
            ? "/v1/models"
            : (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? "/models" : "/v1/models");
        var url = baseUrl + modelsPath;
        using var client = _httpClientFactory?.CreateClient() ?? new HttpClient();
        if (_httpClientFactory == null)
            client.Timeout = TimeSpan.FromSeconds(15);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(model.ApiKey))
        {
            if (model.Provider == ModelProvider.Anthropic)
            {
                req.Headers.TryAddWithoutValidation("x-api-key", model.ApiKey);
                req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            }
            else
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", model.ApiKey);
            }
        }

        try
        {
            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            return await Classify(resp, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ConnectivityResult { Status = ConnectivityStatus.NetworkError, Message = $"网络不可达：{ex.Message}" };
        }
    }

    private static async Task<ConnectivityResult> Classify(HttpResponseMessage resp, CancellationToken ct)
    {
        var status = resp.StatusCode switch
        {
            System.Net.HttpStatusCode.OK => ConnectivityStatus.Ok,
            System.Net.HttpStatusCode.Unauthorized => ConnectivityStatus.Unauthorized,
            System.Net.HttpStatusCode.Forbidden => ConnectivityStatus.Forbidden,
            (System.Net.HttpStatusCode)429 => ConnectivityStatus.RateLimited,
            var s when (int)s >= 500 => ConnectivityStatus.ServerError,
            _ => ConnectivityStatus.BadRequest,
        };

        if (status == ConnectivityStatus.Ok)
        {
            var ids = new List<string>();
            try
            {
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var id = idEl.GetString();
                            if (!string.IsNullOrWhiteSpace(id)) ids.Add(id!);
                        }
                    }
                }
            }
            catch
            {
                // 解析失败不影响连通判定
            }
            return new ConnectivityResult
            {
                Status = ConnectivityStatus.Ok,
                Message = ids.Count > 0 ? $"已连接 · {ids.Count} 个模型" : "已连接（端点未返回模型列表）",
                ModelCount = ids.Count,
                ModelIds = ids,
            };
        }

        var hint = status switch
        {
            ConnectivityStatus.Unauthorized => "401 鉴权失败（API Key 无效）",
            ConnectivityStatus.Forbidden => "403 无权限（可能未开通账单/该区域）",
            ConnectivityStatus.RateLimited => "429 请求过于频繁（限流）",
            ConnectivityStatus.ServerError => "服务端错误（5xx）",
            _ => $"请求失败（{(int)resp.StatusCode}）",
        };
        return new ConnectivityResult { Status = status, Message = hint };
    }
}
