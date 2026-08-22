using System;
using System.Collections.ObjectModel;
using System.Linq;
using LingFanEngine.SDK.I18n;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Services.Implementations;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>供应商分组头（插入 GroupedItems 中的非 Model 标记对象）</summary>
public sealed record VendorGroupHeader(string Vendor, int Count);

/// <summary>
/// 模型管理页面 ViewModel——列出已保存模型，提供新增/编辑/删除/设默认。
/// <para>模型持久化由 <see cref="IModelService"/> 负责（models.json）。</para>
/// </summary>
public partial class ModelsViewModel : ViewModelBase
{
    private readonly IModelService _modelService;

    /// <summary>模型列表（平铺，原始增删改操作对象）</summary>
    public ObservableCollection<ModelConfig> Models { get; } = new();

    /// <summary>
    /// 分组展示列表：供 View 直接绑定。元素类型 = <see cref="VendorGroupHeader"/> | <see cref="ModelConfig"/>。
    /// <para>组 = 按 BaseUrl 域名推断的供应商；组内 = 默认模型置顶 → 其余按名称排序。</para>
    /// </summary>
    public ObservableCollection<object> GroupedItems { get; } = new();

    /// <summary>当前默认模型 Id（列表高亮用）</summary>
    public string? DefaultId => _modelService.DefaultModelId;

    // ===== 本地化静态文案（供页面绑定） =====
    public string PageTitle => SdkLocalizer.Loc("Models_Title");
    public string PageDescription => SdkLocalizer.Loc("Models_Desc");
    public string AddHint => SdkLocalizer.Loc("Models_Hint");
    public string EmptyHintText => SdkLocalizer.Loc("Models_Empty");
    public string AddModelLabel => SdkLocalizer.Loc("Action_AddModel");
    public string EditModelLabel => SdkLocalizer.Loc("Action_EditModel");
    public string DeleteModelLabel => SdkLocalizer.Loc("Action_DeleteModel");
    public string SetDefaultLabel => SdkLocalizer.Loc("Action_SetDefault");
    public string DefaultBadge => SdkLocalizer.Loc("Models_Default");
    public string ModelCountFormat => SdkLocalizer.Loc("Models_Count");

    /// <summary>创建模型管理 ViewModel</summary>
    public ModelsViewModel(IModelService modelService)
    {
        _modelService = modelService;
        Refresh();
    }

    /// <summary>从服务重新拉取模型列表并重建分组展示</summary>
    public void Refresh()
    {
        Models.Clear();
        foreach (var m in _modelService.Models)
            Models.Add(m);
        RebuildGroupedItems();
    }

    /// <summary>按供应商分组 + 默认置顶，重建 <see cref="GroupedItems"/></summary>
    private void RebuildGroupedItems()
    {
        GroupedItems.Clear();
        var defaultId = DefaultId;
        var groups = Models
            .GroupBy(m => VendorOf(m))
            .OrderBy(g => g.Any(m => m.Id == defaultId) ? 0 : 1)   // 含默认模型的供应商组排最前
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            GroupedItems.Add(new VendorGroupHeader(g.Key, g.Count()));
            var ordered = g
                .OrderByDescending(m => m.Id == defaultId)   // 默认模型排第一
                .ThenBy(m => m.DisplayOrId, StringComparer.OrdinalIgnoreCase);
            foreach (var m in ordered)
                GroupedItems.Add(m);
        }
    }

    /// <summary>供应商分组名：优先命中预定品牌映射；未预定的域名回退为「主域名」（去 TLD 后缀与子域名前缀）。</summary>
    private static string VendorOf(ModelConfig m)
    {
        var host = "";
        if (Uri.TryCreate(m.BaseUrl?.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            host = uri.Host.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(host))
            return m.Provider == ModelProvider.Anthropic ? "Anthropic" : SdkLocalizer.Loc("Models_Unnamed");

        // ---- 预定品牌映射（展示名）----
        if (host.Contains("openai") || host.Contains("azure-openai")) return "OpenAI";
        if (host.Contains("anthropic")) return "Anthropic";
        if (host.Contains("deepseek")) return "DeepSeek";
        if (host.Contains("dashscope") || host.Contains("aliyun") || host.Contains("qwen")) return "通义千问";
        if (host.Contains("moonshot")) return "Moonshot";
        if (host.Contains("zhipu") || host.Contains("bigmodel")) return "智谱 GLM";
        if (host.Contains("volces") || host.Contains("ark")) return "火山方舟";
        if (host.Contains("groq")) return "Groq";
        if (host.Contains("siliconflow")) return "硅基流动";
        if (host.Contains("mistral")) return "Mistral";
        if (host.Contains("googleapis") || host.Contains("generativelanguage")) return "Gemini";
        // 本地/内网：原样（不参与主域名规则）
        if (host is "localhost" or "127.0.0.1" || System.Net.IPAddress.TryParse(host, out _))
            return host;

        // ---- 未预定的域名：取主域名，如 api.example.cn → example ----
        var parts = host.Split('.');
        return parts.Length >= 2 ? parts[^2] : host;
    }

    /// <summary>新增或更新模型</summary>
    public void Upsert(ModelConfig model)
    {
        _modelService.Upsert(model);
        Refresh();
    }

    /// <summary>删除模型（按 Id）</summary>
    public void Remove(string id)
    {
        _modelService.Remove(id);
        Refresh();
    }

    /// <summary>设为默认模型</summary>
    public void SetDefault(string id)
    {
        _modelService.DefaultModelId = id;
        Refresh();
    }
}
