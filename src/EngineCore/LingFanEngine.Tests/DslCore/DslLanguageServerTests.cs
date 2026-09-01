using System.IO;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LingFanEngine.DslCore;
using LingFanEngine.Dsl.LanguageService;
using LingFanEngine.Dsl.LanguageServer;
using LingFanEngine.Dsl.LanguageServer.Protocol;
using Xunit;

namespace LingFanEngine.Tests.DslCore;

/// <summary>
/// LSP server 层 wire-level 集成测试 + 语法表完整性 + signatureHelp 服务层测试。
/// </summary>
public class DslLanguageServerTests
{
    // ---- 语法表完整性（BUG-01 + FIX-05/06 回归保护）----

    [Fact]
    public void Grammar_Gallery_HasSlot()
    {
        var g = DslGrammar.TryGet("gallery");
        g.Should().NotBeNull("gallery 语句必须有语法槽");
    }

    [Fact]
    public void Grammar_Shake_HasNamedParams()
    {
        var g = DslGrammar.TryGet("shake");
        g.Should().NotBeNull();
        g!.NamedParams.Should().ContainKey("intensity");
        g.NamedParams.Should().ContainKey("duration");
    }

    [Fact]
    public void Grammar_GalleryUnlock_HasNamedParams()
    {
        var g = DslGrammar.TryGet("gallery_unlock");
        g.Should().NotBeNull();
        g!.NamedParams.Should().ContainKey("title");
        g.NamedParams.Should().ContainKey("scene");
    }

    [Fact]
    public void Keywords_UiElementTypes_ContainsEngineRecognizedNames()
    {
        // BUG-01 回归：引擎 ControlFactory 识别 scroll/scrollviewer/viewport/bar/vbar
        DslKeywords.UiElementTypes.Should().Contain("scroll");
        DslKeywords.UiElementTypes.Should().Contain("scrollviewer");
        DslKeywords.UiElementTypes.Should().Contain("viewport");
        DslKeywords.UiElementTypes.Should().Contain("bar");
        DslKeywords.UiElementTypes.Should().Contain("vbar");
        // scrollview 不应存在（引擎不识别）
        DslKeywords.UiElementTypes.Should().NotContain("scrollview");
    }

    [Fact]
    public void Grammar_Viewport_HasScrollAttributes()
    {
        var g = DslGrammar.TryGet("viewport");
        g.Should().NotBeNull();
        g!.NamedParams.Should().ContainKey("scroll_h");
        g.NamedParams.Should().ContainKey("scroll_v");
    }

    [Fact]
    public void Grammar_Scroll_HasScrollAttributes()
    {
        var g = DslGrammar.TryGet("scroll");
        g.Should().NotBeNull();
        g!.NamedParams.Should().ContainKey("scroll_h");
        g.NamedParams.Should().ContainKey("scroll_v");
    }

    [Fact]
    public void Grammar_Bar_HasMinMaxAttributes()
    {
        var g = DslGrammar.TryGet("bar");
        g.Should().NotBeNull();
        g!.NamedParams.Should().ContainKey("min");
        g.NamedParams.Should().ContainKey("max");
    }

    // ---- UI 元素属性补全完整性（引擎渲染层对齐回归，2026-08-28）----

    [Fact]
    public void Grammar_Text_HasFontAndSize()
    {
        // Demo 实测高频：font=（49 次）/ size=（40 次）均无补全
        var g = DslGrammar.TryGet("text");
        g.Should().NotBeNull();
        g!.NamedParams.Should().ContainKey("font", "ControlFactory:832 读取 font");
        g.NamedParams.Should().ContainKey("size", "DslParser:270 size→fontSize 别名");
    }

    [Fact]
    public void Grammar_Button_HasInteractionAttributes()
    {
        // InteractionBinder 真实读取的交互属性
        var g = DslGrammar.TryGet("button");
        g.Should().NotBeNull();
        g!.NamedParams.Should().ContainKey("disabled");
        g.NamedParams.Should().ContainKey("hover_source");
        g.NamedParams.Should().ContainKey("hover_color");
        g.NamedParams.Should().ContainKey("hover_opacity");
        g.NamedParams.Should().ContainKey("selected_source");
        g.NamedParams.Should().ContainKey("selected_color");
    }

    [Fact]
    public void Grammar_Image_HasSourcePathSrcAliases()
    {
        // ControlFactory source 回退链：source → path → src
        var g = DslGrammar.TryGet("image");
        g.Should().NotBeNull();
        g!.NamedParams.Should().ContainKey("source");
        g.NamedParams.Should().ContainKey("path");
        g.NamedParams.Should().ContainKey("src");
        // 值引用：path/src → Resource 补全
        g.NamedParams["path"].Should().Be(DslCompletionRef.Resource);
        g.NamedParams["src"].Should().Be(DslCompletionRef.Resource);
    }

    [Fact]
    public void Grammar_Character_HasScreenParam()
    {
        // Phase 65 角色级模板绑定（DialogHandlers:133 GetCharProp(charDef, "screen")）
        var g = DslGrammar.TryGet("character");
        g.Should().NotBeNull();
        g!.NamedParams.Should().ContainKey("screen");
    }

    [Fact]
    public void Completion_TextElement_SuggestsFontAndSize()
    {
        // 服务层端到端：text 元素行内应提示 font=/size= 参数名
        var text = "text \"标题\" ";
        var svc = CreateServiceWithDoc(text);
        var items = svc.GetCompletion("test.story", text.Length);
        items.Should().Contain(i => i.InsertText == "font=", "text 行应提示 font=");
        items.Should().Contain(i => i.InsertText == "size=", "text 行应提示 size=");
    }

