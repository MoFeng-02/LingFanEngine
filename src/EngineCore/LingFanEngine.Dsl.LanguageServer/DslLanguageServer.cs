using System.Collections.Concurrent;
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
    /// <summary>uri/localPath → 源文本（用于 LSP 偏移↔行列互转）。并发字典：后台索引与文档打开/变更可同时安全访问。</summary>
    private readonly ConcurrentDictionary<string, string> _docs = new();
    private bool _exit;
    /// <summary>initialize 解析出的工作区根路径（file:// 经 LocalPath 还原）；用于自动跨文件索引。</summary>
    private string? _rootPath;
    private Channel<Request>? _channel;

    /// <summary>写类方法集合（改变文档/索引/诊断状态，须沿写链串行）。其余为读类（可并发，但须晚于前序写执行）。</summary>
    private static readonly HashSet<string> s_writeMethods = new()
    {
        "initialize", "initialized", "shutdown", "exit",
        "textDocument/didOpen", "textDocument/didChange", "workspace/didChangeWatchedFiles",
    };

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

                // 在「入队（流顺序）」这一刻确定写屏障：保证本读请求排在它之前入队的所有写之后执行，
                // 即使该读被某 worker 抢先分派——从而修复并发 worker 下读早于前序写（didOpen/initialized）执行的竞态。
                if (s_writeMethods.Contains(req.Method))
                {
                    var handler = ResolveHandler(req.Method);
                    lock (_chainLock)
                    {
                        var prev = _writeChain;
                        // Task.Run 卸载到线程池，避免写 handler（如 initialized 的目录枚举、didOpen 的索引）阻塞 reader 线程。
                        _writeChain = Task.Run(() => ChainWrite(prev, handler, req));
                        req.ChainTask = _writeChain;
                    }
                }
                else
                {
                    lock (_chainLock) req.Barrier = _writeChain;
                }
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
        if (s_writeMethods.Contains(req.Method))
        {
            // 写链已在入队时构建；worker 仅等待其完成（handler 已在链内于写锁下执行）。
            return req.ChainTask ?? Task.CompletedTask;
        }
        var handler = ResolveHandler(req.Method);
        if (handler == null)
        {
            if (req.Id.HasValue)
                _conn.SendError(req.Id.Value, -32601, $"method not found: {req.Method}");
            return Task.CompletedTask;
        }
        return WithReadLock(handler, req);
    }

    /// <summary>方法 → handler 解析（与 dispatch 同源单一真相源）。写方法在入队时已据此构建写链。</summary>
    private Func<Request, Task> ResolveHandler(string method) => method switch
    {
        "initialize" => HandleInitialize,
        "initialized" => HandleInitialized,
        "shutdown" => HandleShutdown,
        "exit" => HandleExit,
        "textDocument/didOpen" => HandleDidOpen,
        "textDocument/didChange" => HandleDidChange,
        "workspace/didChangeWatchedFiles" => HandleWatchedFiles,
        "textDocument/hover" => HandleHover,
        "textDocument/definition" => HandleDefinition,
        "textDocument/references" => HandleReferences,
        "textDocument/completion" => HandleCompletion,
        "textDocument/foldingRange" => HandleFolding,
        "textDocument/formatting" => HandleFormatting,
        "textDocument/rangeFormatting" => HandleRangeFormatting,
        "textDocument/semanticTokens/full" => HandleSemanticTokens,
        _ => null!,
    };

    /// <summary>写串行化：沿 <see cref="_writeChain"/> 顺序执行，且持写锁独占服务/文档状态。</summary>
    private async Task ChainWrite(Task prev, Func<Request, Task> handler, Request req)
    {
        try { await prev; } catch { /* 前序写失败不影响后续 */ }
        _gate.EnterWriteLock();
        try { await handler(req); }
        finally { _gate.ExitWriteLock(); }
    }

    /// <summary>读并发：先等「本请求之前入队的所有写」完成（保证看到最新文档状态，且晚于前序写执行），
    /// 再持读锁执行（多个读可并发）。屏障在 <see cref="ReaderLoop"/> 入队时按流顺序快照，杜绝读早于前序写执行。</summary>
    private async Task WithReadLock(Func<Request, Task> handler, Request req)
    {
        if (req.Barrier != null) await req.Barrier;
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
        var before = _service.SnapshotDefinitions(path);
        _service.UpdateDocument(path, p.TextDocument.Text);
        PublishAffected(path, before);
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
            var beforeSnap = _service.SnapshotDefinitions(path);
            _service.UpdateDocument(path, updated, new Ls.DirtyRange(so, eo - so, newText.Length));
            PublishAffected(path, beforeSnap);
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
        var before = _service.SnapshotDefinitions(path);
        _service.UpdateDocument(path, fullText); // dirty=null → 整文重建（小文件开销可忽略，且零错）
        PublishAffected(path, before);
        return Task.CompletedTask;
    }

    private Task HandleWatchedFiles(Request req)
    {
        var p = Deserialize<DidChangeWatchedFilesParams>(req, LspJsonContext.Default.DidChangeWatchedFilesParams);
        if (p == null) return Task.CompletedTask;
        var resourceChanged = false;
        foreach (var ev in p.Changes)
        {
            var path = UriToPath(ev.Uri);
            var isStory = path.EndsWith(".story", StringComparison.OrdinalIgnoreCase);
            if (ev.Type == 3) // 3 = Deleted
            {
                _docs.TryRemove(path, out _);
                if (isStory)
                {
                    _service.RemoveDocument(path);
                    LogMessage(4, $"watched: 删除 {path}");
                }
                else
                {
                    // 资源文件删除：增量刷新资源索引（非资源类型在索引内被忽略）。
                    _service.RemoveResource(path);
                    resourceChanged = true;
                }
            }
            else if (isStory) // 1=Created / 2=Changed：外部编辑我们不知增量，整文重建
            {
                try
                {
                    var text = File.ReadAllText(path);
                    var before = _service.SnapshotDefinitions(path);
                    _docs[path] = text;
                    _service.UpdateDocument(path, text);
                    // 定向刷新：仅重发受本文件定义变化影响的文件（含自身）
                    foreach (var ap in _service.GetAffectedFilesByDefinitionChange(path, before))
                        if (_docs.ContainsKey(ap)) PublishDiagnostics(PathToUri(ap), ap);
                    LogMessage(4, $"watched: 重新索引 {(ev.Type == 1 ? "新增" : "变更")} {path}");
                }
                catch (Exception ex) { Log($"watched reindex skip {path}: {ex.Message}"); }
            }
            else
            {
                // 资源文件新增/变更：增量刷新资源索引（非资源类型在索引内被忽略，不会误建文档）。
                // 资源可用性影响「未找到资源」诊断，但无法用符号索引精确定向，故资源类变更仍重发全部打开文档（外部编辑，低频）。
                _service.UpdateResource(path);
                resourceChanged = true;
            }
        }
        // 仅当资源类文件变更（无法用符号索引定向）时才全量重发；.story 外部编辑已走定向刷新。
        if (resourceChanged) PublishAllDiagnostics();
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

    /// <summary>补全触发字符（标点类）：空格（行首语句后）、{（插值变量）、=（参数值枚举/布尔）、"（字符串开启即弹引用列表）、(（参数起点）、.（属性/路径联动）、:（say: 等联动）。
    /// 字母类「边打字边弹」由客户端 editor.quickSuggestions(other:on) 承载，无须在此列 a–z（否则会与 quickSuggestions 重复触发）。</summary>
    private static string[] CompletionTriggerCharacters()
    {
        return new[] { " ", "{", "=", "\"", "(", ".", ":" };
    }

    private Task HandleCompletion(Request req)
    {
        var p = Deserialize<TextDocumentPositionParams>(req, LspJsonContext.Default.TextDocumentPositionParams);
        if (p == null || !req.Id.HasValue) return Task.CompletedTask;
        var path = UriToPath(p.TextDocument.Uri);
        var src = SourceOf(path);
        var offset = PositionToOffset(src, p.Position);
        var items = _service.GetCompletion(path, offset);
        var arr = new CompletionItem[items.Count];
        for (var i = 0; i < arr.Length; i++)
        {
            var it = items[i];
            var ci = new CompletionItem
            {
                Label = it.DisplayText,
                InsertText = it.InsertText,
                Detail = it.Detail,
                Kind = MapCompletionKind(it.Kind),
            };
            // 含 / 或 _ 的候选（资源路径、命令名）：用精确替换范围覆盖客户端默认词边界，
            // 避免把已输入前缀重复拼回（"Audio/cri" 选 "Audio/x.mp3" → "Audio/Audio/x.mp3"）。
            if (it.ReplaceStart >= 0)
                ci.TextEdit = new TextEdit
                {
                    Range = MakeRange(src, it.ReplaceStart, offset - it.ReplaceStart),
                    NewText = it.InsertText,
                };
            arr[i] = ci;
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

    private Task HandleFormatting(Request req)
    {
        var p = Deserialize<DocumentFormattingParams>(req, LspJsonContext.Default.DocumentFormattingParams);
        if (p == null || !req.Id.HasValue) return Task.CompletedTask;
        var path = UriToPath(p.TextDocument.Uri);
        var src = SourceOf(path);
        if (src is null) { SendEmptyEdits(req); return Task.CompletedTask; }
        var insertSpaces = p.Options?.InsertSpaces != false;
        var formatted = _service.FormatDocument(path, p.Options?.TabSize, insertSpaces) ?? src;
        var edit = new TextEdit
        {
            Range = new Protocol.Range { Start = new Position { Line = 0, Character = 0 }, End = OffsetToPosition(src, src.Length) },
            NewText = formatted,
        };
        _conn.SendResult(req.Id.Value, new[] { edit }, LspJsonContext.Default.TextEditArray);
        return Task.CompletedTask;
    }

    private Task HandleRangeFormatting(Request req)
    {
        var p = Deserialize<DocumentRangeFormattingParams>(req, LspJsonContext.Default.DocumentRangeFormattingParams);
        if (p == null || !req.Id.HasValue) return Task.CompletedTask;
        var path = UriToPath(p.TextDocument.Uri);
        var src = SourceOf(path);
        if (src is null) { SendEmptyEdits(req); return Task.CompletedTask; }
        var startLine = p.Range.Start.Line;
        var endLine = p.Range.End.Line;
        if (p.Range.End.Character == 0) endLine--; // LSP 区间 end 行号独占；字符为 0 时上一行才是末包含行
        if (endLine < startLine) endLine = startLine;

        var insertSpaces = p.Options?.InsertSpaces != false;
        var formatted = _service.FormatRange(path, startLine, endLine, p.Options?.TabSize, insertSpaces) ?? src;

        // 取格式化结果中对应行的子串（含其换行），保证替换区间与原文逐行对齐、尾随换行不丢。
        var fStart = LineStartOffset(formatted, startLine);
        var fEnd = LineStartOffset(formatted, endLine + 1);
        var rangeText = formatted[fStart..fEnd];

        var startOff = LineStartOffset(src, startLine);
        var endOff = LineStartOffset(src, endLine + 1);
        var edit = new TextEdit
        {
            Range = new Protocol.Range { Start = OffsetToPosition(src, startOff), End = OffsetToPosition(src, endOff) },
            NewText = rangeText,
        };
        _conn.SendResult(req.Id.Value, new[] { edit }, LspJsonContext.Default.TextEditArray);
        return Task.CompletedTask;
    }

    private void SendEmptyEdits(Request req)
    {
        if (req.Id.HasValue)
            _conn.SendResult(req.Id.Value, System.Array.Empty<TextEdit>(), LspJsonContext.Default.TextEditArray);
    }

    /// <summary>返回第 <paramref name="line"/> 行（0-based）起始字符偏移；越界返回文本长度。</summary>
    private static int LineStartOffset(string src, int line)
    {
        if (line <= 0) return 0;
        var lf = 0;
        for (var i = 0; i < src.Length; i++)
        {
            if (src[i] == '\n')
            {
                lf++;
                if (lf == line) return i + 1;
            }
        }
        return src.Length;
    }

    private Task HandleSemanticTokens(Request req)
    {
        var p = Deserialize<SemanticTokensParams>(req, LspJsonContext.Default.SemanticTokensParams);
        if (p == null || !req.Id.HasValue) return Task.CompletedTask;
        var path = UriToPath(p.TextDocument.Uri);
        var src = SourceOf(path);
        var tokens = _service.GetSemanticTokens(path); // 偏移已升序（GetAllTokens 按行/词序产出），无需再排序
        var data = new List<int>(tokens.Count * 5);
        var prevLine = 0;
        var prevChar = 0;
        foreach (var t in tokens)
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

    /// <summary>构建时间戳（exe 文件最后写入时间）——用于在 trace 的 initialize 响应里直接辨认当前运行的二进制版本，避免「到底加载的是不是新 exe」的歧义。
    /// 单文件 AOT 下 <c>Assembly.Location</c> 恒为空（IL3000），改用 <c>AppContext.BaseDirectory</c>（红线 B17 规定的路径基准）拼接 exe 名。</summary>
    private static string LspBuildStamp()
    {
        try
        {
            var exePath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "LingFan.Dsl.LanguageServer.exe");
            if (System.IO.File.Exists(exePath))
                return "build-" + System.IO.File.GetLastWriteTime(exePath).ToString("yyyy-MM-dd_HH:mm:ss");
        }
        catch { }
        return "build-unknown";
    }

    private Task HandleInitialize(Request req)
    {
        if (!req.Id.HasValue) return Task.CompletedTask;
        var init = Deserialize<InitializeParams>(req, LspJsonContext.Default.InitializeParams);
        _rootPath = ResolveRoot(init);
        LogMessage(3, $"LSP 启动：{LspBuildStamp()}（pid={System.Environment.ProcessId}）");
        var caps = new ServerCapabilities
        {
            TextDocumentSync = 2, // 2 = Incremental（支持行级增量重索引）
            HoverProvider = true,
            DefinitionProvider = true,
            ReferencesProvider = true,
            FoldingRangeProvider = true,
            DocumentFormattingProvider = true,
            DocumentRangeFormattingProvider = true,
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
                        Filters =
                        [
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.story" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.png" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.jpg" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.jpeg" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.gif" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.webp" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.mp3" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.wav" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.ogg" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.mp4" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.webm" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.ttf" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.otf" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.woff" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.woff2" } },
                            new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.fnt" } },
                        ],
                    },
                },
            },
            Window = new WindowCapabilities { WorkDoneProgress = true },
        };
        var result = new InitializeResult
        {
            Capabilities = caps,
            ServerInfo = new ServerInfo { Name = "LingFan DSL Language Server", Version = LspBuildStamp() },
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
    /// 客户端发 <c>initialized</c> 后触发。重活（项目级 .story 索引 + 全资源树扫描）改为<b>后台执行</b>：
    /// 经服务层「构建新鲜实例→原子替换引用」（见 <see cref="Ls.DslLanguageService.IndexProject"/> / <see cref="Ls.DslLanguageService.ScanProject"/>），
    /// 不占用写链、不阻塞 <c>foldingRange</c>/<c>semanticTokens</c>/<c>diagnostics</c> 等读请求——
    /// 首次响应从 ~3.5s（等全树扫描）降至 ~百毫秒（仅当前文档解析）。
    /// </summary>
    private Task HandleInitialized(Request req)
    {
        if (_rootPath == null) return Task.CompletedTask;
        // 立即返回（写链随即释放），重活在独立线程池任务里跑，读请求无需等待它。
        _ = Task.Run(() => BuildProjectIndexAsync(_rootPath!));
        return Task.CompletedTask;
    }

    /// <summary>后台构建项目索引：枚举 .story + 全资源树扫描（含 C# 符号联动），完成后经服务层原子替换生效，
    /// 并补发一次全部打开文档的诊断，使资源/命令引用告警在扫描就绪后浮现。</summary>
    private void BuildProjectIndexAsync(string root)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!Directory.Exists(root))
            {
                Log($"workspace root NOT FOUND: {root}");
                LogMessage(1, $"未找到工作区根目录：{root}");
                return;
            }
            // 磁盘枚举在后台线池（IO 不占 LSP 写锁），索引写入走服务层原子替换，读请求全程不被阻塞。
            List<(string Path, string Text)>? files = null;
            try
            {
                files = new List<(string Path, string Text)>();
                foreach (var f in EnumerateStoryFiles(root))
                {
                    if (_docs.TryGetValue(f, out var mem)) { files.Add((f, mem)); continue; }
                    try { files.Add((f, File.ReadAllText(f))); }
                    catch (Exception ex) { Log($"index skip {f}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { Log($"workspace index failed: {ex.Message}"); }
            if (files is { Count: > 0 }) _service.IndexProject(files);
            // 资源联合索引：扫描项目根下全部资源文件（图片/音频/视频/字体）+ C# 符号，供资源路径补全/悬停/跳转/C# 联动。
            _service.ScanProject(root);
            sw.Stop();
            var count = files?.Count ?? 0;
            Log($"project index ready in {sw.ElapsedMilliseconds}ms (background): {count} .story + resources under {root}");
            LogMessage(3, $"已索引 {count} 个 .story 文件（后台 {sw.ElapsedMilliseconds}ms，{root}）");
            // 扫描就绪后重发诊断，使资源/命令引用告警浮现（_docs 为并发字典，枚举安全）
            try { PublishAllDiagnostics(); } catch (Exception ex) { Log($"republish diagnostics skip: {ex.Message}"); }
        }
        catch (Exception ex) { Log($"background index failed: {ex.Message}"); }
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

    /// <summary>重发所有已打开文档的诊断。本 DSL 的「未定义」是跨文件解析的（引用文件靠全局 _definitions 解析到别处的定义），
    /// 因此任一文件新增/删除 define 都会改变其它文件的诊断结果。若只在被编辑文件自身 didChange 后重发，
    /// 引用文件会残留过期诊断，而悬停/跳转是实时解析的 → 出现「诊断=未定义、悬停=已定义」的矛盾。
    /// 故每次文档变更后统一重发全部打开文档的诊断（story 文件小，开销可忽略）。</summary>
    private void PublishAllDiagnostics()
    {
        foreach (var path in _docs.Keys)
            PublishDiagnostics(PathToUri(path), path);
    }

    /// <summary>定向刷新：仅重发「定义状态发生变化的符号」所影响到的文件诊断。
    /// 依据现有跨文件索引 _references（符号→引用列表），由 changedNames 反查引用文件，性能损耗远低于全量重发。</summary>
    private void PublishAffected(string editedPath, HashSet<Ls.SymbolKey> before)
    {
        foreach (var p in _service.GetAffectedFilesByDefinitionChange(editedPath, before))
            if (_docs.ContainsKey(p)) PublishDiagnostics(PathToUri(p), p);
    }

    // ---- 工具 ----

    private T? Deserialize<T>(Request req, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) where T : class
        => req.Params is { } p ? p.Deserialize(typeInfo) : null;

    private string SourceOf(string path)
    {
        if (_docs.TryGetValue(path, out var s) && s.Length > 0) return s;
        // 回退到服务层规范文档源（工作区扫描建立的索引文档），确保仅被扫描、尚未 didOpen 的文件也能换算坐标。
        return _service.GetSource(path) ?? string.Empty;
    }

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
        "resource" => 17,  // File
        "command" => 3,    // Function
        "tag" => 14,       // Keyword
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
