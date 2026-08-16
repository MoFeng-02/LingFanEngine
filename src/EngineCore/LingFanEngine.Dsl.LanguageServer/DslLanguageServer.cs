using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Ls = LingFanEngine.Dsl.LanguageService;
using LingFanEngine.Dsl.LanguageServer.Protocol;

namespace LingFanEngine.Dsl.LanguageServer;

/// <summary>
/// DSL LSP 服务核心：把 LSP 请求映射到 <see cref="IDslLanguageService"/>（UI 无关、引擎侧）。
/// <para>多线程模型：reader 线程从 stdin 读消息入无界 Channel；N 个 worker 并发消费（N=min(核数,4)，至少 1）。
/// 所有对 <see cref="_service"/>/<see cref="_docs"/> 的访问经 <see cref="_gate"/>（读写锁）串行化：
/// 查询类走读锁（可并发），文档变更/索引类走写锁（独占）。写操作还经 <see cref="_writeChain"/> 顺序链，
/// 保证「写之后的读」一定看到该写结果。stdio 收发仍由 <see cref="JsonRpcConnection"/> 单写锁保护，AOT 安全（零反射、无 async void）。</para>
/// </summary>
internal sealed class DslLanguageServer
{
    private readonly Ls.IDslLanguageService _service;
    private readonly JsonRpcConnection _conn;
    private readonly ReaderWriterLockSlim _gate = new(LockRecursionPolicy.NoRecursion);
    private readonly object _chainLock = new();
    private Task _writeChain = Task.CompletedTask;
    private readonly int _workerCount = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
    /// <summary>uri/localPath → 源文本（用于 LSP 偏移↔行列互转）。</summary>
    private readonly Dictionary<string, string> _docs = new();
    private bool _exit;
    /// <summary>initialize 解析出的工作区根路径（file:// 经 LocalPath 还原）；用于自动跨文件索引。</summary>
    private string? _rootPath;
    private Channel<Request>? _channel;

    public DslLanguageServer(Ls.IDslLanguageService service, Stream input, Stream output)
    {
        _service = service;
        _conn = new JsonRpcConnection(input, output);
    }

    public void Run()
    {
        _channel = Channel.CreateUnbounded<Request>(new UnboundedChannelOptions { SingleReader = false });
        var readerTask = Task.Run(ReaderLoop);
        var workers = new Task[_workerCount];
        for (var i = 0; i < workers.Length; i++)
            workers[i] = Task.Run(WorkerLoop);
        Task.WaitAll(workers);
        try { readerTask.Wait(TimeSpan.FromMilliseconds(500)); } catch { /* ignore */ }
    }

    // ---- 线程模型 ----

    private void ReaderLoop()
    {
        try
        {
            while (true)
            {
                Request? req = null;
                try { req = _conn.ReadMessage(); }
                catch (EndOfStreamException) { break; }
                catch (Exception ex) { Log($"read error: {ex.Message}"); break; }
                if (req == null) break;
                _channel!.Writer.TryWrite(req);
            }
        }
        finally { _channel!.Writer.TryComplete(); }
    }

