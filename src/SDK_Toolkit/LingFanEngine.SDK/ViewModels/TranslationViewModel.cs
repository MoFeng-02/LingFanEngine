using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Services.Implementations;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>
/// 多语言翻译 ViewModel。
/// <para>扫描项目全部可翻译文本（DSL + C#），选择语言与翻译模式（Manual/AI/API），
/// 批量翻译（AI 单请求多条，30× 成本摊薄）并自动生成翻译文件，带进度与取消。</para>
/// </summary>
public partial class TranslationViewModel : ViewModelBase
{
    private readonly ITranslationService _translationService;
    private readonly IProjectSession _session;
    private readonly IModelService _modelService;
    private readonly ITranslatorFactory _translatorFactory;
    private CancellationTokenSource? _cts;

    /// <summary>项目根目录</summary>
    public string? ProjectDir => _session.IsProjectOpen ? _session.ProjectDirectory : null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    private string _statusMessage = "";

    /// <summary>目标语言（locale code，遵循引擎 i18n 标准：en-US / ja-JP / ko-KR…，用作 Lang/{lang}/ 语言根目录名、语言切换命令与可用语言列表；AI/API 模式据此决定输出语言）</summary>
    [ObservableProperty]
    private string _targetLang = "en-US";

    /// <summary>源语言（自然语言，如「中文」「English」；留空=让翻译器自动检测原文语言）</summary>
    [ObservableProperty]
    private string _sourceLang = "";

    /// <summary>翻译模式</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    private TranslationMode _mode = TranslationMode.Manual;

    /// <summary>强制重新翻译（忽略已有译文，全部重翻）</summary>
    [ObservableProperty]
    private bool _forceRetranslate;

    // ===== AI 模型（从「模型管理」添加，持久化于 %LOCALAPPDATA%/LingFanEngine.SDK/models.json）=====
    /// <summary>当前已保存模型列表（供翻译页下拉绑定；变更后调用 <see cref="RefreshModels"/> 刷新）</summary>
    public ObservableCollection<ModelConfig> Models { get; } = new();

    /// <summary>当前选中的模型 Id（空=未选；AI 模式据此创建翻译器）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    private string _selectedModelId = "";

    // API 配置（默认 DeepL——调研确认 tag_handling 保留富文本标记）
    [ObservableProperty]
    private string _apiEndpoint = "https://api-free.deepl.com/v2/translate";

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private string _apiTargetLangCode = "EN-US";