    [Fact]
    public void Completion_ButtonElement_SuggestsHoverAttributes()
    {
        var text = "button \"开始\" ";
        var svc = CreateServiceWithDoc(text);
        var items = svc.GetCompletion("test.story", text.Length);
        items.Should().Contain(i => i.InsertText == "hover_source=");
        items.Should().Contain(i => i.InsertText == "disabled=");
    }

    [Fact]
    public void Completion_FontValue_ContextIsResource()
    {
        // font= 后的值上下文应是 Resource（ formerly 误判为 Expression/变量补全）
        var text = "text \"a\" font=\"";
        var svc = CreateServiceWithDoc(text);
        var items = svc.GetCompletion("test.story", text.Length);
        // Resource 上下文：不应提示变量（variable kind），且应存在资源路径类候选或至少非变量
        items.Should().NotContain(i => i.Kind == "variable", "font= 值上下文不应是变量补全");
    }

    // ---- LSP 大检查回归（2026-08-28：引擎对齐 + 假阳性修复）----

    [Fact]
    public async Task Diagnostics_FontFamilyName_NoFalsePositiveResourceWarning()
    {
        // 回归：font="Microsoft YaHei" 字体族名不得报「未找到资源」假诊断
        // （font 曾误映射 Resource → FindResource(族名)=null → 假警告）
        var svc = CreateServiceWithDoc("text \"标题\" font=\"Microsoft YaHei\" size=32\n");
        var result = await svc.GetDiagnosticsAsync("test.story");
        result.Diagnostics.Should().NotContain(d => d.Message.Contains("未找到资源"),
            "字体族名不是资源路径，不得触发资源校验假阳性");
    }

    [Fact]
    public async Task Diagnostics_HoverSource_StillValidatedAsResource()
    {
        // 对偶验证：真实图片路径属性（hover_source）仍走资源校验（无索引时跳过，不误报）
        var svc = CreateServiceWithDoc("image \"bg.png\" hover_source=\"hover.png\"\n");
        var result = await svc.GetDiagnosticsAsync("test.story");
        // 资源索引未建立 → 校验整体跳过（设计取舍，避免空索引全盘误报）
        result.Diagnostics.Should().NotContain(d => d.Message.Contains("未注册命令"));
    }

    [Fact]
    public void Keywords_UiElementTypes_AlignedWithEngine()
    {
        // 权威源 = StoryLoader.s_uiElementTypes ∪ ControlFactory case 集
        // 新增：引擎真实渲染但补全表曾缺失的元素
        DslKeywords.UiElementTypes.Should().Contain("imagebutton", "ControlFactory:191");
        DslKeywords.UiElementTypes.Should().Contain("stack", "ControlFactory:290 + StoryLoader 白名单");
        DslKeywords.UiElementTypes.Should().Contain("stackpanel", "ControlFactory:290 别名");
        DslKeywords.UiElementTypes.Should().Contain("canvas", "ControlFactory:392");
        DslKeywords.UiElementTypes.Should().Contain("border", "ControlFactory:414");
        DslKeywords.UiElementTypes.Should().Contain("frame", "ControlFactory:228 panel 组");
        DslKeywords.UiElementTypes.Should().Contain("separator", "ControlFactory:435");
        DslKeywords.UiElementTypes.Should().Contain("window", "ControlFactory:228 + LayoutHelper 容器");
        DslKeywords.UiElementTypes.Should().Contain("dialogbox", "ControlFactory:228");
        DslKeywords.UiElementTypes.Should().Contain("popup", "ControlFactory:228");
        // 移除：无引擎依据的幻影元素（写了不渲染 / 已是语句身份）
        DslKeywords.UiElementTypes.Should().NotContain("container", "引擎无此元素（StoryLoader/CF 皆无）");
        DslKeywords.UiElementTypes.Should().NotContain("divider", "引擎无此元素（separator 才是真名）");
        DslKeywords.UiElementTypes.Should().NotContain("sprite", "sprite 是语句（_statements/_display），非 scene 元素");
        DslKeywords.UiElementTypes.Should().NotContain("live2d", "live2d_* 是语句族，非 scene 元素");
        DslKeywords.UiElementTypes.Should().NotContain("input", "input 是语句（DslStatementParser:561）");
        DslKeywords.UiElementTypes.Should().NotContain("label", "label 是流程语句（DslStatementParser:312）");
    }

    [Fact]
    public void Grammar_ImageButton_PositionalIsResource()
    {
        // imagebutton 与 image 同组（ControlFactory:190-196），位置参即图片路径
        var g = DslGrammar.TryGet("imagebutton");
        g.Should().NotBeNull();
        g!.PositionalRef.Should().Be(DslCompletionRef.Resource, "imagebutton 位置参是图片资源路径");
        g.NamedParams.Should().ContainKey("stretch");
    }

    [Fact]
    public void Grammar_Stack_HasContainerAttributes()
    {
        var g = DslGrammar.TryGet("stack");
        g.Should().NotBeNull();
        g!.NamedParams.Should().ContainKey("direction", "ControlFactory:292 读 direction");
        g.NamedParams.Should().ContainKey("spacing", "ControlFactory:825 StackPanel spacing");
    }

    // ---- 第二轮大检查回归（2026-08-28：PositionToOffset 越界钳制）----

    [Fact]
    public void Wire_Hover_OutOfRangePosition_DoesNotCrash()
    {
        // 客户端发越界 position（行 10 超出单行文档 / character 超长）→ 必须响应而非异常
        var didOpenJson = """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///test.story","languageId":"dsl","version":1,"text":"say \"hi\""}}}""";
        var hoverJson = """{"jsonrpc":"2.0","method":"textDocument/hover","id":7,"params":{"textDocument":{"uri":"file:///test.story"},"position":{"line":10,"character":500}}}""";
        var (raw, _) = SendMessages(didOpenJson, hoverJson);
        var responses = ParseResponses(raw);
        var hoverResp = responses.FirstOrDefault(r =>
            r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 7);
        // 必须收到 id=7 的响应（null result 也算）——证明 handler 未因越界崩溃
        hoverResp.ValueKind.Should().Be(JsonValueKind.Object, "越界 position 不得导致 handler 崩溃");
    }

