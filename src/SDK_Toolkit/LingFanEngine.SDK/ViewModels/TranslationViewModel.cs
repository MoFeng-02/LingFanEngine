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
using LingFanEngine.SDK.I18n;
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
                    StatusMessage = SdkLocalizer.Loc("St_NeedProjectX");
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
            StatusMessage = SdkLocalizer.Loc("St_NeedProject");
            return;
        }

        IsBusy = true;
        StatusMessage = SdkLocalizer.Loc("Tr_Scanning");
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
            StatusMessage = SdkLocalizer.Loc("Tr_Scanned", ScannedCount, TranslatedCount, UntranslatedCount);
        }
        catch (Exception ex)
        {
            StatusMessage = SdkLocalizer.Loc("Tr_ScanFail", ex.Message);
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
            StatusMessage = SdkLocalizer.Loc("St_NeedProject");
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
                TranslationMode.Api => SdkLocalizer.Loc("Tr_ApiNeedCfg"),
                _ => SdkLocalizer.Loc("Tr_NoMode"),
            };
            return;
        }

        await RunPrepareAsync(translator, Mode switch
        {
            TranslationMode.Manual => SdkLocalizer.Loc("Tr_PrepManual"),
            TranslationMode.Ai => SdkLocalizer.Loc("Tr_PrepAi"),
            TranslationMode.Api => SdkLocalizer.Loc("Tr_PrepApi"),
            _ => SdkLocalizer.Loc("Tr_PrepSync"),
        }, isGenerate: false);
    }

    private bool CanSync() => !IsBusy && ProjectDir != null;

    /// <summary>生成模式文件：按所选布局用占位构建对应格式的翻译文件（不动用外部 AI），走 diff 审批。</summary>
    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task GenerateModeFilesAsync()
    {
        if (ProjectDir == null)
        {
            StatusMessage = SdkLocalizer.Loc("St_NeedProject");
            return;
        }
        await RunPrepareAsync(new ManualTranslator(), SdkLocalizer.Loc("Tr_GenBusy", LayoutName()), isGenerate: true);
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

        var progress = new Progress<TranslationProgress>(p => OnUiThread(() =>
        {
            Progress = p.Total > 0 ? p.Completed * 100.0 / p.Total : 0;
            // 优先显示批量/批次状态（如"正在请求翻译/正在接收响应…"）；为空则回退到 x/y 进度
            StatusMessage = string.IsNullOrWhiteSpace(p.CurrentText)
                ? SdkLocalizer.Loc("Tr_Translating", p.Completed, p.Total)
                : p.CurrentText;
        }));

        try
        {
            var result = await _translationService.PrepareSyncAsync(
                ProjectDir!, TargetLang, Layout, translator, progress, ForceRetranslate, SourceLang, _cts.Token);

            _pendingEdits = result.PendingEdits is { Count: > 0 } ? result.PendingEdits : null;
            PreviewText = result.PreviewText;
            HasPending = _pendingEdits is { Count: > 0 };
            IsBusy = false;

            var forceNote = ForceRetranslate ? SdkLocalizer.Loc("Tr_ForceNote") : "";
            var layoutName = LayoutName();
            StatusMessage = _pendingEdits is { Count: > 0 }
                ? isGenerate
                    ? SdkLocalizer.Loc("Tr_GenManual", _pendingEdits.Count, layoutName, forceNote, result.Added, result.Kept)
                    : Mode switch
                    {
                        TranslationMode.Manual => SdkLocalizer.Loc("Tr_GenOther", _pendingEdits.Count, forceNote, result.Added, result.Kept, layoutName),
                        _ => SdkLocalizer.Loc("Tr_GenAi", _pendingEdits.Count, forceNote, result.Translated, result.Failed, result.Added, result.Kept, layoutName),
                    }
                : SdkLocalizer.Loc("Tr_NoChange", result.Kept, layoutName);
            StatusMessage += FormatUsage((translator as LlmTranslator)?.Usage);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = SdkLocalizer.Loc("Tr_Cancelled");
        }
        catch (Exception ex)
        {
            StatusMessage = SdkLocalizer.Loc("Tr_OpFail", SdkLocalizer.Loc(isGenerate ? "Tr_GenAction" : "Tr_SyncAction"), ex.Message);
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

        StatusMessage = SdkLocalizer.Loc("Tr_AgentPlanning");
        var progress = new Progress<TranslationProgress>(p => OnUiThread(() =>
        {
            Progress = p.Total > 0 ? p.Completed * 100.0 / p.Total : 0;
            StatusMessage = string.IsNullOrWhiteSpace(p.CurrentText)
                ? SdkLocalizer.Loc("Tr_AgentTranslating", p.Completed, p.Total)
                : p.CurrentText;
        }));

        try
        {
            var client = _modelClientFactory.Create(model);
            if (client == null)
            {
                StatusMessage = SdkLocalizer.Loc("Tr_AgentNoClient");
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
                ? SdkLocalizer.Loc("Tr_AgentDone", toolCount, _pendingEdits.Count, ar.FinalText)
                : SdkLocalizer.Loc("Tr_AgentEmpty", toolCount, ar.FinalText);
            StatusMessage += FormatUsage(ar.Usage);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = SdkLocalizer.Loc("Tr_Cancelled");
        }
        catch (Exception ex)
        {
            StatusMessage = SdkLocalizer.Loc("Tr_AgentFail", ex.Message);
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
        TranslationLayout.Mirrored => SdkLocalizer.Loc("Tr_LayoutMirrored"),
        TranslationLayout.SingleFile => SdkLocalizer.Loc("Tr_LayoutSingle"),
        _ => SdkLocalizer.Loc("Tr_LayoutFlat"),
    };

    /// <summary>把任意更新封送到 UI 线程（后台线程安全）；已在 UI 线程则直接执行。</summary>
    private static void OnUiThread(Action action)
    {
        var dispatcher = Avalonia.Threading.Dispatcher.UIThread;
        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Post(action);
    }

    /// <summary>把 Agent 分步状态安全写到 UI 线程（回调可能运行在后台线程）。</summary>
    private void PostStatus(string s) => OnUiThread(() => StatusMessage = s);

    /// <summary>格式化用量（无用量返回空串，避免噪音）</summary>
    private static string FormatUsage(UsageStats? u)
    {
        if (u == null || (u.InputTokens == 0 && u.OutputTokens == 0 && u.RequestCount <= 1))
            return "";
        var i = u.InputTokens.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        var o = u.OutputTokens.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        return SdkLocalizer.Loc("Tr_Usage", i, o, u.RequestCount);
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
            StatusMessage = SdkLocalizer.Loc("Tr_Applied", applied, dir);
        }
        catch (Exception ex)
        {
            StatusMessage = SdkLocalizer.Loc("Tr_ApplyFail", ex.Message);
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
        StatusMessage = SdkLocalizer.Loc("Tr_Discarded");
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
            return SdkLocalizer.Loc("Tr_NoModels");
        if (string.IsNullOrEmpty(SelectedModelId))
            return SdkLocalizer.Loc("Tr_NoModelSelected");
        var model = _modelService.GetById(SelectedModelId);
        if (model == null)
            return SdkLocalizer.Loc("Tr_ModelGone");
        if (string.IsNullOrWhiteSpace(model.ModelId))
            return SdkLocalizer.Loc("Tr_ModelNoId");
        if (string.IsNullOrWhiteSpace(model.ApiKey) && !TranslatorFactory.IsLocal(model.BaseUrl))
            return SdkLocalizer.Loc("Tr_ModelNoKey", model.DisplayOrId);
        return SdkLocalizer.Loc("Tr_ModelBadCfg");
    }

    partial void OnModeChanged(TranslationMode value)
    {
        StatusMessage = value switch
        {
            TranslationMode.Manual => SdkLocalizer.Loc("Tr_ModeDescManual"),
            TranslationMode.Ai => Models.Count == 0
                ? SdkLocalizer.Loc("Tr_ModeDescAiNoModel")
                : SdkLocalizer.Loc("Tr_ModeDescAi"),
            TranslationMode.Api => SdkLocalizer.Loc("Tr_ModeDescApi"),
            TranslationMode.Agent => Models.Count == 0
                ? SdkLocalizer.Loc("Tr_ModeDescAgentNoModel")
                : SdkLocalizer.Loc("Tr_ModeDescAgent"),
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