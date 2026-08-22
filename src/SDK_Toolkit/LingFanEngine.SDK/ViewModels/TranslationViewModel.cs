using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingFanEngine.SDK.AI;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Services.Implementations;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>
/// 多语言翻译 ViewModel。
/// <para>扫描项目全部可翻译文本（DSL + C#，带来源），选择语言与翻译模式（Manual/AI/API），
/// 按所选布局（Flat/Mirrored/SingleFile）批量翻译生成翻译文件；写入走 <see cref="IFileEditor"/> 原子写 + diff 审批。
/// 布局选择持久化到项目（ProjectConfig.TranslationLayout，项目级优先）。</para>
/// </summary>
public partial class TranslationViewModel : ViewModelBase
{
    private readonly ITranslationService _translationService;
    private readonly IProjectSession _session;
    private readonly IModelService _modelService;
    private readonly ITranslatorFactory _translatorFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IModelClientFactory _modelClientFactory;
    private CancellationTokenSource? _cts;

    /// <summary>项目根目录</summary>
    public string? ProjectDir => _session.IsProjectOpen ? _session.ProjectDirectory : null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    private string _statusMessage = "";

    /// <summary>目标语言（locale code，遵循引擎 i18n 标准：en-US / ja-JP / ko-KR…，用作 Lang/{lang}/ 语言根目录名）</summary>
    [ObservableProperty]
    private string _targetLang = "en-US";

    /// <summary>源语言（自然语言，如「中文」「English」；留空=让翻译器自动检测）</summary>
    [ObservableProperty]
    private string _sourceLang = "";

    /// <summary>翻译输出布局（默认扁平 Flat；Mirrored=子文件夹分类逐 story；SingleFile=单文件）。项目级持久化。</summary>
    [ObservableProperty]
    private TranslationLayout _layout = TranslationLayout.Flat;

    /// <summary>翻译模式</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    private TranslationMode _mode = TranslationMode.Manual;

    /// <summary>强制重新翻译（忽略已有译文，全部重翻）</summary>
    [ObservableProperty]
    private bool _forceRetranslate;

    /// <summary>整轮 diff 预览（Prepare 产物，供审批/展示）</summary>
    [ObservableProperty]
    private string _previewText = "";

    /// <summary>待审批的编辑集（Prepare 产物；应用前不写盘）</summary>
    private IReadOnlyList<FileEdit>? _pendingEdits;

    /// <summary>是否有待审批的变更（控制预览面板与审批按钮）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveSyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardSyncCommand))]
    private bool _hasPending;