    private async Task WorkerLoop()
    {
        var reader = _channel!.Reader;
        try
        {
            await foreach (var req in reader.ReadAllAsync())
            {
                if (_exit) continue;
                try { await DispatchAsync(req); }
                catch (Exception ex) { Log($"dispatch error ({req.Method}): {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { /* drain */ }
    }

    private Task DispatchAsync(Request req)
    {
        switch (req.Method)
        {
            case "initialize":                        return WithWriteLockAsync(HandleInitialize, req);
            case "initialized":                       return WithWriteLockAsync(HandleInitialized, req);
            case "shutdown":                          return WithWriteLockAsync(HandleShutdown, req);
            case "exit":                              return WithWriteLockAsync(HandleExit, req);
            case "textDocument/didOpen":              return WithWriteLockAsync(HandleDidOpen, req);
            case "textDocument/didChange":            return WithWriteLockAsync(HandleDidChange, req);
            case "workspace/didChangeWatchedFiles":   return WithWriteLockAsync(HandleWatchedFiles, req);
            case "textDocument/hover":                return WithReadLock(HandleHover, req);
            case "textDocument/definition":           return WithReadLock(HandleDefinition, req);
            case "textDocument/references":            return WithReadLock(HandleReferences, req);
            case "textDocument/completion":           return WithReadLock(HandleCompletion, req);
            case "textDocument/foldingRange":         return WithReadLock(HandleFolding, req);
            case "textDocument/semanticTokens/full":  return WithReadLock(HandleSemanticTokens, req);
            default:
                if (req.Id.HasValue)
                    _conn.SendError(req.Id.Value, -32601, $"method not found: {req.Method}");
                return Task.CompletedTask;
        }
    }

    /// <summary>写串行化：沿 <see cref="_writeChain"/> 顺序执行，且持写锁独占服务/文档状态。</summary>
    private async Task WithWriteLockAsync(Func<Request, Task> handler, Request req)
    {
        Task prev;
        lock (_chainLock) { prev = _writeChain; _writeChain = ChainWrite(prev, handler, req); }
        await _writeChain;
    }

    private async Task ChainWrite(Task prev, Func<Request, Task> handler, Request req)
    {
        try { await prev; } catch { /* 前序写失败不影响后续 */ }
        _gate.EnterWriteLock();
        try { await handler(req); }
        finally { _gate.ExitWriteLock(); }
    }

    /// <summary>读并发：先等当前写链完成（保证看到最新写结果），再持读锁执行（多个读可并发）。</summary>
    private async Task WithReadLock(Func<Request, Task> handler, Request req)
    {
        Task wait;
        lock (_chainLock) wait = _writeChain;
        await wait;
        _gate.EnterReadLock();
        try { await handler(req); }
        finally { _gate.ExitReadLock(); }
    }

    // ---- 文档生命周期 ----

    private Task HandleDidOpen(Request req)
    {
        var p = Deserialize<DidOpenTextDocumentParams>(req, LspJsonContext.Default.DidOpenTextDocumentParams);
        if (p == null) return Task.CompletedTask;
        var path = UriToPath(p.TextDocument.Uri);
        _docs[path] = p.TextDocument.Text;
        _service.UpdateDocument(path, p.TextDocument.Text);
        PublishDiagnostics(p.TextDocument.Uri, path);
        return Task.CompletedTask;
    }

    private Task HandleDidChange(Request req)
    {
        var p = Deserialize<DidChangeTextDocumentParams>(req, LspJsonContext.Default.DidChangeTextDocumentParams);
        if (p == null) return Task.CompletedTask;
        var path = UriToPath(p.TextDocument.Uri);
        var changes = p.ContentChanges;
        if (changes.Length == 0) return Task.CompletedTask;

        // 单 range 变更 → 行级增量（O(变更) 而非 O(整文)）
        if (changes.Length == 1 && changes[0].Range is { } range)
        {
            var src = SourceOf(path);
            var so = PositionToOffset(src, range.Start);
            var eo = PositionToOffset(src, range.End);
            var newText = changes[0].Text;
            var updated = src.Substring(0, so) + newText + src.Substring(eo);
            _docs[path] = updated;
            _service.UpdateDocument(path, updated, new Ls.DirtyRange(so, eo - so, newText.Length));
            PublishDiagnostics(p.TextDocument.Uri, path);
            return Task.CompletedTask;
        }

        // 多变更 / 无 range：增量同步下每个 change 带独立 range，必须「顺序应用全部 change」
        // 拼装出完整新文本，再整文重建。旧实现误把末段 change.Text 当全文，会把文档截断成
        // 一段碎片，导致索引建立在错误文本上（移动 / 剪切粘贴 / 多光标编辑都会触发）。
        var fullText = SourceOf(path);
        foreach (var ch in changes)
        {
            if (ch.Range is { } r)
            {
                var so = PositionToOffset(fullText, r.Start);
                var eo = PositionToOffset(fullText, r.End);
                fullText = fullText.Substring(0, so) + ch.Text + fullText.Substring(eo);
            }
            else
            {
                fullText = ch.Text;
            }
        }
        _docs[path] = fullText;
        _service.UpdateDocument(path, fullText); // dirty=null → 整文重建（小文件开销可忽略，且零错）
        PublishDiagnostics(p.TextDocument.Uri, path);
        return Task.CompletedTask;
    }

    private Task HandleWatchedFiles(Request req)
    {
        var p = Deserialize<DidChangeWatchedFilesParams>(req, LspJsonContext.Default.DidChangeWatchedFilesParams);
        if (p == null) return Task.CompletedTask;
        foreach (var ev in p.Changes)
        {
            var path = UriToPath(ev.Uri);
            if (ev.Type == 3) // 3 = Deleted
            {
                _docs.Remove(path);
                _service.RemoveDocument(path);
                LogMessage(4, $"watched: 删除 {path}");
            }
            else // 1=Created / 2=Changed：外部编辑我们不知增量，整文重建
            {
                try
                {
                    var text = File.ReadAllText(path);
                    _docs[path] = text;
                    _service.UpdateDocument(path, text);
                    LogMessage(4, $"watched: 重新索引 {(ev.Type == 1 ? "新增" : "变更")} {path}");
                }
                catch (Exception ex) { Log($"watched reindex skip {path}: {ex.Message}"); }
            }
        }
        return Task.CompletedTask;
    }

    // ---- 查询（读锁内并发）----

    private Task HandleHover(Request req)
    {
        var p = Deserialize<TextDocumentPositionParams>(req, LspJsonContext.Default.TextDocumentPositionParams);
        if (p == null || !req.Id.HasValue) return Task.CompletedTask;
        var path = UriToPath(p.TextDocument.Uri);
        var src = SourceOf(path);
        var offset = PositionToOffset(src, p.Position);
        var info = _service.GetHover(path, offset);
        if (info == null) { _conn.SendResult(req.Id.Value, null, null); return Task.CompletedTask; }

        var value = string.IsNullOrEmpty(info.Detail) ? info.Title : $"**{info.Title}**\n\n{info.Detail}";
        var hover = new Hover { Contents = new MarkupContent { Kind = "markdown", Value = value } };
        if (info.RelatedLocation is { } loc)
            hover.Range = MakeRange(SourceOf(loc.FilePath), loc.Offset, loc.Length);
        _conn.SendResult(req.Id.Value, hover, LspJsonContext.Default.Hover);
        return Task.CompletedTask;
    }

    private Task HandleDefinition(Request req)
    {
        var p = Deserialize<TextDocumentPositionParams>(req, LspJsonContext.Default.TextDocumentPositionParams);
        if (p == null || !req.Id.HasValue) return Task.CompletedTask;
        var path = UriToPath(p.TextDocument.Uri);
        var offset = PositionToOffset(SourceOf(path), p.Position);
        var def = _service.GoToDefinition(path, offset);
        if (!def.Found || def.Location is null) { _conn.SendResult(req.Id.Value, null, null); return Task.CompletedTask; }
        var svcLoc = def.Location.Value;
        var loc = MakeLocation(svcLoc.FilePath, svcLoc.Offset, svcLoc.Length);
        _conn.SendResult(req.Id.Value, loc, LspJsonContext.Default.Location);
        return Task.CompletedTask;
    }

    private Task HandleReferences(Request req)
    {
        var p = Deserialize<ReferenceParams>(req, LspJsonContext.Default.ReferenceParams);
        if (p == null || !req.Id.HasValue) return Task.CompletedTask;
        var path = UriToPath(p.TextDocument.Uri);
        var offset = PositionToOffset(SourceOf(path), p.Position);
        var refs = _service.FindReferences(path, offset);
        var arr = new Location[refs.Locations.Count];
        for (var i = 0; i < arr.Length; i++)
        {
            var L = refs.Locations[i];
            arr[i] = MakeLocation(L.FilePath, L.Offset, L.Length);
        }
        _conn.SendResult(req.Id.Value, arr, LspJsonContext.Default.LocationArray);
        return Task.CompletedTask;
    }

    /// <summary>补全触发字符：空格（行首语句后）、{（插值变量）、=（参数值枚举/布尔）、a–z（输入关键字/变量名即弹上下文感知补全）。</summary>
    private static string[] CompletionTriggerCharacters()
    {
        var list = new List<string> { " ", "{", "=" };
        for (var c = 'a'; c <= 'z'; c++) list.Add(c.ToString());
        return list.ToArray();
    }

    private Task HandleCompletion(Request req)
    {
        var p = Deserialize<TextDocumentPositionParams>(req, LspJsonContext.Default.TextDocumentPositionParams);
        if (p == null || !req.Id.HasValue) return Task.CompletedTask;
        var path = UriToPath(p.TextDocument.Uri);
        var offset = PositionToOffset(SourceOf(path), p.Position);
        var items = _service.GetCompletion(path, offset);
        var arr = new CompletionItem[items.Count];
        for (var i = 0; i < arr.Length; i++)
        {
            var it = items[i];
            arr[i] = new CompletionItem
            {
                Label = it.DisplayText,
                InsertText = it.InsertText,
                Detail = it.Detail,
                Kind = MapCompletionKind(it.Kind),
            };
        }
        _conn.SendResult(req.Id.Value, arr, LspJsonContext.Default.CompletionItemArray);
        return Task.CompletedTask;
    }

    private Task HandleFolding(Request req)
    {
        var p = Deserialize<FoldingRangeParams>(req, LspJsonContext.Default.FoldingRangeParams);
        if (p == null || !req.Id.HasValue) return Task.CompletedTask;
        var path = UriToPath(p.TextDocument.Uri);
        var src = SourceOf(path);
        var regions = _service.GetFoldingRegions(path);
        var arr = new FoldingRange[regions.Count];
        for (var i = 0; i < arr.Length; i++)
        {
            var (s, e) = regions[i];
            arr[i] = new FoldingRange
            {
                StartLine = OffsetToPosition(src, s).Line,
                EndLine = OffsetToPosition(src, e).Line,
                Kind = "region",
            };
        }
        _conn.SendResult(req.Id.Value, arr, LspJsonContext.Default.FoldingRangeArray);
        return Task.CompletedTask;
    }

    private Task HandleSemanticTokens(Request req)
    {
        var p = Deserialize<SemanticTokensParams>(req, LspJsonContext.Default.SemanticTokensParams);
        if (p == null || !req.Id.HasValue) return Task.CompletedTask;
        var path = UriToPath(p.TextDocument.Uri);
        var src = SourceOf(path);
        var tokens = _service.GetSemanticTokens(path);
        var sorted = tokens.OrderBy(t => t.Offset).ToArray();
        var data = new List<int>(sorted.Length * 5);
        var prevLine = 0;
        var prevChar = 0;
        foreach (var t in sorted)
        {
            var start = OffsetToPosition(src, t.Offset);
            var deltaLine = start.Line - prevLine;
            var deltaChar = start.Line == prevLine ? start.Character - prevChar : start.Character;
            data.Add(deltaLine);
            data.Add(deltaChar);
            data.Add(t.Length);
            data.Add((int)t.Category); // tokenType 下标 == SemanticCategory 枚举值
            data.Add(0);               // tokenModifiers
            prevLine = start.Line;
            prevChar = start.Character;
        }
        _conn.SendResult(req.Id.Value, new SemanticTokens { Data = data.ToArray() }, LspJsonContext.Default.SemanticTokens);
        return Task.CompletedTask;
    }

    private Task HandleInitialize(Request req)
    {
        if (!req.Id.HasValue) return Task.CompletedTask;
        var init = Deserialize<InitializeParams>(req, LspJsonContext.Default.InitializeParams);
        _rootPath = ResolveRoot(init);
        var caps = new ServerCapabilities
        {
            TextDocumentSync = 2, // 2 = Incremental（支持行级增量重索引）
            HoverProvider = true,
            DefinitionProvider = true,
            ReferencesProvider = true,
            FoldingRangeProvider = true,
            CompletionProvider = new CompletionOptions { TriggerCharacters = CompletionTriggerCharacters() },
            SemanticTokensProvider = new SemanticTokensOptions
            {
                Legend = new SemanticTokensLegend
                {
                    TokenTypes = LspProtocol.SemanticTokenLegend,
                    TokenModifiers = [],
                },
                Full = true,
            },
            Workspace = new WorkspaceCapabilities
            {
                FileOperations = new FileOperations
                {
                    DidChangeWatchedFiles = new DidChangeWatchedFiles
                    {
                        Filters = [ new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.story" } } ],
                    },
                },
            },
            Window = new WindowCapabilities { WorkDoneProgress = true },
        };
        var result = new InitializeResult
        {
            Capabilities = caps,
            ServerInfo = new ServerInfo { Name = "LingFan DSL Language Server", Version = "1.0.0" },
        };
        _conn.SendResult(req.Id.Value, result, LspJsonContext.Default.InitializeResult);
        return Task.CompletedTask;
    }

    private Task HandleShutdown(Request req)
    {
        if (req.Id.HasValue) _conn.SendResult(req.Id.Value, null, null);
        return Task.CompletedTask;
    }

    private Task HandleExit(Request req)
    {
        _exit = true;
        _channel?.Writer.TryComplete();
        // LSP 规范：exit 通知后服务端应退出。标准客户端发 exit 后即期待进程终止。
        Environment.Exit(0);
        return Task.CompletedTask;
    }

    // ---- 自动跨文件索引（initialize 后由客户端 initialized 通知触发）----

    /// <summary>
    /// 客户端发 <c>initialized</c> 后触发：扫描工作区根下所有 <c>*.story</c> 建全量跨文件符号索引，
    /// 使全局跳转/引用/诊断在未逐个 didOpen 前即可用。已打开文档用内存文本，避免被磁盘旧内容覆盖。
    /// </summary>
    private Task HandleInitialized(Request req)
    {
        if (_rootPath == null) return Task.CompletedTask;
        if (!Directory.Exists(_rootPath))
        {
            Log($"workspace root NOT FOUND: {_rootPath}");
            LogMessage(1, $"未找到工作区根目录：{_rootPath}");
        }
        // 磁盘枚举在写锁外（IO 不占锁），仅 IndexProject 写入在写锁内（由调用方 WithWriteLockAsync 保证）
        List<(string Path, string Text)>? files = null;
        try
        {
            files = new List<(string Path, string Text)>();
            foreach (var f in EnumerateStoryFiles(_rootPath))
            {
                if (_docs.TryGetValue(f, out var mem)) { files.Add((f, mem)); continue; }
                try { files.Add((f, File.ReadAllText(f))); }
                catch (Exception ex) { Log($"index skip {f}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Log($"workspace index failed: {ex.Message}"); }
        if (files is { Count: > 0 }) _service.IndexProject(files);
        var count = files?.Count ?? 0;
        Log($"indexed {count} .story file(s) under {_rootPath}");
        LogMessage(3, $"已索引 {count} 个 .story 文件（{_rootPath}）");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 容错递归枚举工作区下所有 <c>.story</c>：逐目录独立 try-catch，单个无权限/超大目录失败不影响其它目录，
    /// 并跳过 <c>.git</c>/<c>bin</c>/<c>obj</c>/<c>node_modules</c> 等典型噪声目录以提速。
    /// <para>取代 <c>Directory.EnumerateFiles(AllDirectories)</c>——后者遇到首个访问受限目录会整体抛异常、中止枚举。</para>
    /// </summary>
    private static IEnumerable<string> EnumerateStoryFiles(string root)
    {
        var dirs = new Stack<string>();
        dirs.Push(root);
        while (dirs.Count > 0)
        {
            var dir = dirs.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.story"); }
            catch { continue; }
            foreach (var f in files) yield return f;

            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(dir); }
            catch { continue; }
            foreach (var d in subs)
            {
                var name = Path.GetFileName(d);
                if (name is ".git" or "bin" or "obj" or "node_modules" or "$tf" or ".vs") continue;
                dirs.Push(d);
            }
        }
    }

    private static string? ResolveRoot(InitializeParams? init)
    {
        if (init == null) return null;
        if (!string.IsNullOrEmpty(init.RootUri)) { var p = UriToPath(init.RootUri); if (!string.IsNullOrEmpty(p)) return p; }
        if (!string.IsNullOrEmpty(init.RootPath)) { var p = NormalizePath(init.RootPath); if (!string.IsNullOrEmpty(p)) return p; }
        if (init.WorkspaceFolders is { Length: > 0 } wf) { var p = UriToPath(wf[0].Uri); if (!string.IsNullOrEmpty(p)) return p; }
        return null;
    }

    // ---- 诊断推送（服务端→客户端通知）----

    private void PublishDiagnostics(string uri, string path)
    {
        Ls.DslAnalysisResult analysis;
        try { analysis = _service.GetDiagnosticsAsync(path).GetAwaiter().GetResult(); }
        catch { return; }

        var arr = new LspDiagnostic[analysis.Diagnostics.Count];
        for (var i = 0; i < arr.Length; i++)
        {
            var d = analysis.Diagnostics[i];
            arr[i] = new LspDiagnostic
            {
                Range = MakeRange(SourceOf(d.Location.FilePath), d.Location.Offset, d.Location.Length),
                Severity = MapSeverity(d.Severity),
                Source = "LingFan DSL",
                Message = d.Message,
            };
        }
        _conn.SendNotification(
            "textDocument/publishDiagnostics",
            new PublishDiagnosticsParams { Uri = uri, Diagnostics = arr },
            LspJsonContext.Default.PublishDiagnosticsParams);
    }

    // ---- 工具 ----

    private T? Deserialize<T>(Request req, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) where T : class
        => req.Params is { } p ? p.Deserialize(typeInfo) : null;

    private string SourceOf(string path) => _docs.TryGetValue(path, out var s) ? s : string.Empty;

    private Location MakeLocation(string path, int offset, int length)
    {
        var src = SourceOf(path);
        return new Location
        {
            Uri = PathToUri(path),
            Range = MakeRange(src, offset, length),
        };
    }

    private Protocol.Range MakeRange(string src, int offset, int length)
        => new()
        {
            Start = OffsetToPosition(src, offset),
            End = OffsetToPosition(src, offset + length),
        };

    private static int PositionToOffset(string text, Position pos)
    {
        var line = 0;
        var offset = 0;
        while (offset < text.Length && line < pos.Line)
        {
            if (text[offset] == '\n') line++;
            offset++;
        }
        return offset + pos.Character;
    }

    private static Position OffsetToPosition(string text, int offset)
    {
        var line = 0;
        var lineStart = 0;
        var limit = Math.Min(offset, text.Length);
        for (var i = 0; i < limit; i++)
            if (text[i] == '\n') { line++; lineStart = i + 1; }
        return new Position { Line = line, Character = offset - lineStart };
    }

    private static int MapCompletionKind(string kind) => kind switch
    {
        "statement" => 14, // Keyword
        "scene" => 7,      // Class
        "label" => 18,     // Reference
        "func" => 3,       // Function
        "character" => 7,  // Class
        "variable" => 6,   // Variable
        "parameter" => 10, // Property
        _ => 1,            // Text
    };

    private static int MapSeverity(Ls.DiagnosticSeverity severity) => severity switch
    {
        Ls.DiagnosticSeverity.Error => 1,
        Ls.DiagnosticSeverity.Warning => 2,
        Ls.DiagnosticSeverity.Info => 3,
        _ => 4, // Hint
    };

    /// <summary>修正 Windows 上非法的「前导斜杠 + 盘符」形式路径（如 <c>/e:/x</c> → <c>e:/x</c>）。</summary>
    /// <remarks>
    /// <c>file://e:/x</c>（双斜杠）这类 URI 经 <see cref="Uri.LocalPath"/> 会反序列化成 <c>/e:/x</c>，
    /// 而 Windows 的 <c>Directory</c> API 无法识别该形式（前导斜杠导致路径失效、Exists 返回 false）。
    /// 正常 <c>file:///e:/x</c>（三斜杠）才能得到 <c>e:\x</c>。此处统一兜底剥掉前导斜杠。
    /// </remarks>
    private static string NormalizePath(string p)
    {
        if (string.IsNullOrEmpty(p)) return p;
        if (p.Length > 2 && p[0] == '/' && char.IsLetter(p[1]) && p[2] == ':')
            return p.Substring(1);
        return p;
    }

    private static string UriToPath(string uri)
    {
        try
        {
            var local = new Uri(uri).LocalPath;
            return NormalizePath(local);
        }
        catch { return NormalizePath(uri); }
    }

    private static string PathToUri(string path)
    {
        try { return new Uri(path).AbsoluteUri; }
        catch { return path; }
    }

    private static void Log(string message)
    {
        try { Console.Error.WriteLine($"[LingFanLsp] {message}"); } catch { /* ignore */ }
    }

    /// <summary>向客户端推送 window/logMessage 进度/诊断通知（不影响协议主流程）。</summary>
    private void LogMessage(int type, string message)
    {
        try
        {
            _conn.SendNotification("window/logMessage",
                new LogMessageParams { Type = type, Message = message },
                LspJsonContext.Default.LogMessageParams);
        }
        catch { /* 通知失败不应影响主流程 */ }
    }
}
