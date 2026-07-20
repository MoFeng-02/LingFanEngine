using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Abstractions.Interfaces.Logging;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Abstractions.Interfaces.Media;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Entry;
using LingFanEngine.Services.Logging;
using LingFanEngine.Services.Logging.Context;
using LingFanEngine.Services.Events;
using LingFanEngine.Services.Media;
using LingFanEngine.Services.Platform;
using LingFanEngine.Services.Resources;
using LingFanEngine.Services.Saves;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Services.Tweens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LingFanEngine.Abstractions.EngineOptions;

namespace LingFanEngine.Extensions;

/// <summary>
/// 引擎服务扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册引擎核心服务
    /// </summary>
    public static IServiceCollection AddLingFanEngine(this IServiceCollection services, Action<LingFanEngineOptions>? configure = null)
    {
        var options = new LingFanEngineOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        // 注册核心运行时
        services.AddSingleton<IJsonValueConverter, JsonValueConverter>();
        services.AddSingleton<IStateContainer>(sp =>
            new StateContainer(sp.GetRequiredService<IJsonValueConverter>()));
        services.AddSingleton<ICommandPipeline, CommandPipeline>();
        services.AddSingleton<IGameTimeService, GameTimeService>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<ITweenEngine>(sp => new TweenEngine(sp.GetRequiredService<IStateContainer>()));
        // 注册异步等待服务（订阅 IStateContainer.ValueChanged，供 DslExecutor/GameController 零延迟等待）
        services.AddSingleton<IAsyncWaitService>(sp => new AsyncWaitService(sp.GetRequiredService<IStateContainer>()));
        // RouterService 已移除，场景切换基于 SceneRegistry + SceneStack
        services.AddSingleton<IEventScheduler, EventScheduler>();
        // Phase 63：DSL 全局时间事件注册表（启动时扫描 .story 文件提取时间事件）
        // 注入 IEncryptedFileReader 支持加密 .story 文件读取
        services.AddSingleton<ITimeEventRegistry>(sp => new TimeEventRegistry(
            sp.GetService<IEncryptedFileReader>()));
        services.AddSingleton<HotReloadWatcher>();
        services.AddSingleton<ResourceManager>();
        services.AddSingleton<InputService>();
        services.AddSingleton<IReadOnlyGameState, GameState>();
        services.AddSingleton<ISceneRegistry, SceneRegistry>();
        // SceneLoader 已移除
        services.AddSingleton<ITransitionEngine, TransitionEngine>();
        services.AddSingleton<ISceneStack, SceneStack>();
        services.AddSingleton<IScriptEngine, LingFanDslEngine>();

        // 注册故事加载管线（StoryLoader 必须在 StoryRegistry 之前）
        // Phase 50：注入 IEncryptedFileReader 支持 .story 文件即解即用
        services.AddSingleton<IStoryLoader>(sp => new StoryLoader(
            sp.GetRequiredService<IScriptEngine>(),
            sp.GetRequiredService<ICommandPipeline>(),
            sp.GetRequiredService<IStateContainer>(),
            sp.GetRequiredService<ISceneRegistry>(),
            sp.GetService<IDslExecutor>(),
            sp.GetService<PackLoader>(),
            sp.GetService<IEncryptedFileReader>(),
            sp.GetService<IEngineLoggerFactory>()));
        services.AddSingleton<IStoryRegistry>(sp =>
        {
            var sceneRegistry = sp.GetRequiredService<ISceneRegistry>();
            var dslEngine = sp.GetRequiredService<IScriptEngine>();
            var storyLoader = sp.GetRequiredService<IStoryLoader>();
            var opts = sp.GetRequiredService<LingFanEngineOptions>();
            return new StoryRegistry(sceneRegistry, dslEngine, (StoryLoader)storyLoader, opts.StoriesDirectory,
                sp.GetService<IEncryptedFileReader>());
        });
        services.AddSingleton<IDslExecutor>(sp => new DslExecutor(
            sp.GetRequiredService<IStateContainer>(),
            sp.GetRequiredService<ICommandPipeline>(),
            sp.GetRequiredService<LingFanEngineOptions>(),
            sp.GetRequiredService<IAsyncWaitService>(),
            sp.GetService<IEventScheduler>(),
            sp.GetService<IEngineLoggerFactory>()));
        services.AddSingleton<IPreferencesService, PreferencesService>();
        services.AddSingleton<IGameController>(sp => new GameController(
            sp.GetRequiredService<ICommandPipeline>(),
            sp.GetRequiredService<IStateContainer>(),
            sp.GetRequiredService<LingFanEngineOptions>(),
            sp.GetRequiredService<IAsyncWaitService>(),
            sp.GetService<IEventScheduler>(),
            sp.GetService<IEngineLoggerFactory>()));
        services.AddSingleton<IEventAggregator, EventAggregator>();
        services.AddSingleton<II18nService>(sp => new I18nService(
            sp.GetRequiredService<IStateContainer>(),
            sp.GetService<IEncryptedFileReader>()));

        // 注册日志服务——日志上下文访问器 + 日志工厂（游戏可替换为自定义实现）
        services.TryAddSingleton<IEngineLogContextAccessor, EngineLogContextAccessor>();
        services.TryAddSingleton<IEngineLoggerFactory>(sp =>
        {
            var options = sp.GetRequiredService<LingFanEngineOptions>();
            var contextAccessor = sp.GetRequiredService<IEngineLogContextAccessor>();
            return new EngineLoggerFactory(options, contextAccessor, sp);
        });
        // 默认引擎日志（分类 "Engine"）——供未指定分类的服务使用
        services.TryAddSingleton<IEngineLogger>(sp =>
            sp.GetRequiredService<IEngineLoggerFactory>().Create("Engine"));

        // 注册资源包加载器
        services.AddSingleton<PackLoader>();
        // Phase 50：即解即用加密——密钥提供者（游戏层可覆盖）+ 加密文件读取器
        services.TryAddSingleton<IEncryptionKeyProvider, NullEncryptionKeyProvider>();
        services.AddSingleton<IEncryptedFileReader>(sp => new EncryptedFileReader(
            sp.GetService<IEncryptionKeyProvider>()));
        services.AddSingleton<IAudioManager>(sp => new AudioManager(
            sp.GetRequiredService<ICommandPipeline>(),
            sp.GetRequiredService<IStateContainer>(),
            CreateAudioPlayerFactory(),
            sp.GetService<IEncryptedFileReader>()
        ));
        services.AddSingleton<IVideoManager>(sp => new VideoManager(
            sp.GetRequiredService<IStateContainer>()
        ));
        services.AddSingleton<LanguageService>();
        services.AddSingleton<IConfigService, JsonConfigService>();

        // 注册新服务
        services.AddSingleton<IGalleryService, GalleryService>();
        services.AddSingleton<IDebugConsoleService, DebugConsoleService>();

        // 注册默认命令处理器（IDefaultCommandHandler 标记接口，AOT 安全手动注册）
        services.AddSingleton<IDefaultCommandHandler, SetVariableHandler>();
        services.AddSingleton<IDefaultCommandHandler, ShowDialogHandler>();
        services.AddSingleton<IDefaultCommandHandler, ExtendDialogHandler>();
        services.AddSingleton<IDefaultCommandHandler, PlayBgmHandler>();
        services.AddSingleton<IDefaultCommandHandler, PlaySeHandler>();
        services.AddSingleton<IDefaultCommandHandler, PlayVoiceHandler>();
        services.AddSingleton<IDefaultCommandHandler, BgmQueueHandler>();
        services.AddSingleton<IDefaultCommandHandler, PlayAmbientHandler>();
        services.AddSingleton<IDefaultCommandHandler, StopAmbientHandler>();
        services.AddSingleton<IDefaultCommandHandler, StopVoiceHandler>();
        services.AddSingleton<IDefaultCommandHandler, TransitionHandler>();
        services.AddSingleton<IDefaultCommandHandler, AnimateHandler>();
        services.AddSingleton<IDefaultCommandHandler, ShowHideHandler>();
        services.AddSingleton<IDefaultCommandHandler, NavigateHandler>();
        services.AddSingleton<IDefaultCommandHandler, SaveLoadHandler>();
        services.AddSingleton<IDefaultCommandHandler, InputHandler>();
        services.AddSingleton<IDefaultCommandHandler, WaitHandler>();
        services.AddSingleton<IDefaultCommandHandler, HardPauseHandler>();
        services.AddSingleton<IDefaultCommandHandler, BackHandler>();
        services.AddSingleton<IDefaultCommandHandler, ForwardHandler>();
        services.AddSingleton<IDefaultCommandHandler, RollbackHandler>();
        services.AddSingleton<IDefaultCommandHandler, RollforwardHandler>();
        services.AddSingleton<IDefaultCommandHandler, RollbackToHandler>();
        services.AddSingleton<IDefaultCommandHandler, SceneHandler>();
        services.AddSingleton<IDefaultCommandHandler, NavToLabelHandler>();
        services.AddSingleton<IDefaultCommandHandler, BuildSceneHandler>();
        services.AddSingleton<IDefaultCommandHandler, ClearStackHandler>();
        services.AddSingleton<IDefaultCommandHandler, ResetGameStateHandler>();
        services.AddSingleton<IDefaultCommandHandler, MergeDefinesHandler>();
        services.AddSingleton<IDefaultCommandHandler, ShakeHandler>();
        services.AddSingleton<IDefaultCommandHandler, ToggleSkipHandler>();
        services.AddSingleton<IDefaultCommandHandler, ToggleAutoHandler>();
        services.AddSingleton<IDefaultCommandHandler, UnlockGalleryHandler>();
        services.AddSingleton<IDefaultCommandHandler, DebugLogHandler>();
        services.AddSingleton<IDefaultCommandHandler, NvlHandler>();

        // 注册视频命令处理器
        services.AddSingleton<IDefaultCommandHandler, PlayVideoHandler>();
        services.AddSingleton<IDefaultCommandHandler, StopVideoHandler>();
        services.AddSingleton<IDefaultCommandHandler, PauseVideoHandler>();
        services.AddSingleton<IDefaultCommandHandler, ResumeVideoHandler>();
        services.AddSingleton<IDefaultCommandHandler, SeekVideoHandler>();
        services.AddSingleton<IDefaultCommandHandler, CutsceneHandler>();

        // Phase 38: 时间事件与通知处理器
        services.AddSingleton<IDefaultCommandHandler, TimeEventHandler>();
        services.AddSingleton<IDefaultCommandHandler, TimePauseHandler>();
        services.AddSingleton<IDefaultCommandHandler, TimeResumeHandler>();
        services.AddSingleton<IDefaultCommandHandler, SkipTimeHandler>();
        services.AddSingleton<IDefaultCommandHandler, SetTimeEventHandler>();
        services.AddSingleton<IDefaultCommandHandler, UnregisterTimeEventHandler>();
        services.AddSingleton<IDefaultCommandHandler, RestoreTimeEventHandler>();
        services.AddSingleton<IDefaultCommandHandler, NotifyHandler>();

        // Phase 44-47: DSL 2.0 新命令处理器
        services.AddSingleton<IDefaultCommandHandler, ArrayPushHandler>();
        services.AddSingleton<IDefaultCommandHandler, ArrayPopHandler>();
        services.AddSingleton<IDefaultCommandHandler, DictSetHandler>();
        services.AddSingleton<IDefaultCommandHandler, SpriteHandler>();
        services.AddSingleton<IDefaultCommandHandler, BgSwitchHandler>();
        services.AddSingleton<IDefaultCommandHandler, Live2DHandler>();
        services.AddSingleton<IDefaultCommandHandler, AchievementUnlockHandler>();
        services.AddSingleton<IDefaultCommandHandler, ChapterUnlockHandler>();
        services.AddSingleton<IDefaultCommandHandler, SaveDeleteHandler>();

        // 注册子服务（从 GameLoop 拆分）
        services.AddSingleton<IStateInitializer, StateInitializer>();
        services.AddSingleton<IAnimationService, AnimationService>();
        services.AddSingleton<IShakeService, ShakeService>();
        services.AddSingleton<IPlaybackService, PlaybackService>();
        // 对话框工厂（游戏可注册自定义实现替换内置 DialogBox）
        services.TryAddSingleton<Views.IDialogBoxFactory, Views.DefaultDialogBoxFactory>();
        // Phase 65: 对话框模板注册表（UI 层注册具体模板，SceneView 按名消费）
        services.TryAddSingleton<Views.IDialogTemplateRegistry, Views.DialogTemplateRegistry>();

        // Phase 32: SceneView 模块化——注册子模块接口（Phase 50：VideoPresenter 注入 IEncryptedFileReader）
        services.AddSingleton<Views.IControlFactory, Views.ControlFactory>();
        services.AddSingleton<Views.IInteractionBinder>(sp => new Views.InteractionBinder(
            sp.GetRequiredService<IStateContainer>(),
            sp.GetRequiredService<ICommandPipeline>(),
            sp.GetService<ICommandService>()));
        services.AddSingleton<Views.IOverlayRenderer>(sp => new Views.OverlayRenderer(
        sp.GetRequiredService<IStateContainer>(),
        sp.GetService<II18nService>()));
        services.AddSingleton<Views.IVideoPresenter>(sp => new Views.VideoPresenter(
            sp.GetRequiredService<IStateContainer>(),
            sp.GetService<IEncryptedFileReader>()));
        services.AddSingleton<Views.IAnimationApplier, Views.AnimationApplier>();
        services.AddSingleton<ISaveDataService>(sp => new SaveDataService(
            sp.GetRequiredService<IStateContainer>(),
            sp.GetRequiredService<IJsonValueConverter>(),
            sp.GetRequiredService<LingFanEngineOptions>(),
            sp.GetService<ISaveService>(),
            sp.GetService<ISceneStack>(),
            sp.GetService<ISceneRegistry>(),
            sp.GetService<IStoryRegistry>(),
            sp.GetService<IDslExecutor>(),
            sp.GetService<IEventScheduler>(),
            sp.GetService<ITimeEventRegistry>()));

        // 注册 GameLoop 并应用目标帧率
        services.AddSingleton<IGameLoop>(sp =>
        {
            var pipeline = sp.GetRequiredService<ICommandPipeline>();
            var state = sp.GetRequiredService<IStateContainer>();
            var time = sp.GetRequiredService<IGameTimeService>();
            var dispatcher = sp.GetRequiredService<ICommandDispatcher>();
            var tween = sp.GetRequiredService<ITweenEngine>();
            var jsonConverter = sp.GetRequiredService<IJsonValueConverter>();
            var defaultHandlers = sp.GetServices<IDefaultCommandHandler>();
            var save = sp.GetService<ISaveService>();
            var sceneReg = sp.GetService<ISceneRegistry>();
            var sceneStack = sp.GetService<ISceneStack>();
            var storyReg = sp.GetService<IStoryRegistry>();
            var dslExec = sp.GetService<IDslExecutor>();
            var transition = sp.GetService<ITransitionEngine>();
            var audio = sp.GetService<IAudioManager>();
            var video = sp.GetService<IVideoManager>();
            var eventScheduler = sp.GetService<IEventScheduler>();
            var timeEventRegistry = sp.GetService<ITimeEventRegistry>();
            var i18n = sp.GetService<II18nService>();
            var uiDispatcher = sp.GetService<IUIThreadDispatcher>();
            var loop = new GameLoop(
            pipeline, state, time,
            dispatcher, tween, jsonConverter, defaultHandlers,
            sp.GetRequiredService<IStateInitializer>(),
            sp.GetRequiredService<IAnimationService>(),
            sp.GetRequiredService<IShakeService>(),
            sp.GetRequiredService<IPlaybackService>(),
            sp.GetRequiredService<ISaveDataService>(),
            save, sceneReg, options,
            sceneStack, storyReg, dslExec, transition, audio, video, eventScheduler, timeEventRegistry, i18n, uiDispatcher);
            loop.TargetFps = options.GetTargetFps();
            // StoryRegistry 扫描副作用（需在 GameLoop 构造后执行）
            storyReg?.Scan();
            // DSL 执行器关联 StoryRegistry
            if (dslExec != null && storyReg != null)
                dslExec.SetStoryRegistry(storyReg);
            // Phase 63: 初始化全局时间事件注册表——扫描并编译所有含 set_time_event 的 .story 文件
            // 必须在 storyReg.Scan() 之后执行（依赖已扫描的文件列表）
            if (timeEventRegistry != null && storyReg != null && options.EnableTimeSystem)
            {
                var scriptEngine = sp.GetService<IScriptEngine>();
                if (scriptEngine != null)
                {
                    try
                    {
                        // DI 工厂在启动期执行，无 SynchronizationContext，GetAwaiter().GetResult() 不会死锁
                        timeEventRegistry.InitializeAsync(storyReg, scriptEngine)
                            .GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ServiceCollectionExtensions] TimeEventRegistry 初始化失败: {ex.Message}");
                    }
                }
            }
            return loop;
        });

        // 使用选项中的配置注册默认服务
        services.AddSingleton<ISaveService>(sp =>
        {
            var opts = sp.GetRequiredService<LingFanEngineOptions>();
            return new BinarySaveService(opts.SaveDirectory);
        });
        services.AddSingleton<IMediaDataService>(sp =>
        {
            var opts = sp.GetRequiredService<LingFanEngineOptions>();
            return new MediaDataService(opts.MediaDirectory);
        });
        services.AddSingleton<ILive2DDataService>(sp =>
        {
            var opts = sp.GetRequiredService<LingFanEngineOptions>();
            return new Live2DDataService(opts.Live2DDirectory);
        });

        // 注册 UI 线程调度器
        services.AddSingleton<IUIThreadDispatcher, LingFanEngine.Views.AvaloniaUIThreadDispatcher>();

        // 注册资源访问器（Phase 50：注入 IEncryptedFileReader 自动检测 LFEN 加密）
        services.AddSingleton<IAssetAccessor>(sp => new LingFanEngine.Views.AvaloniaAssetAccessor(
            sp.GetService<IEncryptedFileReader>()));

        // 注册故事热重载服务
        services.AddSingleton<StoryHotReloadService>();

        return services;
    }

    /// <summary>
    /// 注册默认命令服务（CommandService）
    /// <para>需先调用 AddLingFanEngine</para>
    /// </summary>
    public static IServiceCollection AddDefaultCommandService(this IServiceCollection services)
    {
        services.AddSingleton<ICommandService, LingFanEngine.Services.Entry.CommandService>();
        return services;
    }

    /// <summary>
    /// 注册默认存档服务（BinarySaveService）
    /// </summary>
    public static IServiceCollection AddDefaultSaveService(this IServiceCollection services, string saveDirectory)
    {
        services.AddSingleton<ISaveService>(sp => new BinarySaveService(saveDirectory));
        return services;
    }

    /// <summary>
    /// 注册默认存档服务（BinarySaveService，使用指定密钥）
    /// </summary>
    public static IServiceCollection AddDefaultSaveService(this IServiceCollection services, string saveDirectory, byte[] key, byte[] iv)
    {
        services.AddSingleton<ISaveService>(sp => new BinarySaveService(saveDirectory, key, iv));
        return services;
    }

    /// <summary>
    /// 注册默认媒体数据服务
    /// </summary>
    public static IServiceCollection AddDefaultMediaDataService(this IServiceCollection services, string mediaBasePath = "Media")
    {
        services.AddSingleton<IMediaDataService>(sp => new MediaDataService(mediaBasePath));
        return services;
    }

    /// <summary>
    /// 注册默认 Live2D 数据服务
    /// </summary>
    public static IServiceCollection AddDefaultLive2DDataService(this IServiceCollection services, string modelBasePath = "Live2D")
    {
        services.AddSingleton<ILive2DDataService>(sp => new Live2DDataService(modelBasePath));
        return services;
    }

    /// <summary>
    /// 创建音频播放器工厂——根据平台选择 LibVLC 或 NullAsyncAudioPlayer
    /// <para>LibVLC 不可用时（Browser/WASM 或初始化失败）降级为空操作播放器。</para>
    /// </summary>
    private static Func<IAudioPlayer> CreateAudioPlayerFactory()
    {
        // 确保 Core 已初始化
        LibVlcInitializer.InitializeCore();

        if (!LibVlcInitializer.IsAvailable)
        {
            // Browser/WASM 或 LibVLC 初始化失败——降级为空操作
            return () => new NullAsyncAudioPlayer();
        }

        return () =>
        {
            var libVLC = LibVlcInitializer.GetLibVLC();
            if (libVLC == null)
                return new NullAsyncAudioPlayer();
            return new LibVlcAudioPlayer(libVLC);
        };
    }
}