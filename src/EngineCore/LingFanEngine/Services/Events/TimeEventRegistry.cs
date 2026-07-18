using System.Collections.Concurrent;
using System.Text;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Events;

/// <summary>
/// DSL 全局时间事件注册表（Phase 63 新增）
/// <para>启动时预过滤 + 并行编译含 set_time_event 的 .story 文件，提取事件注册信息。</para>
/// <para>读档重注册时按 ID 查表，解决跨场景事件丢失问题。</para>
/// <para>restore_time_event 时按 ID 查回定义重新注册。</para>
/// <para>设计理念：时间事件生命周期——事件一旦注册即独立，场景只是挂载器（出生地）。</para>
/// <para>Phase 63 修复：通过 IEncryptedFileReader 读取文件，支持加密 .story 文件。</para>
/// </summary>
public class TimeEventRegistry : ITimeEventRegistry
{
    /// <summary>DSL 事件 ID → 注册信息 映射（线程安全）</summary>
    private readonly ConcurrentDictionary<string, TimeEventRegistration> _registrations = new();

    /// <summary>C# 声明式事件 ID 集合（区分 DSL 事件，用于 ResetGameState 后恢复）</summary>
    private readonly ConcurrentDictionary<string, TimeEventRegistration> _declarations = new();

    /// <summary>加密文件读取器（null=开发期无加密，直接读文件）</summary>
    private readonly IEncryptedFileReader? _fileReader;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="fileReader">加密文件读取器（可选，null 时直接读文件）</param>
    public TimeEventRegistry(IEncryptedFileReader? fileReader = null)
    {
        _fileReader = fileReader;
    }

    /// <summary>是否已完成初始化加载</summary>
    public bool IsLoaded { get; private set; }

    /// <inheritdoc/>
    public bool TryGetRegistration(string id, out TimeEventRegistration registration)
    {
        return _registrations.TryGetValue(id, out registration!);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// C# 声明式事件通过此方法注册到全局注册表。
    /// 可在 InitializeAsync 之前或之后调用——_registrations 是 ConcurrentDictionary，线程安全。
    /// 同时记录到 _declarations，用于 ResetGameState 后恢复。
    /// </remarks>
    public void RegisterDeclaration(TimeEventRegistration registration)
    {
        _registrations[registration.Id] = registration;
        _declarations[registration.Id] = registration;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<TimeEventRegistration> GetAllDeclarations() => _declarations.Values.ToArray();

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetAllIds() => _registrations.Keys.ToArray();

    /// <inheritdoc/>
    public async Task InitializeAsync(IStoryRegistry storyRegistry, IScriptEngine scriptEngine, CancellationToken cancellationToken = default)
    {
        if (IsLoaded) return;

        var storyFiles = storyRegistry.GetAllStoryFiles().ToList();
        if (storyFiles.Count == 0)
        {
            IsLoaded = true;
            return;
        }

        // 并行读取所有文件内容（通过 IEncryptedFileReader 支持加密文件）
        var fileContents = new ConcurrentBag<(string filePath, string content)>();
        await Parallel.ForEachAsync(storyFiles, cancellationToken, async (filePath, ct) =>
        {
            try
            {
                string? content;
                if (_fileReader != null)
                {
                    // Phase 63: 通过加密文件读取器读取（自动检测 LFEN 并解密）
                    content = await _fileReader.ReadAllTextAsync(filePath, ct);
                }
                else
                {
                    // 开发期无加密：直接读文件
                    if (!File.Exists(filePath)) return;
                    content = await File.ReadAllTextAsync(filePath, ct);
                }

                if (string.IsNullOrEmpty(content)) return;
                // 预过滤：只保留含 set_time_event 的文件（零误报，固定关键字）
                if (content.Contains("set_time_event"))
                {
                    fileContents.Add((filePath, content));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[TimeEventRegistry] 读取文件失败 [{filePath}]: {ex.Message}");
            }
        });

        // 并行编译命中的文件
        // 注意：LingFanDslEngine.Compile 不是线程安全的（_loopStack 是实例字段），
        // 必须用锁保护编译调用。文件读取已并行完成，编译串行化不影响整体性能（IO 是瓶颈）。
        // Phase 63 审计修复：异步上下文中使用 SemaphoreSlim 异步锁，避免阻塞线程池线程。
        var compiledCommands = new ConcurrentBag<List<SetTimeEventCommand>>();
        using var compileLock = new SemaphoreSlim(1, 1);
        await Parallel.ForEachAsync(fileContents, cancellationToken, async (item, ct) =>
        {
            try
            {
                ScriptResult result;
                await compileLock.WaitAsync(ct);
                try
                {
                    result = scriptEngine.Compile(item.content);
                }
                finally
                {
                    compileLock.Release();
                }

                if (!result.Success || result.Commands == null || result.Commands.Count == 0)
                    return;

                // 从编译结果提取 SetTimeEventCommand
                var timeEventCommands = result.Commands
                    .OfType<SetTimeEventCommand>()
                    .ToList();

                if (timeEventCommands.Count > 0)
                {
                    compiledCommands.Add(timeEventCommands);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[TimeEventRegistry] 编译文件失败 [{item.filePath}]: {ex.Message}");
            }
        });

        // 构建 ID → 注册信息 映射（同 ID 重复定义 → 字典覆盖，最后定义的赢——创作者责任）
        foreach (var commands in compiledCommands)
        {
            foreach (var cmd in commands)
            {
                var registration = cmd.ToRegistration();
                _registrations[cmd.Id] = registration;
            }
        }

        IsLoaded = true;
        System.Diagnostics.Debug.WriteLine(
            $"[TimeEventRegistry] 初始化完成：扫描 {storyFiles.Count} 个文件，" +
            $"预过滤 {fileContents.Count} 个，提取 {_registrations.Count} 个时间事件");
    }
}
