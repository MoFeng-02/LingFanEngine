namespace LingFanEngine.Dsl.ProjectIndex;

/// <summary>项目级容错递归枚举（与 LSP 的 EnumerateStoryFiles 同源思路）：逐目录 try-catch，
/// 跳过 .git/bin/obj/node_modules/$tf/.vs/.workbuddy 等噪声目录；用于资源/C# 索引的全量扫描。</summary>
internal static class ProjectScanner
{
    private static readonly HashSet<string> s_skipDirs = new(StringComparer.Ordinal)
    {
        ".git", "bin", "obj", "node_modules", "$tf", ".vs", ".workbuddy", "GeneratedKeys",
    };

    public static bool ShouldSkip(string dirName) => s_skipDirs.Contains(dirName);

    public static IEnumerable<string> Enumerate(string root, string searchPattern)
    {
        var dirs = new Stack<string>();
        dirs.Push(root);
        while (dirs.Count > 0)
        {
            var dir = dirs.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, searchPattern); }
            catch { continue; }
            foreach (var f in files) yield return f;

            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(dir); }
            catch { continue; }
            foreach (var d in subs)
            {
                if (ShouldSkip(Path.GetFileName(d))) continue;
                dirs.Push(d);
            }
        }
    }
}