    // ===== AI 模型（从「模型管理」添加，持久化于 %LOCALAPPDATA%/LingFanEngine.SDK/models.json）=====
    public ObservableCollection<ModelConfig> Models { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    private string _selectedModelId = "";

    // API 配置（默认 DeepL）
    [ObservableProperty]
    private string _apiEndpoint = "https://api-free.deepl.com/v2/translate";

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private string _apiTargetLangCode = "EN-US";

    /// <summary>扫描结果条目（按原文去重展示）</summary>
    public ObservableCollection<TranslationEntry> Entries { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private int _scannedCount;

    [ObservableProperty]
    private int _translatedCount;

    [ObservableProperty]
    private int _untranslatedCount;

    private bool _isLoading; // 抑制加载时的布局回写

    public TranslationViewModel(
        ITranslationService translationService,
        IProjectSession session,
        IModelService modelService,
        ITranslatorFactory translatorFactory,
        IHttpClientFactory httpClientFactory,
        IModelClientFactory modelClientFactory)
    {
        _translationService = translationService;
        _session = session;
        _modelService = modelService;
        _translatorFactory = translatorFactory;
        _httpClientFactory = httpClientFactory;
        _modelClientFactory = modelClientFactory;
        RefreshModels();

        // 项目打开/关闭：刷新可执行态 + 从项目读布局
        _session.ProjectOpened += OnProjectOpened;
        _session.ProjectClosed += OnProjectClosed;
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

        if (_session.IsProjectOpen)
            OnProjectOpened();
    }

    private void OnProjectOpened()
    {
        _isLoading = true;
        try
        {
            if (_session.CurrentProject != null)
                Layout = _session.CurrentProject.TranslationLayout;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OnProjectClosed()
    {
        _isLoading = true;
        try
        {
            Entries.Clear();
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>布局变更即持久化到项目（项目级优先于用户默认）。</summary>
    partial void OnLayoutChanged(TranslationLayout value)
    {
        if (_isLoading || !_session.IsProjectOpen || _session.CurrentProject == null)
            return;
        _session.CurrentProject.TranslationLayout = value;
        _ = _session.SaveCurrentProjectAsync();
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

    /// <summary>扫描项目可翻译文本（带来源），按原文去重展示</summary>
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
            var scanned = await _translationService.ScanTranslatableTextsAsync(ProjectDir);
            var status = await _translationService.GetTranslationStatusAsync(ProjectDir, TargetLang);

            // 带来源条目按原文去重后展示
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            Entries.Clear();
            foreach (var st in scanned)
            {
                if (!distinct.Add(st.Text)) continue;
                var translated = status.TryGetValue(st.Text, out var t) && t;
                Entries.Add(new TranslationEntry(st.Text, translated));
            }

            ScannedCount = Entries.Count;
            TranslatedCount = Entries.Count(e => e.IsTranslated);
            UntranslatedCount = ScannedCount - TranslatedCount;
            StatusMessage = $"扫描完成：共 {ScannedCount} 条，已翻译 {TranslatedCount}，待翻译 {UntranslatedCount}";
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

    /// <summary>同步翻译文件（Prepare → diff 预览 → Apply 原子落盘）</summary>
    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync()
    {
        if (ProjectDir == null)
        {
            StatusMessage = "请先打开项目";
            return;
        }

        // Agent 模式：交给翻译 Agent 编排（不走普通翻译器）
        if (Mode == TranslationMode.Agent)
        {
            await RunAgentAsync();
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

        await RunPrepareAsync(translator, Mode switch
        {
            TranslationMode.Manual => "正在准备翻译文件（生成占位）...",
            TranslationMode.Ai => "正在 AI 批量翻译（占位符自动校验）...",
            TranslationMode.Api => "正在调用翻译 API...",
            _ => "正在同步...",
        }, isGenerate: false);
    }

    private bool CanSync() => !IsBusy && ProjectDir != null;

    /// <summary>生成模式文件：按所选布局用占位构建对应格式的翻译文件（不动用外部 AI），走 diff 审批。</summary>
    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task GenerateModeFilesAsync()
    {
        if (ProjectDir == null)
        {
            StatusMessage = "请先打开项目";
            return;
        }
        await RunPrepareAsync(new ManualTranslator(), $"正在生成 {LayoutName()} 模式翻译文件（占位）...", isGenerate: true);
    }

    /// <summary>
    /// 核心准备流程：扫描 → 按所选布局路由 → 翻译/占位 → 构建每文件 FileEdit + diff 预览 → 切到待审批态。
    /// <paramref name="isGenerate"/> 为 true 表示"生成模式文件"（手动占位，仅建结构不含翻译）。
    /// </summary>
    private async Task RunPrepareAsync(ITranslator translator, string busyMessage, bool isGenerate)
    {
        IsBusy = true;
        Progress = 0;
        PreviewText = "";
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        StatusMessage = busyMessage;

        var progress = new Progress<TranslationProgress>(p =>
        {
            Progress = p.Total > 0 ? p.Completed * 100.0 / p.Total : 0;
            StatusMessage = $"正在翻译 {p.Completed}/{p.Total}...";
        });

        try
        {
            var result = await _translationService.PrepareSyncAsync(
                ProjectDir!, TargetLang, Layout, translator, progress, ForceRetranslate, SourceLang, _cts.Token);

            _pendingEdits = result.PendingEdits is { Count: > 0 } ? result.PendingEdits : null;
            PreviewText = result.PreviewText;
            HasPending = _pendingEdits is { Count: > 0 };
            IsBusy = false;

            var forceNote = ForceRetranslate ? "（强制重翻）" : "";
            var layoutName = LayoutName();
            StatusMessage = _pendingEdits is { Count: > 0 }
                ? isGenerate
                    ? $"已生成 {_pendingEdits.Count} 个 {layoutName} 模式文件（占位，仅结构不含翻译）{forceNote}：新增 {result.Added}，保留 {result.Kept}。请审阅后「应用变更」。"
                    : Mode switch
                    {
                        TranslationMode.Manual => $"已生成 {_pendingEdits.Count} 个文件变更（占位）{forceNote}：新增 {result.Added}，保留 {result.Kept} · 布局 {layoutName}。请审阅预览后「应用变更」。",
                        _ => $"已准备 {_pendingEdits.Count} 个文件变更{forceNote}：翻译 {result.Translated}，回退 {result.Failed}，新增 {result.Added}，保留 {result.Kept} · 布局 {layoutName}。请审阅后「应用变更」。",
                    }
                : $"未产生文件变更：新增 0 · 保留 {result.Kept} · 已是最新（{layoutName}）。";
            StatusMessage += FormatUsage((translator as LlmTranslator)?.Usage);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"{(isGenerate ? "生成" : "同步")}失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Agent 模式：经 tool calling 理解项目、规划翻译，产出待审批变更（不写盘），复用界面整轮 diff 审批。
    /// </summary>
    private async Task RunAgentAsync()
    {
        var model = _modelService.GetById(SelectedModelId);
        if (model == null || string.IsNullOrWhiteSpace(model.ModelId)
            || (string.IsNullOrWhiteSpace(model.ApiKey) && !ModelClientFactory.IsLocal(model.BaseUrl)))
        {
            StatusMessage = AiFailureHint();
            return;
        }

        IsBusy = true;
        Progress = 0;
        PreviewText = "";
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        StatusMessage = "Agent 正在理解项目并规划翻译（tool calling）...";
        var progress = new Progress<TranslationProgress>(p =>
        {
            Progress = p.Total > 0 ? p.Completed * 100.0 / p.Total : 0;
            StatusMessage = $"Agent 正在翻译 {p.Completed}/{p.Total}...";
        });

        try
        {
            var client = _modelClientFactory.Create(model);
            if (client == null)
            {
                StatusMessage = "无法创建模型客户端";
                return;
            }
            var translator = new LlmTranslator(client, model.Advanced);
            var agent = new TranslationAgent(client, _translationService, translator);
            var req = new TranslationAgentRequest
            {
                ProjectDir = ProjectDir!,
                TargetLang = TargetLang,
                Layout = Layout,
                SourceLang = SourceLang,
                AutoApprove = false, // 走界面整轮 diff 审批
                Progress = progress,
                OnStep = PostStatus, // 理解项目 + tool calling 阶段的分步实时反馈
            };
            var ar = await agent.RunAsync(req, _cts.Token);

            _pendingEdits = ar.Sync is { PendingEdits: { Count: > 0 } } ? ar.Sync.PendingEdits : null;
            PreviewText = ar.Sync?.PreviewText ?? "";
            HasPending = _pendingEdits is { Count: > 0 };
            IsBusy = false;

            var toolCount = ar.ToolCalls?.Count ?? 0;
            StatusMessage = _pendingEdits is { Count: > 0 }
                ? $"Agent 完成（{toolCount} 次工具调用）：已准备 {_pendingEdits.Count} 个文件变更，待审批。\n模型说明：{ar.FinalText}"
                : $"Agent 未产生文件变更（{toolCount} 次工具调用）。\n模型说明：{ar.FinalText}";
            StatusMessage += FormatUsage(ar.Usage);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Agent 运行失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private string LayoutName() => Layout switch
    {
        TranslationLayout.Mirrored => "子文件夹分类",
        TranslationLayout.SingleFile => "单文件",
        _ => "扁平",
    };

    /// <summary>把 Agent 分步状态安全写到 UI 线程（回调可能运行在后台线程）。</summary>
    private void PostStatus(string s)
    {
        var dispatcher = Avalonia.Threading.Dispatcher.UIThread;
        if (dispatcher.CheckAccess())
            StatusMessage = s;
        else
            dispatcher.Post(() => StatusMessage = s);
    }

    /// <summary>格式化用量（无用量返回空串，避免噪音）</summary>
    private static string FormatUsage(UsageStats? u)
    {
        if (u == null || (u.InputTokens == 0 && u.OutputTokens == 0 && u.RequestCount <= 1))
            return "";
        var i = u.InputTokens.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        var o = u.OutputTokens.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        return $"\n用量：输入 {i} tok · 输出 {o} tok · 请求 {u.RequestCount}";
    }

    /// <summary>取消当前翻译（Prepare 中）</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelSync() => _cts?.Cancel();

    private bool CanCancel() => IsBusy;

    /// <summary>应用已审批的待提交变更（原子写 + 备份；失败自动回滚）。</summary>
    [RelayCommand(CanExecute = nameof(CanApprove))]
    private async Task ApproveSyncAsync()
    {
        if (_pendingEdits is not { Count: > 0 }) return;
        IsBusy = true;
        try
        {
            var dir = ProjectDir ?? "";
            var applied = await _translationService.ApplyEditsAsync(_pendingEdits, CancellationToken.None);
            await ScanAsync(); // 刷新列表（未翻译数应下降）
            StatusMessage = $"已应用 {applied} 处文件变更到 {dir}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"应用变更失败（已回滚）：{ex.Message}";
        }
        finally
        {
            _pendingEdits = null;
            HasPending = false;
            PreviewText = "";
            IsBusy = false;
        }
    }

    private bool CanApprove() => HasPending && !IsBusy;

    /// <summary>放弃本轮准备的文件变更（不写盘）。</summary>
    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private Task DiscardSyncAsync()
    {
        _pendingEdits = null;
        HasPending = false;
        PreviewText = "";
        IsBusy = false;
        StatusMessage = "已放弃本轮变更（未写盘）";
        return Task.CompletedTask;
    }

    private bool CanDiscard() => HasPending && !IsBusy;

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
        }, _httpClientFactory),
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

    /// <summary>AI 模式翻译器为 null 时的精确提示</summary>
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
            TranslationMode.Manual => "人工模式：生成译文留空的翻译文件，由人工/外部 AI 填充",
            TranslationMode.Ai => Models.Count == 0
                ? "AI 模式：尚未添加模型，请点击「管理模型」添加（OpenAI 兼容 / Anthropic）"
                : "AI 模式：按所选模型批量翻译，占位符自动校验重试",
            TranslationMode.Api => "API 模式：DeepL 风格翻译端点，tag_handling 保留富文本标记",
            TranslationMode.Agent => Models.Count == 0
                ? "Agent 模式：请先添加模型（管理模型 → 添加 OpenAI 兼容 / Anthropic）"
                : "Agent 模式：LLM 经工具调用理解项目并按所选布局规划翻译，文件写入需审批（Fast 快译确定性回退可用）",
            _ => "",
        };
    }

    partial void OnIsBusyChanged(bool value)
    {
        CancelSyncCommand.NotifyCanExecuteChanged();
        ApproveSyncCommand.NotifyCanExecuteChanged();
        DiscardSyncCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>翻译条目（UI 列表项）</summary>
public sealed record TranslationEntry(string Text, bool IsTranslated);