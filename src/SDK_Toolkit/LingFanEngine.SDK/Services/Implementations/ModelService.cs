using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 模型管理实现——持久化到 <c>%LOCALAPPDATA%/LingFanEngine.SDK/models.json</c>。
/// <para>AOT 安全：通过 <see cref="SdkJsonContext"/> 源生成序列化，零反射；线程安全（内部锁）。</para>
/// </summary>
public sealed class ModelService : IModelService
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<ModelConfig> _models = new();
    private string? _defaultModelId;

    /// <summary>创建模型服务并立即从磁盘加载已有模型</summary>
    public ModelService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LingFanEngine.SDK");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "models.json");
        Load();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ModelConfig> Models
    {
        get { lock (_lock) return _models.ToList(); }
    }

    /// <inheritdoc/>
    public string? DefaultModelId
    {
        get { lock (_lock) return _defaultModelId; }
        set { lock (_lock) { _defaultModelId = value; Save(); } }
    }

    /// <inheritdoc/>
    public ModelConfig? GetDefault()
    {
        lock (_lock)
        {
            var id = _defaultModelId;
            return id == null ? null : _models.FirstOrDefault(m => m.Id == id);
        }
    }

    /// <inheritdoc/>
    public ModelConfig? GetById(string id)
    {
        lock (_lock) return _models.FirstOrDefault(m => m.Id == id);
    }

    /// <inheritdoc/>
    public void Upsert(ModelConfig model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        lock (_lock)
        {
            var idx = _models.FindIndex(m => m.Id == model.Id);
            if (idx >= 0) _models[idx] = model;
            else _models.Add(model);
            if (string.IsNullOrEmpty(_defaultModelId))
                _defaultModelId = model.Id;
            Save();
        }
    }

    /// <inheritdoc/>
    public void RegisterModels(IEnumerable<ModelConfig> incoming, out int added, out int skipped)
    {
        if (incoming == null) throw new ArgumentNullException(nameof(incoming));
        added = 0;
        skipped = 0;
        lock (_lock)
        {
            var seen = new HashSet<string>(
                _models.Select(m => DedupeKey(m.Provider, m.BaseUrl, m.ModelId)),
                StringComparer.Ordinal);
            foreach (var m in incoming)
            {
                var key = DedupeKey(m.Provider, m.BaseUrl, m.ModelId);
                if (seen.Contains(key)) { skipped++; continue; }
                _models.Add(m);
                seen.Add(key);
                if (string.IsNullOrEmpty(_defaultModelId))
                    _defaultModelId = m.Id;
                added++;
            }
            if (added > 0) Save();
        }
    }

    /// <summary>去重键：Provider + 规范化 BaseUrl + ModelId（同供应商同端点同模型视为一个）</summary>
    private static string DedupeKey(ModelProvider provider, string baseUrl, string modelId) =>
        $"{(int)provider}|{NormalizeBaseUrl(baseUrl)}|{modelId}";

    /// <summary>规范化 Base URL：去空白、去尾斜杠、转小写（消除 …/v1 与 …/v1/ 差异）</summary>
    private static string NormalizeBaseUrl(string baseUrl) =>
        (baseUrl ?? "").Trim().TrimEnd('/').ToLowerInvariant();

    /// <inheritdoc/>
    public void Remove(string id)
    {
        lock (_lock)
        {
            _models.RemoveAll(m => m.Id == id);
            if (_defaultModelId == id)
                _defaultModelId = _models.Count > 0 ? _models[0].Id : null;
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            var data = JsonHelper.Deserialize(json, SdkJsonContext.Default.ModelsFile);
            if (data?.Models != null)
            {
                _models = data.Models;
                _defaultModelId = data.DefaultModelId;
            }
        }
        catch (Exception)
        {
            _models = new List<ModelConfig>();
            _defaultModelId = null;
        }
    }

    private void Save()
    {
        try
        {
            var data = new ModelsFile
            {
                Models = _models,
                DefaultModelId = _defaultModelId,
            };
            var json = JsonHelper.Serialize(data, SdkJsonContext.Default.ModelsFile);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception)
        {
            // 保存失败静默（内存态仍可用）
        }
    }
}
