using LingFanEngine.Abstractions.Entities.Scenes;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Dlc;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Events;

namespace LingFanEngine.Services.Dlc;

/// <summary>
/// DLC 加载协调器
/// <para>编排 DLC 加载流程：扫描 → 解压 → 注册场景/事件 → 加载原生插件。</para>
/// <para>启动时调用 LoadAll() 完成全部 DLC 初始化。</para>
/// </summary>
public class DlcLoader
{
    private readonly DlcScanner _scanner;
    private readonly PluginLoader _pluginLoader;
    private readonly ISceneRegistry _sceneRegistry;
    private readonly EventScheduler _eventScheduler;
    private readonly IReadOnlyGameState _gameState;

    /// <summary>
    /// 已加载的 DLC 包列表
    /// </summary>
    private readonly List<DlcScanner.DlcPackage> _loadedPackages = [];

    /// <summary>
    /// 构造函数
    /// </summary>
    public DlcLoader(
        DlcScanner scanner,
        PluginLoader pluginLoader,
        ISceneRegistry sceneRegistry,
        EventScheduler eventScheduler,
        IReadOnlyGameState gameState)
    {
        _scanner = scanner;
        _pluginLoader = pluginLoader;
        _sceneRegistry = sceneRegistry;
        _eventScheduler = eventScheduler;
        _gameState = gameState;
    }

    /// <summary>
    /// 加载所有 DLC 包
    /// <para>扫描 Mods 目录，注册场景和事件，加载原生插件。</para>
    /// </summary>
    /// <returns>成功加载的 DLC 数量</returns>
    public int LoadAll()
    {
        var packages = _scanner.Scan();
        var count = 0;

        foreach (var package in packages)
        {
            try
            {
                LoadPackage(package);
                count++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DlcLoader] Failed to load DLC '{package.Manifest.Id}': {ex.Message}");
            }
        }

        if (count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[DlcLoader] Successfully loaded {count} DLC package(s).");
        }

        return count;
    }

    /// <summary>
    /// 加载单个 DLC 包
    /// </summary>
    private void LoadPackage(DlcScanner.DlcPackage package)
    {
        var manifest = package.Manifest;

        // 1. 注册场景
        if (manifest.Scenes.Count > 0)
        {
            foreach (var scene in manifest.Scenes)
            {
                _sceneRegistry.RegisterScene(scene.SceneName, scene);
            }
            System.Diagnostics.Debug.WriteLine($"[DlcLoader] Registered {manifest.Scenes.Count} scene(s) from DLC '{manifest.Id}'.");
        }

        // 2. 注册时间事件
        if (manifest.Events.Count > 0)
        {
            _eventScheduler.RegisterEvents(manifest.Events);
            System.Diagnostics.Debug.WriteLine($"[DlcLoader] Registered {manifest.Events.Count} event(s) from DLC '{manifest.Id}'.");
        }

        // 3. 加载原生插件（如果有）
        if (!string.IsNullOrWhiteSpace(manifest.NativeLibraryPath))
        {
            var resolvedPath = PluginLoader.ResolveNativeLibraryPath(
                manifest.NativeLibraryPath,
                package.ExtractPath);

            if (resolvedPath != null)
            {
                var api = new PluginLoader.PluginApi
                {
                    RegisterRoute = CreateRegisterRouteCallback(),
                    RegisterEvent = CreateRegisterEventCallback(),
                    GetGameState = () => _gameState,
                    Log = msg => System.Diagnostics.Debug.WriteLine($"[DLC:{manifest.Id}] {msg}")
                };

                var success = _pluginLoader.TryLoadPlugin(resolvedPath, api, manifest.EntryPoint);
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"[DlcLoader] Loaded native plugin for DLC '{manifest.Id}' from '{resolvedPath}'.");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[DlcLoader] Native plugin not loaded for DLC '{manifest.Id}' (no entry point found).");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[DlcLoader] Native library not found for DLC '{manifest.Id}' (path: '{manifest.NativeLibraryPath}').");
            }
        }

        _loadedPackages.Add(package);
    }

    /// <summary>
    /// 创建注册路由回调
    /// </summary>
    private RegisterRouteDelegate CreateRegisterRouteCallback()
    {
        return (path, scene) =>
        {
            _sceneRegistry.RegisterScene(path, scene);
            System.Diagnostics.Debug.WriteLine($"[DlcLoader] DLC registered route: {path}");
        };
    }

    /// <summary>
    /// 创建注册事件回调
    /// </summary>
    private RegisterEventDelegate CreateRegisterEventCallback()
    {
        return evt =>
        {
            _eventScheduler.RegisterEvent(evt);
            System.Diagnostics.Debug.WriteLine($"[DlcLoader] DLC registered event: {evt.Description ?? evt.TargetPath ?? "unnamed"}");
        };
    }

    /// <summary>
    /// 获取已加载的 DLC 包列表
    /// </summary>
    public IReadOnlyList<DlcScanner.DlcPackage> LoadedPackages => _loadedPackages;
}
