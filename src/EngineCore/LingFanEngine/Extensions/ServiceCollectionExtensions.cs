using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Abstractions.Interfaces.Media;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Dlc;
using LingFanEngine.Services.Entry;
using LingFanEngine.Services.Events;
using LingFanEngine.Services.Media;
using LingFanEngine.Services.Platform;
using LingFanEngine.Services.Resources;
using LingFanEngine.Services.Saves;
using LingFanEngine.Services.Scripting;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<IStateContainer, StateContainer>();
        services.AddSingleton<ICommandPipeline, CommandPipeline>();
        services.AddSingleton<IGameTimeService, GameTimeService>();
        // RouterService 已移除，场景切换基于 SceneRegistry + SceneStack
        services.AddSingleton<EventScheduler>();
        // DlcLoader 已移除
        services.AddSingleton(sp => new DlcScanner(sp.GetRequiredService<LingFanEngineOptions>().ModsDirectory));
        services.AddSingleton<PluginLoader>();
        services.AddSingleton<DlcLoader>();
        services.AddSingleton<HotReloadWatcher>();
        services.AddSingleton<ResourceManager>();
        services.AddSingleton<InputService>();
        services.AddSingleton<IReadOnlyGameState, GameState>();
        services.AddSingleton<ISceneRegistry, SceneRegistry>();
        // SceneLoader 已移除
        services.AddSingleton<TransitionEngine>();
        services.AddSingleton<SceneStack>();
        services.AddSingleton<IScriptEngine, LingFanDslEngine>();

        // 注册故事加载管线（StoryLoader 必须在 StoryRegistry 之前）
        services.AddSingleton<StoryLoader>();
        services.AddSingleton(sp =>
        {
            var sceneRegistry = sp.GetRequiredService<ISceneRegistry>();
            var dslEngine = sp.GetRequiredService<IScriptEngine>();
            var storyLoader = sp.GetRequiredService<StoryLoader>();
            return new StoryRegistry(sceneRegistry, dslEngine, storyLoader);
        });
        services.AddSingleton<DslExecutor>();

        // 注册资源包加载器
        services.AddSingleton<PackLoader>();
        services.AddSingleton<AudioManager>(sp => new AudioManager(
            sp.GetRequiredService<ICommandPipeline>(),
            sp.GetRequiredService<IStateContainer>()
        ));
        services.AddSingleton<LanguageService>();
        services.AddSingleton<I18nService>();
        services.AddSingleton<IConfigService, JsonConfigService>();

        // 注册新服务
        services.AddSingleton<IGalleryService, GalleryService>();
        services.AddSingleton<IDebugConsoleService, DebugConsoleService>();

        // 注册 GameLoop 并应用目标帧率
        services.AddSingleton<IGameLoop>(sp =>
        {
            var pipeline = sp.GetRequiredService<ICommandPipeline>();
            var state = sp.GetRequiredService<IStateContainer>();
            var time = sp.GetRequiredService<IGameTimeService>();
            var save = sp.GetService<ISaveService>();
            var sceneReg = sp.GetService<ISceneRegistry>();
            var loop = new GameLoop(pipeline, state, time, save, sceneReg, options);
            loop.TargetFps = options.GetTargetFps();
            // 注入过渡引擎，由 GameLoop 每帧驱动过渡更新
            var transition = sp.GetRequiredService<TransitionEngine>();
            loop.SetTransitionEngine(transition);
            // 注入音频管理器
            var audio = sp.GetRequiredService<AudioManager>();
            loop.SetAudioManager(audio);
            // 注入场景堆栈，由 GameLoop NavigateCommand 时写入
            var sceneStack = sp.GetRequiredService<SceneStack>();
            loop.SetSceneStack(sceneStack);
            // 注入 StoryRegistry（场景懒加载 + 全局 label 索引）
            var storyReg = sp.GetRequiredService<StoryRegistry>();
            loop.SetStoryRegistry(storyReg);
            storyReg.Scan();
            // 注入 DSL 执行器，由 GameLoop 每帧推进命令执行
            var dslExecutor = sp.GetRequiredService<DslExecutor>();
            dslExecutor.SetStoryRegistry(storyReg);
            loop.SetDslExecutor(dslExecutor);
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
}