using System;
using LingFanEngine.Dsl.LanguageService;

namespace LingFanEngine.Dsl.LanguageServer;

/// <summary>
/// LSP 服务进程入口：把标准输入/输出接成 JSON-RPC 通道，委妥 <see cref="DslLanguageService"/>。
/// <para>经 <c>dotnet publish -c Release -r &lt;rid&gt; /p:PublishAot=true</c> 产出原生二进制，可被 VS Code / 任意 LSP 客户端以 stdio 方式拉起。</para>
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // [PROBE] 进程启动探针：记录 Main 入口墙钟时间，用于定位「VS Code 拉起 LSP 慢」是 spawn 慢还是计算慢。
        System.Console.Error.WriteLine($"[PROBE] main-start {DateTime.UtcNow:HH:mm:ss.fff}");
        // 标准流直接接管；不依赖任何反射式通用宿主，保证 NativeAOT 兼容。
        var input = Console.OpenStandardInput();
        var output = Console.OpenStandardOutput();
        var service = new DslLanguageService();
        var server = new DslLanguageServer(service, input, output);
        server.Run();
        return 0;
    }
}
