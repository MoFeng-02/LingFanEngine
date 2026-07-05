using System.Runtime.InteropServices;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Entities.Scenes;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Dlc;

namespace LingFanEngine.Services.Dlc;

/// <summary>
/// 原生插件加载器
/// <para>加载 DLC 中的原生库（.dll / .so / .dylib），调用 InitializePlugin 入口函数。</para>
/// <para>使用 NativeLibrary.Load 路径，NativeAOT 完全兼容。</para>
/// </summary>
public class PluginLoader
{
    /// <summary>
    /// 已加载的插件句柄（用于卸载）
    /// </summary>
    private readonly List<nint> _loadedHandles = [];

    /// <summary>
    /// 插件 API 回调集合——主程序提供给 DLC 的函数表
    /// </summary>
    public sealed class PluginApi
    {
        /// <summary>注册路由（场景）</summary>
        public required RegisterRouteDelegate RegisterRoute { get; init; }

        /// <summary>注册时间事件</summary>
        public required RegisterEventDelegate RegisterEvent { get; init; }

        /// <summary>获取只读游戏状态</summary>
        public required GetGameStateDelegate GetGameState { get; init; }

        /// <summary>日志输出</summary>
        public required LogDelegate Log { get; init; }
    }

    /// <summary>
    /// 根据 RID 返回原生库文件扩展名
    /// </summary>
    private static string GetNativeLibraryExtension()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ".dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return ".so";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return ".dylib";
        return ".dll";
    }

    /// <summary>
    /// 获取当前运行时的 RID 子目录名
    /// </summary>
    private static string GetRidSubdirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.Arm64 => "win-arm64",
                Architecture.X86 => "win-x86",
                _ => "win-x64"
            };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                _ => "linux-x64"
            };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => "osx-arm64"
            };
        return "win-x64";
    }

    /// <summary>
    /// 解析原生库的实际路径
    /// <para>manifest 中 NativeLibraryPath 可以是：</para>
    /// <para>1. 绝对路径 → 直接使用</para>
    /// <para>2. 相对路径（如 "plugins/module"）→ 在 DLC 解压目录下查找 {rid}/module{ext}</para>
    /// <para>3. 带扩展名的相对路径 → 先尝试原路径，再尝试 {rid}/ 路径</para>
    /// </summary>
    /// <param name="manifestLibraryPath">manifest 中声明的路径</param>
    /// <param name="dlcExtractPath">DLC 解压根目录</param>
    /// <returns>解析后的绝对路径，找不到返回 null</returns>
    public static string? ResolveNativeLibraryPath(string? manifestLibraryPath, string dlcExtractPath)
    {
        if (string.IsNullOrWhiteSpace(manifestLibraryPath))
            return null;

        // 绝对路径直接使用
        if (Path.IsPathRooted(manifestLibraryPath) && File.Exists(manifestLibraryPath))
            return manifestLibraryPath;

        var ext = GetNativeLibraryExtension();
        var rid = GetRidSubdirectory();

        // 去掉已有扩展名，统一处理
        var baseName = Path.GetFileNameWithoutExtension(manifestLibraryPath);
        var dirPart = Path.GetDirectoryName(manifestLibraryPath) ?? string.Empty;

        // 尝试 {extractPath}/{dirPart}/{rid}/{baseName}{ext}
        var ridPath = Path.Combine(dlcExtractPath, dirPart, rid, baseName + ext);
        if (File.Exists(ridPath))
            return Path.GetFullPath(ridPath);

        // 尝试 {extractPath}/{dirPart}/{baseName}{ext}（平台无关路径）
        var flatPath = Path.Combine(dlcExtractPath, dirPart, baseName + ext);
        if (File.Exists(flatPath))
            return Path.GetFullPath(flatPath);

        // 尝试原始路径
        var rawPath = Path.Combine(dlcExtractPath, manifestLibraryPath);
        if (File.Exists(rawPath))
            return Path.GetFullPath(rawPath);

        return null;
    }

    /// <summary>
    /// 尝试加载 DLC 的原生插件
    /// <para>从 manifest 读取 NativeLibraryPath，按 RID 选择原生库，加载后调用 InitializePlugin。</para>
    /// </summary>
    /// <param name="nativeLibraryPath">原生库的绝对路径</param>
    /// <param name="api">主程序提供的 API 函数表</param>
    /// <param name="entryPointName">入口函数名（默认 "InitializePlugin"）</param>
    /// <returns>加载成功返回 true</returns>
    public bool TryLoadPlugin(string nativeLibraryPath, PluginApi api, string entryPointName = "InitializePlugin")
    {
        if (string.IsNullOrEmpty(nativeLibraryPath) || !File.Exists(nativeLibraryPath))
            return false;

        try
        {
            // NativeLibrary.Load 在 NativeAOT 下完全可用
            var handle = NativeLibrary.Load(nativeLibraryPath);

            // 查找 InitializePlugin 入口
            if (NativeLibrary.TryGetExport(handle, entryPointName, out var exportPtr))
            {
                // 通过函数指针调用（AOT 安全：指针→委托，无反射）
                var initPlugin = Marshal.GetDelegateForFunctionPointer<InitializePluginDelegate>(exportPtr);
                initPlugin(
                    api.RegisterRoute,
                    api.RegisterEvent,
                    api.GetGameState,
                    api.Log);
                _loadedHandles.Add(handle);
                return true;
            }

            // 未找到入口函数，释放句柄
            NativeLibrary.Free(handle);
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginLoader] Failed to load plugin '{nativeLibraryPath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 卸载所有已加载的插件
    /// </summary>
    public void UnloadAll()
    {
        foreach (var handle in _loadedHandles)
        {
            try { NativeLibrary.Free(handle); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PluginLoader] Unload failed: {ex.Message}"); }
        }
        _loadedHandles.Clear();
    }
}
