using System.Collections.ObjectModel;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Services.Implementations;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>
/// 模型管理页面 ViewModel——列出已保存模型，提供新增/编辑/删除/设默认。
/// <para>模型持久化由 <see cref="IModelService"/> 负责（models.json）。</para>
/// </summary>
public partial class ModelsViewModel : ViewModelBase
{
    private readonly IModelService _modelService;

    /// <summary>模型列表（绑定到页面 ListBox）</summary>
    public ObservableCollection<ModelConfig> Models { get; } = new();

    /// <summary>当前默认模型 Id（列表高亮用）</summary>
    public string? DefaultId => _modelService.DefaultModelId;

    /// <summary>创建模型管理 ViewModel</summary>
    public ModelsViewModel(IModelService modelService)
    {
        _modelService = modelService;
        Refresh();
    }

    /// <summary>从服务重新拉取模型列表</summary>
    public void Refresh()
    {
        Models.Clear();
        foreach (var m in _modelService.Models)
            Models.Add(m);
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