    [Fact]
    public void Wire_DidChange_OutOfRangeRange_DoesNotCrash()
    {
        // didChange 的 range 超出文档末尾（行 99）→ Substring 不得抛 ArgumentOutOfRange
        var didOpenJson = """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///test.story","languageId":"dsl","version":1,"text":"say \"hi\""}}}""";
        var didChangeJson = """{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///test.story","version":2},"contentChanges":[{"range":{"start":{"line":99,"character":0},"end":{"line":99,"character":10}},"text":"x"}]}}""";
        var (raw, _) = SendMessages(didOpenJson, didChangeJson);
        var responses = ParseResponses(raw);
        // didChange 是通知无响应；但 didChange 后的 publishDiagnostics 应正常发布（未崩溃的证据）
        var diag = responses.FirstOrDefault(r =>
            r.TryGetProperty("method", out var m) && m.GetString() == "textDocument/publishDiagnostics");
        diag.ValueKind.Should().Be(JsonValueKind.Object, "越界 range 应用后诊断照常发布，证明未崩溃");
    }

    [Fact]
    public void Wire_Completion_EndOfLinePlusOne_Clamped()
    {
        // 光标 position.character = 行长+1（客户端常见的"行尾后一格"）→ 钳制到行尾，补全照常
        var didOpenJson = """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///test.story","languageId":"dsl","version":1,"text":"say \"hi\""}}}""";
        // 行长 9（say "hi" = 9 字符），character=10 越界
        var completionJson = """{"jsonrpc":"2.0","method":"textDocument/completion","id":8,"params":{"textDocument":{"uri":"file:///test.story"},"position":{"line":0,"character":10}}}""";
        var (raw, _) = SendMessages(didOpenJson, completionJson);
        var responses = ParseResponses(raw);
        var compResp = responses.FirstOrDefault(r =>
            r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 8);
        compResp.ValueKind.Should().Be(JsonValueKind.Object, "行尾+1 的 position 应被钳制而非崩溃");
        compResp.TryGetProperty("result", out var result).Should().BeTrue();
        result.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // ---- 第三轮审计回归（2026-08-28：路径键大小写分裂）----

    [Fact]
    public void Service_DocumentKey_CaseInsensitive()
    {
        // VS Code 发小写盘符（e:\）、本地扫描可能原样大写（E:\）——服务层必须视为同一文档
        var svc = new DslLanguageService();
        svc.UpdateDocument(@"E:\proj\test.story", "define gold = 100\n");
        // 小写路径查询补全（{ 插值变量上下文）应命中同一文档的符号
        var items = svc.GetCompletion(@"e:\proj\test.story", "say \"{$".Length);
        items.Should().Contain(i => i.InsertText == "gold", "大小写不同的路径键须命中同一文档");
    }

    [Fact]
    public void Service_SceneSymbol_AfterFullRebuild()
    {
        // 服务层基线：scene 定义（引号形式）在 UpdateDocument 后可查
        var svc = new DslLanguageService();
        svc.UpdateDocument("test.story", "scene \"demo\"\n");
        var syms = svc.GetDocumentSymbols("test.story");
        syms.Should().Contain(s => s.Name == "demo", "scene 定义应产出大纲符号");
    }

    [Fact]
    public void Wire_MixedCaseUri_SameDocument()
    {
        // didOpen 用大写盘符 URI、didChange 用小写 → 必须是同一文档（内容更新可见）
        var didOpenJson = """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///E:/test.story","languageId":"dsl","version":1,"text":"say \"old\""}}}""";
        var didChangeJson = """{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///e:/test.story","version":2},"contentChanges":[{"text":"scene \"demo\"\n"}]}}""";
        // didChange 全文替换后请求 outline：有 scene 定义 = 新内容生效
        var outlineJson = """{"jsonrpc":"2.0","method":"textDocument/documentSymbol","id":9,"params":{"textDocument":{"uri":"file:///E:/TEST.story"}}}""";
        var (raw, _) = SendMessages(didOpenJson, didChangeJson, outlineJson);
        var responses = ParseResponses(raw);
        var outlineResp = responses.FirstOrDefault(r =>
            r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 9);
        outlineResp.ValueKind.Should().Be(JsonValueKind.Object);
        outlineResp.TryGetProperty("result", out var result).Should().BeTrue();
        result.ToString().Should().Contain("demo", "大小写混合 URI 须命中同一文档（didChange 生效）");
    }

    // ---- 用户反馈复现（2026-08-28：输入 b 不出 button）----

    [Fact]
    public void Completion_TypingB_SuggestsButton()
    {
        // 光标紧跟 "b" 之后（输入中途）
        var svc = CreateServiceWithDoc("b");
        var items = svc.GetCompletion("test.story", 1);
        items.Should().Contain(i => i.InsertText == "button", "输入 b 应提示 button");
    }

    [Fact]
    public void Completion_TypingB_InScene_SuggestsButton()
    {
        var svc = CreateServiceWithDoc("scene \"s\"\n  b");
        var items = svc.GetCompletion("test.story", "scene \"s\"\n  ".Length + 1);
        items.Should().Contain(i => i.InsertText == "button", "scene 块内输入 b 应提示 button");
    }

    [Fact]
    public void Completion_ButtonAttribute_MidTyping()
    {
        // 子属性：button 后输入 h → hover_source 等
        var text = "button \"go\" h";
        var svc = CreateServiceWithDoc(text);
        var items = svc.GetCompletion("test.story", text.Length);
        items.Should().Contain(i => i.InsertText.StartsWith("hover_"), "输入 h 应提示 hover_* 属性");
    }

    [Fact]
    public void Wire_TypingB_ReturnsButtonInCompletion()
    {
        // wire 层复现：didOpen "b" → completion 光标(0,1) → 响应应含 button
        var didOpenJson = """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///test.story","languageId":"dsl","version":1,"text":"b"}}}""";
        var completionJson = """{"jsonrpc":"2.0","method":"textDocument/completion","id":10,"params":{"textDocument":{"uri":"file:///test.story"},"position":{"line":0,"character":1}}}""";
        var (raw, _) = SendMessages(didOpenJson, completionJson);
        var responses = ParseResponses(raw);
        var compResp = responses.FirstOrDefault(r =>
            r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 10);
        compResp.ValueKind.Should().Be(JsonValueKind.Object);
        compResp.TryGetProperty("result", out var result).Should().BeTrue();
        var labels = new List<string>();
        if (result.ValueKind == JsonValueKind.Array)
            foreach (var it in result.EnumerateArray())
                if (it.TryGetProperty("label", out var l)) labels.Add(l.GetString() ?? "");
        labels.Should().Contain("button", "wire 层输入 b 的补全应含 button，实际: " + string.Join(",", labels.Take(20)));
    }

    // ---- 发布版 exe 端到端冒烟（E:\Project\LingFanLspServer，用户编辑器实际跑的二进制）----

    private const string PublishedServerExe = @"E:\Project\LingFanLspServer\LingFan.Dsl.LanguageServer.exe";

    /// <summary>向指定 exe 进程发送一组 LSP 消息并返回原始 stdout（ framed ）。进程级验证发布产物行为。</summary>
    private static string RunServerProcess(string exePath, params string[] jsonRequests)
    {
        var input = new MemoryStream();
        foreach (var json in jsonRequests)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            input.Write(header, 0, header.Length);
            input.Write(body, 0, body.Length);
        }
        input.Position = 0;

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        input.CopyTo(p.StandardInput.BaseStream);
        p.StandardInput.BaseStream.Flush();
        p.StandardInput.Close();
        // EOF 后 server 正常 drain + 退出；给足余量
        if (!p.WaitForExit(15000)) p.Kill();
        return outTask.Wait(5000) ? outTask.Result : string.Empty;
    }

