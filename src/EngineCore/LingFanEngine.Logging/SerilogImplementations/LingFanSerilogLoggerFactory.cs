using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;

namespace LingFanEngine.Logging.SerilogImplementations;

/// <summary>
/// Serilog 适配器工厂（直接包装官方实现，避免重复造轮子）
/// </summary>
public sealed class LingFanSerilogLoggerFactory : ILoggerFactory
{
    private readonly SerilogLoggerFactory _factory;

    public LingFanSerilogLoggerFactory(Serilog.ILogger logger)
    {
        // 使用 Serilog 官方提供的工厂，避免自己实现
        _factory = new SerilogLoggerFactory(logger, dispose: true);
    }

    public void Dispose() => _factory.Dispose();

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
        => _factory.CreateLogger(categoryName);

    public void AddProvider(ILoggerProvider provider)
        => _factory.AddProvider(provider);
}