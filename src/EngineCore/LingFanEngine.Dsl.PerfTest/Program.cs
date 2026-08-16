using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LingFanEngine.Dsl.LanguageService;

namespace LingFanEngine.Dsl.PerfTest;

/// <summary>
/// M4 大文件压测：程序化生成数万行 DSL，测量各语言服务操作的耗时与 GC 压力。
/// <para>用法：dotnet run --project src/EngineCore/LingFanEngine.Dsl.PerfTest -- --lines 50000 --files 10 --iter 5</para>
/// <para>仅做 build（AI 红线：不 run）；本程序交用户本机执行以采集 profiling 数据。</para>
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var lines = GetArg(args, "--lines", 50_000);
        var files = GetArg(args, "--files", 10);
        var iter = GetArg(args, "--iter", 5);

        Console.WriteLine($"=== M4 大文件压测 === lines={lines} files={files} iter={iter}");
        Console.WriteLine($"dotnet={Environment.Version}  proc={Environment.ProcessorCount}");

        var text = Generate(lines);
        var defs = GenerateWithDefs(lines);

        var service = new DslLanguageService();
        service.UpdateDocument("big.story", text);
        service.UpdateDocument("defs.story", defs);

        long gc0 = GC.GetTotalMemory(true);

        Measure("open/首次索引 (new service + UpdateDocument)", iter, () =>
        {
            var svc = new DslLanguageService();
            svc.UpdateDocument("big.story", text);
        });

        Measure("整文重建 (UpdateDocument 无 dirty)", iter, () => service.UpdateDocument("big.story", text));

        // 行级增量：在文件末尾追加一行（oldLength=0, newLength=新行长度）—— 验证 M4 增量红利
        var append = "\nsay \"perf-appended-line\"";
        var so = text.Length;
        Measure("行级增量 (UpdateDocument + DirtyRange 末尾追加)", iter, () =>
            service.UpdateDocument("big.story", text + append, new DirtyRange(so, 0, append.Length)));

        Measure("GetSemanticTokens", iter, () => { var _ = service.GetSemanticTokens("big.story"); });
        Measure("GetFoldingRegions", iter, () => { var _ = service.GetFoldingRegions("big.story"); });
        Measure("GetDiagnosticsAsync", iter, () =>
        {
            var t = service.GetDiagnosticsAsync("big.story");
            t.GetAwaiter().GetResult();
        });

        var defOffset = OffsetOf(defs, "jump begin", "begin");
        Measure("GoToDefinition (label 引用处)", iter, () => { var _ = service.GoToDefinition("defs.story", defOffset); });
        Measure("FindReferences (label 引用处)", iter, () => { var _ = service.FindReferences("defs.story", defOffset); });

        // 多文件跨文件索引（P1 自动索引的等价路径）
        var proj = new List<(string Path, string Text)>();
        var per = Math.Max(1, lines / Math.Max(1, files));
        for (var i = 0; i < files; i++) proj.Add(($"proj_{i}.story", Generate(per)));
        Measure("IndexProject (跨文件批量)", iter, () => service.IndexProject(proj));

        long gc1 = GC.GetTotalMemory(false);
        Console.WriteLine($"托管堆增长: {(gc1 - gc0) / 1024 / 1024} MB");
        Console.WriteLine("=== 压测结束 ===");
        return 0;
    }

    private static void Measure(string name, int iter, Action action)
    {
        // 预热一次，避免 JIT 抖动影响首测
        action();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iter; i++) action();
        sw.Stop();
        var avg = sw.Elapsed.TotalMilliseconds / iter;
        Console.WriteLine($"  {name,-46} 平均 {avg,9:F2} ms  (×{iter})");
    }

    /// <summary>生成 N 行 DSL：每行一个 say，每 1000 行一个 scene 块起点（供折叠/结构）。</summary>
    private static string Generate(int n)
    {
        var sb = new System.Text.StringBuilder(n * 16);
        var scene = 0;
        for (var i = 0; i < n; i++)
        {
            if (i % 1000 == 0) { scene++; sb.Append("scene Scene_").Append(scene).Append('\n'); }
            sb.Append("    say \"perf line ").Append(i).Append("\"\n");
        }
        return sb.ToString();
    }

    /// <summary>生成含 label 定义 + jump 引用的文本（供定义跳转/引用压测）。</summary>
    private static string GenerateWithDefs(int n)
    {
        var sb = new System.Text.StringBuilder(n * 24);
        sb.Append("scene Main\n");
        var seg = Math.Max(1, n / 4);
        for (var i = 0; i < seg; i++)
        {
            sb.Append("    label begin\n");
            sb.Append("    say \"a ").Append(i).Append("\"\n");
            sb.Append("    jump begin\n");
            sb.Append("    say \"b ").Append(i).Append("\"\n");
        }
        return sb.ToString();
    }

    /// <summary>在 haystack 中找到 line 里 token 的字符偏移。</summary>
    private static int OffsetOf(string haystack, string line, string token)
    {
        var li = haystack.IndexOf(line, StringComparison.Ordinal);
        if (li < 0) return 0;
        var ti = haystack.IndexOf(token, li, StringComparison.Ordinal);
        return ti < 0 ? li : ti;
    }

    private static int GetArg(string[] args, string key, int fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], key, StringComparison.Ordinal)) return int.Parse(args[i + 1]);
        return fallback;
    }
}
