using System.Collections.Generic;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>模型管理——CRUD + 持久化（%LOCALAPPDATA%/LingFanEngine.SDK/models.json）</summary>
public interface IModelService
{
    /// <summary>当前所有已保存模型（只读快照；增删改后调用方应重新拉取）</summary>
    IReadOnlyList<ModelConfig> Models { get; }

    /// <summary>默认模型 Id（空=无）</summary>
    string? DefaultModelId { get; set; }

    /// <summary>获取默认模型（可能为 null）</summary>
    ModelConfig? GetDefault();

    /// <summary>按 Id 获取模型（可能为 null）</summary>
    ModelConfig? GetById(string id);

    /// <summary>新增或更新模型（Id 相同则覆盖），落盘</summary>
    void Upsert(ModelConfig model);

    /// <summary>
    /// 批量注册模型（自动从供应商 <c>GET /v1/models</c> 拉取后调用）。
    /// 去重键 = (Provider, 规范化 BaseUrl, ModelId)；已存在则跳过且不覆盖既有 DisplayName/Advanced；
    /// 当前无默认模型时设首个新增项为默认。返回新增/跳过计数。
    /// </summary>
    void RegisterModels(IEnumerable<ModelConfig> incoming, out int added, out int skipped);

    /// <summary>删除模型（按 Id），落盘并维护默认项</summary>
    void Remove(string id);
}