    /// <summary>扫描结果条目</summary>
    public ObservableCollection<TranslationEntry> Entries { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    private bool _isBusy;

    /// <summary>翻译进度（0-100）</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private int _scannedCount;

    [ObservableProperty]
    private int _translatedCount;

    [ObservableProperty]
    private int _untranslatedCount;

    public TranslationViewModel(
        ITranslationService translationService,
        IProjectSession session,
        IModelService modelService,
        ITranslatorFactory translatorFactory)
    {
        _translationService = translationService;
        _session = session;
        _modelService = modelService;
        _translatorFactory = translatorFactory;
        RefreshModels();

        // 项目打开/关闭时刷新扫描/同步按钮的可执行态。
        // ProjectDir 是派生属性（非 ObservableProperty），不订阅则项目打开后
        // SyncCommand.CanExecute 停留在旧值 → 按钮禁用、点击无任何反馈（“翻译没执行”）。
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IProjectSession.IsProjectOpen))
            {
                ScanCommand.NotifyCanExecuteChanged();
                SyncCommand.NotifyCanExecuteChanged();
                if (!_session.IsProjectOpen)
                    StatusMessage = "请先打开项目再翻译";
            }
        };
    }

    /// <summary>从模型服务重新拉取模型列表，并默认选中上次保存的默认模型</summary>
    public void RefreshModels()
    {
        Models.Clear();
        foreach (var m in _modelService.Models)
            Models.Add(m);
        if (string.IsNullOrEmpty(SelectedModelId))
        {
            var def = _modelService.GetDefault();
            SelectedModelId = def?.Id ?? (Models.Count > 0 ? Models[0].Id : "");
        }
        else if (_modelService.GetById(SelectedModelId) == null)
        {
            SelectedModelId = Models.Count > 0 ? Models[0].Id : "";
        }
    }

    /// <summary>扫描项目可翻译文本</summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (ProjectDir == null)
        {
            StatusMessage = "请先打开项目";
            return;
        }

        IsBusy = true;
        StatusMessage = "正在扫描可翻译文本...";
        Progress = 0;
        try
        {
            var texts = await _translationService.ScanTranslatableTextsAsync(ProjectDir);
            var status = await _translationService.GetTranslationStatusAsync(ProjectDir, TargetLang);

            Entries.Clear();
            foreach (var text in texts)
            {
                var translated = status.TryGetValue(text, out var t) && t;
                Entries.Add(new TranslationEntry(text, translated));
            }

            ScannedCount = Entries.Count;
            TranslatedCount = Entries.Count(e => e.IsTranslated);
            UntranslatedCount = ScannedCount - TranslatedCount;
            StatusMessage = $"扫描完成：共 {ScannedCount} 条（story + C#），已翻译 {TranslatedCount}，待翻译 {UntranslatedCount}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"扫描失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanScan() => !IsBusy && ProjectDir != null;

    /// <summary>同步翻译文件（扫描 → 按模式翻译新增 → 写入 Lang/{lang}/main.json）</summary>
    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync()
    {
        if (ProjectDir == null)
        {
            StatusMessage = "请先打开项目";
            return;
        }

        var translator = CreateTranslator();
        if (translator == null)
        {
            StatusMessage = Mode switch
            {
                TranslationMode.Ai => AiFailureHint(),
                TranslationMode.Api => "翻译 API 需要配置端点与 Key",
                _ => "请选择翻译模式",
            };
            return;
        }

        IsBusy = true;
        Progress = 0;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        StatusMessage = Mode switch
        {
            TranslationMode.Manual => "正在同步翻译文件（生成占位）...",
            TranslationMode.Ai => $"正在 AI 批量翻译（占位符自动校验）...",
            TranslationMode.Api => "正在调用翻译 API...",
            _ => "正在同步...",
        };

        var progress = new Progress<TranslationProgress>(p =>
        {
            Progress = p.Total > 0 ? p.Completed * 100.0 / p.Total : 0;
            StatusMessage = $"正在翻译 {p.Completed}/{p.Total}...";
        });

        try
        {
        var result = await _translationService.SyncWithTranslatorAsync(
            ProjectDir, TargetLang, translator, progress, ForceRetranslate, SourceLang, _cts.Token);

        var forceNote = ForceRetranslate ? "（强制重翻）" : "";
        StatusMessage = Mode switch
        {
            TranslationMode.Manual => $"同步完成{forceNote}：新增 {result.Added} 占位，保留 {result.Kept}，扫描 {result.Scanned}\n文件：{result.OutputPath}",
            _ => $"翻译完成{forceNote}：成功 {result.Translated}，失败回退 {result.Failed}，新增 {result.Added}，保留 {result.Kept}\n文件：{result.OutputPath}",
        };
        Progress = 100;

            // 刷新列表
            await ScanAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"同步失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanSync() => !IsBusy && ProjectDir != null;

    /// <summary>取消当前翻译</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelSync() => _cts?.Cancel();

    private bool CanCancel() => IsBusy;

    /// <summary>按当前模式创建翻译器</summary>
    private ITranslator? CreateTranslator() => Mode switch
    {
        TranslationMode.Manual => new ManualTranslator(),
        TranslationMode.Ai => BuildModelTranslator(),
        TranslationMode.Api => string.IsNullOrWhiteSpace(ApiKey) ? null : new ApiTranslator(new ApiTranslatorConfig
        {
            Endpoint = ApiEndpoint,
            ApiKey = ApiKey,
            TargetLangCode = ApiTargetLangCode,
        }),
        _ => null,
    };

    /// <summary>从选中的模型配置经工厂创建翻译器（无选中/缺配置返回 null）</summary>
    private ITranslator? BuildModelTranslator()
    {
        if (string.IsNullOrEmpty(SelectedModelId))
            return null;
        var model = _modelService.GetById(SelectedModelId);
        if (model == null)
            return null;
        return _translatorFactory.Create(model);
    }

    /// <summary>AI 模式翻译器为 null 时的精确提示（区分：无模型/未选中/缺 Key/缺 ModelId）</summary>
    private string AiFailureHint()
    {
        if (Models.Count == 0)
            return "AI 翻译需要先添加模型（点击「管理模型」）";
        if (string.IsNullOrEmpty(SelectedModelId))
            return "AI 翻译需要先在下拉框选择一个模型";
        var model = _modelService.GetById(SelectedModelId);
        if (model == null)
            return "所选模型已不存在，请重新选择";
        if (string.IsNullOrWhiteSpace(model.ModelId))
            return "所选模型缺少模型 ID，请在「模型管理」中补全";
        if (string.IsNullOrWhiteSpace(model.ApiKey) && !TranslatorFactory.IsLocal(model.BaseUrl))
            return $"所选模型「{model.DisplayOrId}」缺少 API Key（本地端点如 Ollama 可留空）";
        return "所选模型配置不完整，无法创建翻译器";
    }

    partial void OnModeChanged(TranslationMode value)
    {
        StatusMessage = value switch
        {
            TranslationMode.Manual => "人工模式：生成 value=原文占位，由人工填充翻译文件",
            TranslationMode.Ai => Models.Count == 0
                ? "AI 模式：尚未添加模型，请点击「管理模型」添加（OpenAI 兼容 / Anthropic）"
                : "AI 模式：按所选模型批量翻译，占位符自动校验重试",
            TranslationMode.Api => "API 模式：DeepL 风格翻译端点，tag_handling 保留富文本标记",
            _ => "",
        };
    }

    partial void OnIsBusyChanged(bool value)
    {
        CancelSyncCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>翻译条目（UI 列表项）</summary>
public sealed record TranslationEntry(string Text, bool IsTranslated);