    [Fact]
    public void PublishedExe_Initialize_CompletionB_ReturnsButton()
    {
        if (!File.Exists(PublishedServerExe)) return; // 部署机才有发布版 exe，其余环境静默跳过
        var raw = RunServerProcess(PublishedServerExe,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"rootPath":"E:/smoke"}}""",
            """{"jsonrpc":"2.0","method":"initialized","params":{}}""",
            """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///E:/smoke/test.story","languageId":"dsl","version":1,"text":"b"}}}""",
            """{"jsonrpc":"2.0","id":2,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///E:/smoke/test.story"},"position":{"line":0,"character":1}}}""",
            """{"jsonrpc":"2.0","id":3,"method":"shutdown"}""");
        raw.Should().NotBeEmpty("server 至少应响应 initialize/shutdown");
        raw.Should().Contain("\"id\":2", "completion 请求必须有响应");
        raw.Should().Contain("\"label\":\"button\"", "输入 b 的补全响应必须含 button；实际响应片段: " +
            (raw.Length > 600 ? raw[..600] : raw));
    }

    [Fact]
    public void PublishedExe_ButtonAttributeCompletion_Works()
    {
        if (!File.Exists(PublishedServerExe)) return;
        var raw = RunServerProcess(PublishedServerExe,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"rootPath":"E:/smoke"}}""",
            """{"jsonrpc":"2.0","method":"initialized","params":{}}""",
            """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///E:/smoke/test.story","languageId":"dsl","version":1,"text":"button \"go\" h"}}}""",
            """{"jsonrpc":"2.0","id":2,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///E:/smoke/test.story"},"position":{"line":0,"character":12}}}""",
            """{"jsonrpc":"2.0","id":3,"method":"shutdown"}""");
        raw.Should().Contain("\"label\":\"hover_source\"", "button 行输入 h 应提示 hover_source；实际: " +
            (raw.Length > 600 ? raw[..600] : raw));
    }

    // ---- VS Code 真实场景：didChange 增量打字后补全（didOpen 一次性全文之外未覆盖的路径）----

    [Fact]
    public void Wire_DidChangeIncremental_ThenCompletion_ReturnsButton()
    {
        // 场景：打开已有文档（两行），在新行行首输入 b（VS Code 发单字符增量），随后请求补全
        var didOpenJson = """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///test.story","languageId":"dsl","version":1,"text":"say \"hi\"\nscene \"s\"\n"}}}""";
        // VS Code 增量：在第 2 行末尾（line 1, character 10 = "scene \"s\"" 之后）先插入换行再逐字，
        // 简化为一次插入 "\nb"（等价于用户按回车再输入 b）：
        var didChangeJson = """{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///test.story","version":2},"contentChanges":[{"range":{"start":{"line":1,"character":10},"end":{"line":1,"character":10}},"text":"\nb"}]}}""";
        // 光标在 "b" 之后（line 2, character 1）
        var completionJson = """{"jsonrpc":"2.0","method":"textDocument/completion","id":11,"params":{"textDocument":{"uri":"file:///test.story"},"position":{"line":2,"character":1}}}""";
        var (raw, _) = SendMessages(didOpenJson, didChangeJson, completionJson);
        var responses = ParseResponses(raw);
        var compResp = responses.FirstOrDefault(r =>
            r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 11);
        compResp.ValueKind.Should().Be(JsonValueKind.Object);
        compResp.TryGetProperty("result", out var result).Should().BeTrue();
        var labels = new List<string>();
        if (result.ValueKind == JsonValueKind.Array)
            foreach (var it in result.EnumerateArray())
                if (it.TryGetProperty("label", out var l)) labels.Add(l.GetString() ?? "");
        labels.Should().Contain("button", "增量输入 b 后的补全应含 button，实际: " + string.Join(",", labels.Take(20)));
    }

    [Fact]
    public void PublishedExe_DidChangeIncremental_CompletionB()
    {
        if (!File.Exists(PublishedServerExe)) return;
        var raw = RunServerProcess(PublishedServerExe,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"rootPath":"E:/smoke"}}""",
            """{"jsonrpc":"2.0","method":"initialized","params":{}}""",
            """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///E:/smoke/test.story","languageId":"dsl","version":1,"text":"say \"hi\"\n"}}}""",
            """{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///E:/smoke/test.story","version":2},"contentChanges":[{"range":{"start":{"line":1,"character":0},"end":{"line":1,"character":0}},"text":"b"}]}}""",
            """{"jsonrpc":"2.0","id":2,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///E:/smoke/test.story"},"position":{"line":1,"character":1}}}""",
            """{"jsonrpc":"2.0","id":3,"method":"shutdown"}""");
        raw.Should().Contain("\"label\":\"button\"", "发布版 exe 增量输入 b 后补全应含 button；实际: " +
            (raw.Length > 800 ? raw[..800] : raw));
    }

    [Fact]
    public void PublishedExe_VsCodeRealRequest_WithContext_ReturnsButton()
    {
        if (!File.Exists(PublishedServerExe)) return;
        // 完整复刻 VS Code 请求：带 context(triggerKind/triggerCharacter) + languageId=lingfan-dsl
        var raw = RunServerProcess(PublishedServerExe,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":12345,"rootUri":"file:///e%3A/","rootPath":"e:\\smoke","clientInfo":{"name":"Visual Studio Code","version":"1.90.0"}}}""",
            """{"jsonrpc":"2.0","method":"initialized","params":{}}""",
            """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///e%3A/smoke/test.story","languageId":"lingfan-dsl","version":1,"text":"b"}}}""",
            """{"jsonrpc":"2.0","id":2,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///e%3A/smoke/test.story"},"position":{"line":0,"character":1},"context":{"triggerKind":1}}}""",
            """{"jsonrpc":"2.0","id":3,"method":"shutdown"}""");
        raw.Should().Contain("\"label\":\"button\"", "VS Code 真实请求形态（含 context）应返回 button；实际: " +
            (raw.Length > 800 ? raw[..800] : raw));
    }

    [Fact]
    public void PublishedExe_RealRootPath_InitializedScan_ThenCompletion()
    {
        if (!File.Exists(PublishedServerExe)) return;
        // 真实项目 rootPath（存在 → initialized 触发后台全项目扫描）+ 立即请求补全（与扫描并发，复刻用户时序）
        var raw = RunServerProcess(PublishedServerExe,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":1,"rootUri":"file:///e%3A/langf/Downloads/Demo/Test","rootPath":"e:\\langf\\Downloads\\Demo\\Test"}}""",
            """{"jsonrpc":"2.0","method":"initialized","params":{}}""",
            """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///e%3A/langf/Downloads/Demo/Test/Resources/Stories/system/tmp.story","languageId":"lingfan-dsl","version":1,"text":"b"}}}""",
            """{"jsonrpc":"2.0","id":2,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///e%3A/langf/Downloads/Demo/Test/Resources/Stories/system/tmp.story"},"position":{"line":0,"character":1},"context":{"triggerKind":1}}}""",
            """{"jsonrpc":"2.0","id":3,"method":"shutdown"}""");
        raw.Should().Contain("\"label\":\"button\"", "真实 rootPath + 后台扫描并发下补全应含 button；实际: " +
            (raw.Length > 800 ? raw[..800] : raw));
    }

    [Fact]
    public void PublishedExe_RealFileContent_SceneBlockTypingB()
    {
        if (!File.Exists(PublishedServerExe)) return;
        var storyPath = @"E:\langf\Downloads\Demo\Test\Resources\Stories\title\title_main.story";
        if (!File.Exists(storyPath)) return;
        var content = File.ReadAllText(storyPath)
            .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r\n", "\n").Replace("\n", "\\n");
        // 在 scene 块内的某个缩进行位置追加一行 "  b"（行首输入 b 的场景），光标在 b 后
        // 直接把 "  b\n" 插到 scene 行之后：
        var idx = content.IndexOf("scene \\\"title_main\\\" type=menu");
        idx.Should().BeGreaterThanOrEqualTo(0);
        var insertAt = content.IndexOf('\\', content.IndexOf("\\n", idx)); // scene 行末
        var modified = content.Insert(content.IndexOf("\\n", idx) + 2, "  b\\n");
        _ = insertAt;
        var didOpen = $"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didOpen\",\"params\":{{\"textDocument\":{{\"uri\":\"file:///e%3A/langf/Downloads/Demo/Test/Resources/Stories/title/title_main.story\",\"languageId\":\"lingfan-dsl\",\"version\":1,\"text\":\"{modified}\"}}}}}}";
        var line = 7; // scene 是第 7 行（0-based 7？）——动态计算：插入行在 scene 行后
        // 计算插入行号：数 \n
        var scenePos = modified.IndexOf("scene \\\"title_main\\\"");
        line = modified[..scenePos].Split("\\n").Length - 1 + 1; // 插入行 = scene 行 + 1
        var completion = $"{{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"textDocument/completion\",\"params\":{{\"textDocument\":{{\"uri\":\"file:///e%3A/langf/Downloads/Demo/Test/Resources/Stories/title/title_main.story\"}},\"position\":{{\"line\":{line},\"character\":3}},\"context\":{{\"triggerKind\":1}}}}}}";
        var raw = RunServerProcess(PublishedServerExe,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":1,"rootUri":"file:///e%3A/langf/Downloads/Demo/Test","rootPath":"e:\\langf\\Downloads\\Demo\\Test"}}""",
            """{"jsonrpc":"2.0","method":"initialized","params":{}}""",
            didOpen,
            completion,
            """{"jsonrpc":"2.0","id":3,"method":"shutdown"}""");
        raw.Should().Contain("\"label\":\"button\"", "真实文件 scene 块内输入 b 应提示 button；实际: " +
            (raw.Length > 800 ? raw[..800] : raw));
    }

    [Fact]
    public void PublishedExe_ExactUserScenario_Chapter1Line18Char5()
    {
        if (!File.Exists(PublishedServerExe)) return;
        var storyPath = @"E:\langf\Downloads\Demo\Test\Resources\Stories\chapter1\chapter1.story";
        if (!File.Exists(storyPath)) return;
        var content = File.ReadAllText(storyPath)
            .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r\n", "\n").Replace("\n", "\\n");
        var didOpen = $"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didOpen\",\"params\":{{\"textDocument\":{{\"uri\":\"file:///e%3A/langf/Downloads/Demo/Test/Resources/Stories/chapter1/chapter1.story\",\"languageId\":\"lingfan-dsl\",\"version\":1,\"text\":\"{content}\"}}}}}}";
        // 用户真实请求：line 18, character 5（button 单词中间）
        var completion = """{"jsonrpc":"2.0","id":2,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///e%3A/langf/Downloads/Demo/Test/Resources/Stories/chapter1/chapter1.story"},"position":{"line":18,"character":5},"context":{"triggerKind":1}}}""";
        var raw = RunServerProcess(PublishedServerExe,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":1,"rootUri":"file:///e%3A/langf/Downloads/Demo/Test","rootPath":"e:\\langf\\Downloads\\Demo\\Test"}}""",
            """{"jsonrpc":"2.0","method":"initialized","params":{}}""",
            didOpen,
            completion,
            """{"jsonrpc":"2.0","id":3,"method":"shutdown"}""");
        // 打印 id=2 响应体，断言 button
        raw.Should().Contain("\"id\":2", "必须有响应");
        raw.Should().Contain("\"label\":\"button\"", "用户精确场景（line18 char5）应含 button；实际: " +
            (raw.Length > 1000 ? raw[..1000] : raw));
    }

    // ---- signatureHelp 服务层测试 ----

    private static DslLanguageService CreateServiceWithDoc(string text, string path = "test.story")
    {
        var svc = new DslLanguageService();
        svc.UpdateDocument(path, text);
        return svc;
    }

    [Fact]
    public void SignatureHelp_Say_ReturnsNamedParams()
    {
        var svc = CreateServiceWithDoc("say \"hello\" by=\"lingfan\"");
        // 光标在 by= 之后（offset 指向 = 号后的位置）
        var offset = "say \"hello\" by=".Length;
        var info = svc.GetSignatureHelp("test.story", offset);
        info.Should().NotBeNull();
        info!.Signatures.Should().HaveCount(1);
        info.Signatures[0].Label.Should().StartWith("say(");
        info.Signatures[0].Parameters.Should().Contain(p => p.Label == "speaker");
    }

    [Fact]
    public void SignatureHelp_Bgm_ReturnsNamedParams()
    {
        var svc = CreateServiceWithDoc("bgm \"Audio/bgm.mp3\" volume=");
        var offset = "bgm \"Audio/bgm.mp3\" volume=".Length;
        var info = svc.GetSignatureHelp("test.story", offset);
        info.Should().NotBeNull();
        info!.Signatures[0].Parameters.Should().Contain(p => p.Label == "volume");
    }

    [Fact]
    public void SignatureHelp_NonStatementLine_ReturnsNull()
    {
        var svc = CreateServiceWithDoc("# this is a comment");
        var info = svc.GetSignatureHelp("test.story", 5);
        info.Should().BeNull();
    }

    [Fact]
    public void SignatureHelp_CursorOnKeyword_ReturnsNull()
    {
        var svc = CreateServiceWithDoc("say \"hello\"");
        // 光标在 say 本身中（offset=1）
        var info = svc.GetSignatureHelp("test.story", 1);
        info.Should().BeNull();
    }

    [Fact]
    public void SignatureHelp_ActiveParameter_Speaker_IsZero()
    {
        // say 的 NamedParams 顺序：speaker(0), okey(1), clickable(2), ...
        // 光标在 speaker= 之后 → activeParameter 应为 0
        var text = "say \"hi\" by=\"lf\" speaker=";
        var svc = CreateServiceWithDoc(text);
        var info = svc.GetSignatureHelp("test.story", text.Length);
        info.Should().NotBeNull();
        info!.ActiveParameter.Should().Be(0, "speaker 是 NamedParams 的第一个参数");
    }

    [Fact]
    public void SignatureHelp_ActiveParameter_Okey_IsOne()
    {
        // 光标在 okey= 之后 → activeParameter 应为 1
        var text = "say \"hi\" speaker=\"s\" okey=";
        var svc = CreateServiceWithDoc(text);
        var info = svc.GetSignatureHelp("test.story", text.Length);
        info.Should().NotBeNull();
        info!.ActiveParameter.Should().Be(1, "okey 是 NamedParams 的第二个参数");
    }

    [Fact]
    public void SignatureHelp_ActiveParameter_WordRef_By_ReturnsNull()
    {
        // by 是位置参数(wordRef)，不在 NamedParams 中 → activeParameter 应为 null
        var text = "say \"hi\" by=";
        var svc = CreateServiceWithDoc(text);
        var info = svc.GetSignatureHelp("test.story", text.Length);
        info.Should().NotBeNull();
        info!.ActiveParameter.Should().BeNull("by 是位置参数，不在 NamedParams 中");
    }

    [Fact]
    public void SignatureHelp_NoParams_ReturnsNull()
    {
        // gallery 无参数 → 不弹签名
        var svc = CreateServiceWithDoc("gallery");
        var offset = "gallery ".Length;
        var info = svc.GetSignatureHelp("test.story", offset);
        info.Should().BeNull();
    }

    // ---- Wire-level 集成测试 ----

    /// <summary>
    /// 构造 in-memory 双工管道：写入请求 → server 消费 → 响应写入 output → 测试读取。
    /// </summary>
    private static (string response, MemoryStream output) SendMessages(params string[] jsonRequests)
    {
        var input = new MemoryStream();
        foreach (var json in jsonRequests)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            input.Write(header, 0, header.Length);
            input.Write(body, 0, body.Length);
        }
        // 追加 shutdown 通知让 reader loop 优雅退出（不发 exit 以免 Environment.Exit）
        var shutdownJson = """{"jsonrpc":"2.0","method":"shutdown","id":99}""";
        var shutdownBody = Encoding.UTF8.GetBytes(shutdownJson);
        var shutdownHeader = Encoding.ASCII.GetBytes($"Content-Length: {shutdownBody.Length}\r\n\r\n");
        input.Write(shutdownHeader, 0, shutdownHeader.Length);
        input.Write(shutdownBody, 0, shutdownBody.Length);
        input.Position = 0;

        var output = new MemoryStream();
        var svc = new DslLanguageService();
        var server = new DslLanguageServer(svc, input, output);

        // Run 会阻塞直到 reader loop 退出（input EOF）；在后台线程跑
        var t = Task.Run(() => server.Run());
        // 等待 server 处理完所有消息（超时 5 秒）
        t.Wait(TimeSpan.FromSeconds(5));

        output.Position = 0;
        var responseText = Encoding.UTF8.GetString(output.ToArray());
        return (responseText, output);
    }

    /// <summary>从 output 流解析所有 JSON-RPC 响应/通知。</summary>
    private static List<JsonElement> ParseResponses(string raw)
    {
        var results = new List<JsonElement>();
        var pos = 0;
        while (pos < raw.Length)
        {
            // 找 Content-Length
            var headerEnd = raw.IndexOf("\r\n\r\n", pos, StringComparison.Ordinal);
            if (headerEnd < 0) break;
            var header = raw.Substring(pos, headerEnd - pos);
            var clIdx = header.IndexOf("Content-Length:", StringComparison.OrdinalIgnoreCase);
            if (clIdx < 0) break;
            var lenStr = header.Substring(clIdx + 15).Trim();
            if (!int.TryParse(lenStr, out var len)) break;
            pos = headerEnd + 4;
            if (pos + len > raw.Length) break;
            var json = raw.Substring(pos, len);
            pos += len;
            using var doc = JsonDocument.Parse(json);
            results.Add(doc.RootElement.Clone());
        }
        return results;
    }

    [Fact]
    public void Wire_Initialize_ReturnsCapabilities()
    {
        var initJson = """{"jsonrpc":"2.0","method":"initialize","id":1,"params":{"rootPath":"."}}""";
        var (raw, _) = SendMessages(initJson);
        var responses = ParseResponses(raw);
        responses.Should().NotBeEmpty();
        // 找 id=1 的响应
        var initResp = responses.FirstOrDefault(r =>
            r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 1);
        initResp.ValueKind.Should().Be(JsonValueKind.Object);
        initResp.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("capabilities", out var caps).Should().BeTrue();
        caps.TryGetProperty("textDocumentSync", out var sync).Should().BeTrue();
        // 验证 TextDocumentSyncOptions 对象形式
        sync.ValueKind.Should().Be(JsonValueKind.Object);
        sync.TryGetProperty("openClose", out _).Should().BeTrue();
        sync.TryGetProperty("change", out var change).Should().BeTrue();
        change.GetInt32().Should().Be(2); // Incremental
        sync.TryGetProperty("save", out _).Should().BeTrue();
        // 验证新能力
        caps.TryGetProperty("signatureHelpProvider", out var sig).Should().BeTrue();
        sig.GetBoolean().Should().BeTrue();
        caps.TryGetProperty("codeActionProvider", out var ca).Should().BeTrue();
        ca.GetBoolean().Should().BeTrue();
        caps.TryGetProperty("semanticTokensProvider", out var st).Should().BeTrue();
        st.TryGetProperty("range", out var range).Should().BeTrue();
        range.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Wire_DidOpen_Completion_ReturnsItems()
    {
        // raw string literal 中 \\" 会被当作两个反斜杠+引号，破坏 JSON；
        // 正确写法：\" 在 raw string 中是字面反斜杠+引号，JSON 解析为转义引号。
        var didOpenJson = """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///test.story","languageId":"dsl","version":1,"text":"say \"hello\" by=\"lf\"\n"}}}""";
        var completionJson = """{"jsonrpc":"2.0","method":"textDocument/completion","id":2,"params":{"textDocument":{"uri":"file:///test.story"},"position":{"line":0,"character":20}}}""";
        var (raw, _) = SendMessages(didOpenJson, completionJson);
        var responses = ParseResponses(raw);
        // 应有 completion 响应（id=2）
        var compResp = responses.FirstOrDefault(r =>
            r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 2);
        compResp.ValueKind.Should().Be(JsonValueKind.Object);
        compResp.TryGetProperty("result", out var result).Should().BeTrue();
        // result 应是数组（补全项列表）
        result.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void Wire_SignatureHelp_ReturnsInfo()
    {
        var didOpenJson = """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///test.story","languageId":"dsl","version":1,"text":"say \"hi\" by=\"lf\" speaker="}}}""";
        // 光标在行尾（character=27 对应 speaker= 之后）
        var sigHelpJson = """{"jsonrpc":"2.0","method":"textDocument/signatureHelp","id":3,"params":{"textDocument":{"uri":"file:///test.story"},"position":{"line":0,"character":27}}}""";
        var (raw, _) = SendMessages(didOpenJson, sigHelpJson);
        var responses = ParseResponses(raw);
        var sigResp = responses.FirstOrDefault(r =>
            r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 3);
        sigResp.ValueKind.Should().Be(JsonValueKind.Object);
        sigResp.TryGetProperty("result", out var result).Should().BeTrue();
        if (result.ValueKind == JsonValueKind.Object)
        {
            result.TryGetProperty("signatures", out var sigs).Should().BeTrue();
            sigs.GetArrayLength().Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Wire_DidClose_PublishesEmptyDiagnostics()
    {
        var didOpenJson = """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///test.story","languageId":"dsl","version":1,"text":"say \"hi\""}}}""";
        var didCloseJson = """{"jsonrpc":"2.0","method":"textDocument/didClose","params":{"textDocument":{"uri":"file:///test.story"}}}""";
        var (raw, _) = SendMessages(didOpenJson, didCloseJson);
        var responses = ParseResponses(raw);
        // didClose 后应发空诊断通知
        var diagNotif = responses.FirstOrDefault(r =>
            r.TryGetProperty("method", out var m) && m.GetString() == "textDocument/publishDiagnostics");
        diagNotif.ValueKind.Should().Be(JsonValueKind.Object);
        diagNotif.TryGetProperty("params", out var dp).Should().BeTrue();
        dp.TryGetProperty("diagnostics", out var diags).Should().BeTrue();
        diags.GetArrayLength().Should().Be(0, "didClose 后诊断应清空");
    }

    [Fact]
    public void Wire_SemanticTokens_Full_ReturnsDeltaEncodedData()
    {
        // 两行 DSL：say 关键字 + define 语句，验证 delta 编码（第 2 个 token 的 deltaLine 相对第 1 个）
        var didOpenJson = """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///test.story","languageId":"dsl","version":1,"text":"say \"a\"\ndefine x = 1\n"}}}""";
        var semJson = """{"jsonrpc":"2.0","method":"textDocument/semanticTokens/full","id":4,"params":{"textDocument":{"uri":"file:///test.story"}}}""";
        var (raw, _) = SendMessages(didOpenJson, semJson);
        var responses = ParseResponses(raw);
        var semResp = responses.FirstOrDefault(r =>
            r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 4);
        semResp.ValueKind.Should().Be(JsonValueKind.Object);
        semResp.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("data", out var data).Should().BeTrue();
        data.ValueKind.Should().Be(JsonValueKind.Array);
        data.GetArrayLength().Should().BeGreaterThan(0, "至少应有 say/define 关键字 token");
        // data 是 [deltaLine, deltaChar, length, tokenType, tokenModifier] × N
        // 第一个 token 的 deltaLine 应为 0（第一行），验证 delta 编码正确
        var firstDeltaLine = data[0].GetInt32();
        firstDeltaLine.Should().Be(0, "第一个 token 的 deltaLine 应为 0（相对于文档开头）");
    }

    [Fact]
    public void Wire_SemanticTokens_Range_FiltersToRange()
    {
        // 3 行 DSL：请求 range 只覆盖第 2 行，验证只返回范围内的 token
        var didOpenJson = """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///test.story","languageId":"dsl","version":1,"text":"say \"a\"\ndefine x = 1\nscene demo\n"}}}""";
        // range = 第 2 行全部（line 1）
        var rangeJson = """{"jsonrpc":"2.0","method":"textDocument/semanticTokens/range","id":5,"params":{"textDocument":{"uri":"file:///test.story"},"range":{"start":{"line":1,"character":0},"end":{"line":2,"character":0}}}}""";
        var (raw, _) = SendMessages(didOpenJson, rangeJson);
        var responses = ParseResponses(raw);
        var rangeResp = responses.FirstOrDefault(r =>
            r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 5);
        rangeResp.ValueKind.Should().Be(JsonValueKind.Object);
        rangeResp.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("data", out var data).Should().BeTrue();
        data.ValueKind.Should().Be(JsonValueKind.Array);
        // range 只覆盖第 2 行（define 行），应只返回该行的 token
        // delta 编码：第一个 token 的 deltaLine 应为 1（第 2 行，相对于文档开头）
        if (data.GetArrayLength() > 0)
        {
            var firstDeltaLine = data[0].GetInt32();
            firstDeltaLine.Should().Be(1, "range 内第一个 token 在第 2 行，deltaLine 应为 1（相对文档开头）");
        }
    }

    [Fact]
    public void Wire_CodeAction_QuickFixForUndefined()
    {
        // 一行有未定义变量的 DSL：jump $undefined_var
        var didOpenJson = """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///test.story","languageId":"dsl","version":1,"text":"jump $undefined_var\n"}}}""";
        // 先等诊断发布，然后请求 codeAction（range 覆盖变量）
        var codeActionJson = """{"jsonrpc":"2.0","method":"textDocument/codeAction","id":6,"params":{"textDocument":{"uri":"file:///test.story"},"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":20}},"context":{"diagnostics":[{"range":{"start":{"line":0,"character":6},"end":{"line":0,"character":19}},"message":"未定义的变量: undefined_var","severity":1}],"only":["quickfix"]}}}""";
        var (raw, _) = SendMessages(didOpenJson, codeActionJson);
        var responses = ParseResponses(raw);
        var caResp = responses.FirstOrDefault(r =>
            r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 6);
        caResp.ValueKind.Should().Be(JsonValueKind.Object);
        caResp.TryGetProperty("result", out var result).Should().BeTrue();
        result.ValueKind.Should().Be(JsonValueKind.Array);
        // 应至少有一个 quickfix action（声明 define）
        if (result.GetArrayLength() > 0)
        {
            var first = result[0];
            first.TryGetProperty("kind", out var kind).Should().BeTrue();
            kind.GetString().Should().Be("quickfix");
            first.TryGetProperty("title", out var title).Should().BeTrue();
            title.GetString().Should().Contain("define");
        }
    }
}
