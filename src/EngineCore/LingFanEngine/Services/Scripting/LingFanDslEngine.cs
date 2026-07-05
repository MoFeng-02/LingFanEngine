using System.Text.RegularExpressions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Services.Scripting;

/// <summary>
/// 灵泛 DSL 脚本引擎 v2
/// <para>叙事脚本 + 表达式 + 标签跳转 + 菜单 + 场景构建。</para>
/// <para>所有正则使用 [GeneratedRegex]，NativeAOT 零反射。</para>
/// </summary>
public partial class LingFanDslEngine : IScriptEngine
{
    private readonly Stack<int> _whileStartStack = new();

    /// <inheritdoc/>
    public string Name => "LingFanDSL";

    // ========== 源生成正则 ==========

    [GeneratedRegex(@"^if\s+(.+?)\s*\{\s*$")]
    private static partial Regex IfPattern();

    [GeneratedRegex(@"^\}\s*else\s+if\s+(.+?)\s*\{\s*$")]
    private static partial Regex ElseIfPattern();

    [GeneratedRegex(@"^\}\s*else\s*\{?\s*$")]
    private static partial Regex ElsePattern();

    [GeneratedRegex(@"^say\s+""([^""]+)""(?:\s+by\s+""([^""]+)"")?$")]
    private static partial Regex SayPattern();

    [GeneratedRegex(@"^navigate\s+""([^""]+)""(?:\s+scene\s+""([^""]+)"")?$")]
    private static partial Regex NavigatePattern();

    [GeneratedRegex(@"^set\s+""([^""]+)""\s+(.+)$")]
    private static partial Regex SetPattern();

    [GeneratedRegex(@"^bgm\s+""([^""]+)""(?:\s+volume=([\d.]+))?$")]
    private static partial Regex BgmPattern();

    [GeneratedRegex(@"^wait\s+([\d.]+)$")]
    private static partial Regex WaitPattern();

    [GeneratedRegex(@"^transition\s+""([^""]+)""(?:\s+duration=([\d.]+))?(?:\s+easing=(\w+))?$")]
    private static partial Regex TransitionPattern();

    [GeneratedRegex(@"^label\s+(\w+)\s*:?\s*$")]
    private static partial Regex LabelPattern();

    [GeneratedRegex(@"^jump\s+(\w+)$")]
    private static partial Regex JumpPattern();

    [GeneratedRegex(@"^call\s+(\w+)$")]
    private static partial Regex CallPattern();

    [GeneratedRegex(@"^return\s*$")]
    private static partial Regex ReturnPattern();

    [GeneratedRegex(@"^while\s+(.+?)\s*\{\s*$")]
    private static partial Regex WhilePattern();

    [GeneratedRegex(@"^menu\s+""([^""]+)""$")]
    private static partial Regex MenuStartPattern();

    [GeneratedRegex(@"^\s+option\s+""([^""]+)""\s*->\s*(\w+)\s*$")]
    private static partial Regex MenuOptionPattern();

    [GeneratedRegex(@"^show\s+""([^""]+)""(?:\s+at\s+\(([\d.]+)\s*,\s*([\d.]+)\))?\s*(?:with\s+(\w+))?$")]
    private static partial Regex ShowPattern();

    [GeneratedRegex(@"^hide\s+""([^""]+)""(?:\s+with\s+(\w+))?$")]
    private static partial Regex HidePattern();

    [GeneratedRegex(@"^background\s+""([^""]+)""(?:\s+with\s+(\w+))?$")]
    private static partial Regex BackgroundPattern();

    [GeneratedRegex(@"^\}\s*$")]
    private static partial Regex EndBlockPattern();

    /// <summary>
    /// define "key" value once —— 初始属性定义，只在变量不存在时设置
    /// </summary>
    [GeneratedRegex(@"^define\s+""([^""]+)""\s+(.+?)\s+once$")]
    private static partial Regex DefinePattern();

    /// <summary>
    /// let "key" value once —— 局部变量，每次进入场景用默认值初始化
    /// 语法同 define，但键自动加 _local_ 前缀，不参与存档
    /// </summary>
    [GeneratedRegex(@"^let\s+""([^""]+)""\s+(.+?)\s+once$")]
    private static partial Regex LetPattern();

    /// <summary>
    /// input "提示" store "变量" —— 用户输入
    /// input "提示" store "变量" options=["选1","选2"]
    /// </summary>
    [GeneratedRegex(@"^scene\s+""([^""]+)""$")]
    private static partial Regex SceneDirectPattern();

    [GeneratedRegex(@"^back$")]
    private static partial Regex BackPattern();

    [GeneratedRegex(@"^forward$")]
    private static partial Regex ForwardPattern();

    [GeneratedRegex(@"^input\s+""([^""]+)""\s+store\s+""([^""]+)""(?:\s+options=\[([^\]]+)\])?$")]
    private static partial Regex InputPattern();

    /// <summary>
    /// save "slot" / load "slot"
    /// </summary>
    [GeneratedRegex(@"^(save|load)\s+""([^""]+)""$")]
    private static partial Regex SaveLoadPattern();

    [GeneratedRegex(@"^animate\s+""([^""]+)""\s+(\w+)\s+([\d.-]+)(?:\s+duration=([\d.]+))?(?:\s+easing=(\w+))?$")]
    private static partial Regex AnimatePattern();

    /// <summary>
    /// shake [intensity=10] [duration=0.5]
    /// </summary>
    [GeneratedRegex(@"^shake(?:\s+intensity=([\d.]+))?(?:\s+duration=([\d.]+))?$")]
    private static partial Regex ShakePattern();

    /// <summary>
    /// skip —— 切换跳过模式
    /// </summary>
    [GeneratedRegex(@"^skip$")]
    private static partial Regex SkipPattern();

    /// <summary>
    /// auto —— 切换自动模式
    /// </summary>
    [GeneratedRegex(@"^auto$")]
    private static partial Regex AutoPattern();

    /// <summary>
    /// gallery unlock "id" "imagePath" [title="标题"] [scene="场景名"]
    /// </summary>
    [GeneratedRegex(@"^gallery\s+unlock\s+""([^""]+)""\s+""([^""]+)""(?:\s+title=""([^""]+)"")?(?:\s+scene=""([^""]+)"")?$")]
    private static partial Regex GalleryUnlockPattern();

    /// <summary>
    /// debug "message" [level=Info|Warning|Error|Debug]
    /// </summary>
    [GeneratedRegex(@"^debug\s+""([^""]+)""(?:\s+level=(\w+))?$")]
    private static partial Regex DebugLogPattern();

    /// <summary>
    /// nvl / nvl clear
    /// </summary>
    [GeneratedRegex(@"^nvl(?:\s+(clear))?$")]
    private static partial Regex NvlPattern();

    /// <inheritdoc/>
    public ScriptResult Compile(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new ScriptResult(true, []);

        try
        {
            var commands = new List<ICommand>();
            var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // 第一遍：先收集所有行（包括 label 行），构建 parsedLines
            // 此时还不能确定 label 的命令索引（commands 还没填充）
            // 先用 parsedLines 索引暂存，在第五遍时再转换为命令索引
            var parsedLines = new List<(int OriginalLineIndex, string Text, bool IsLabel)>();

            // 第二遍：编译指令——遍历所有行（包括 label 行），将非 label 行加入 parsedLines
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//") || line.StartsWith('#'))
                    continue;

                if (LabelPattern().IsMatch(line))
                {
                    parsedLines.Add((i, line, true));
                    continue;
                }

                parsedLines.Add((i, line, false));
            }

            // 第三遍：扫描 if {} 块结构，确定各分支的跳过计数
            var ifBlockStack = new Stack<int>(); // 记录 if/if-else/else 的起始命令索引
            var ifBranchMap = new Dictionary<int, int>(); // if/elif 命令索引 → 要跳过的指令数

            int braceDepth = 0;
            for (int ci = 0; ci < parsedLines.Count; ci++)
            {
                var (_, text, isLabel) = parsedLines[ci];
                if (isLabel) continue;

                var whileMatchThird = WhilePattern().Match(text);
                if (whileMatchThird.Success)
                {
                    ifBlockStack.Push(ci);
                    braceDepth++;
                    continue;
                }

                var ifMatch = IfPattern().Match(text);
                if (ifMatch.Success)
                {
                    ifBlockStack.Push(ci);
                    braceDepth++;
                    continue;
                }

                var elseIfMatch = ElseIfPattern().Match(text);
                if (elseIfMatch.Success && braceDepth > 0)
                {
                    ifBlockStack.Push(ci);
                    continue;
                }

                if (ElsePattern().IsMatch(text) && braceDepth > 0)
                {
                    ifBlockStack.Push(ci);
                    continue;
                }

                if (EndBlockPattern().IsMatch(text) && braceDepth > 0)
                {
                    braceDepth--;
                    // 弹出所有从对应 if 开始到 } 之间的分支起始点
                    var endIdx = ci;
                    var branchStarts = new List<int>();
                    while (ifBlockStack.Count > 0)
                    {
                        var start = ifBlockStack.Pop();
                        branchStarts.Add(start);
                        if (IsIfStart(parsedLines[start].Text))
                            break;
                    }
                    // 对每个分支起始点，跳过指令数 = endIdx - start
                    foreach (var start in branchStarts)
                    {
                        ifBranchMap[start] = endIdx - start;
                    }
                }
            }

            // 第四遍：逐行编译为命令
            var ifStateStack = new Stack<(bool IsActive, bool HasMatched, int BranchStart)>();
            int braceDepth2 = 0;
            for (int ci = 0; ci < parsedLines.Count; ci++)
            {
                var (_, line, isLabel) = parsedLines[ci];
                if (isLabel) continue;

                // while ... {
                var whileMatch = WhilePattern().Match(line);
                if (whileMatch.Success)
                {
                    var cond = whileMatch.Groups[1].Value.Trim();
                    if (cond.StartsWith('{') && cond.EndsWith('}'))
                        cond = cond[1..^1].Trim();
                    commands.Add(new BranchCommand
                    {
                        Condition = cond,
                        SkipCount = ifBranchMap.GetValueOrDefault(ci, 0),
                        HasMatched = false
                    });
                    _whileStartStack.Push(ci);
                    braceDepth2++;
                    continue;
                }

                // if ... {
                var ifMatch = IfPattern().Match(line);
                if (ifMatch.Success)
                {
                    var condition = ifMatch.Groups[1].Value.Trim();
                    // 去掉可能残留的 {} 包裹
                    if (condition.StartsWith('{') && condition.EndsWith('}'))
                        condition = condition[1..^1].Trim();
                    var skipCount = ifBranchMap.GetValueOrDefault(ci, 0);
                    commands.Add(new BranchCommand
                    {
                        Condition = condition,
                        SkipCount = skipCount,
                        HasMatched = false
                    });
                    ifStateStack.Push((true, false, ci));
                    braceDepth2++;
                    continue;
                }

                // } else if ... {
                var elseIfMatch = ElseIfPattern().Match(line);
                if (elseIfMatch.Success && braceDepth2 > 0)
                {
                    if (ifStateStack.Count > 0)
                    {
                        var prev = ifStateStack.Pop();
                        var condition = elseIfMatch.Groups[1].Value.Trim();
                        if (condition.StartsWith('{') && condition.EndsWith('}'))
                            condition = condition[1..^1].Trim();
                        var skipCount = ifBranchMap.GetValueOrDefault(ci, 0);
                        commands.Add(new BranchCommand
                        {
                            Condition = condition,
                            SkipCount = skipCount,
                            HasMatched = prev.HasMatched
                        });
                        ifStateStack.Push((true, prev.HasMatched, ci));
                    }
                    continue;
                }

                // } else {
                if (ElsePattern().IsMatch(line) && braceDepth2 > 0)
                {
                    if (ifStateStack.Count > 0)
                    {
                        var prev = ifStateStack.Pop();
                        var skipCount = ifBranchMap.GetValueOrDefault(ci, 0);
                        // else 无条件，但如果前面已经匹配则跳过
                        commands.Add(new BranchCommand
                        {
                            Condition = null,
                            SkipCount = prev.HasMatched ? skipCount : 0,
                            HasMatched = prev.HasMatched
                        });
                        ifStateStack.Push((true, prev.HasMatched || !prev.HasMatched, ci));
                    }
                    continue;
                }

                // }
                if (EndBlockPattern().IsMatch(line) && braceDepth2 > 0)
                {
                    braceDepth2--;
                    if (ifStateStack.Count > 0)
                    {
                        var prev = ifStateStack.Pop();
                        // } 本身不产生命令跳转，但需要占位
                        commands.Add(new BranchCommand
                        {
                            Condition = null,
                            SkipCount = 0,
                            HasMatched = false
                        });
                    }
                    continue;
                }

                // label 定义不产生命令
                if (LabelPattern().IsMatch(line))
                    continue;

                // 解析指令
                var cmd = ParseLine(line, labels);
                if (cmd != null)
                    commands.Add(cmd);
            }

            // 第五遍：用 parsedLines 索引计算 labels 字典（parsedLines 索引 = commands 索引）
            // parsedLines 中的非 label 行一一对应 commands 中的命令
            // 遍历 parsedLines，遇到 label 行时取其 parsedLines 索引位置
            labels.Clear();
            for (int pl = 0; pl < parsedLines.Count; pl++)
            {
                if (parsedLines[pl].IsLabel)
                {
                    // 计算该 label 对应的下一个非 label 命令在 commands 中的索引
                    // 即该 parsedLines 位置之前有多少非 label 行被编译为命令
                    int cmdIdx = 0;
                    for (int k = 0; k < pl; k++)
                    {
                        if (!parsedLines[k].IsLabel)
                            cmdIdx++;
                    }
                    // label 名不带方括号
                    var labelText = parsedLines[pl].Text.TrimEnd(':').Trim();
                    if (labelText.StartsWith("label ", StringComparison.OrdinalIgnoreCase))
                        labelText = labelText["label ".Length..].Trim();
                    labels[labelText] = cmdIdx;
                }
            }

            // 第六遍：在每个 label 末尾插入 EndOfBlock 哨兵
            // 从后往前插才不会影响前面的 label 索引
            var labelStarts = labels.Values.OrderByDescending(v => v).ToList();
            foreach (var startIdx in labelStarts)
            {
                // 找到下一个 label 的起始索引，或命令列表末尾
                int endIdx = commands.Count;
                foreach (var (_, idx) in labels)
                {
                    if (idx > startIdx && idx < endIdx)
                        endIdx = idx;
                }
                // 在 endIdx 位置插入 EndOfBlock（位于当前 label 最后一个命令之后、下一个 label 之前）
                if (endIdx >= startIdx + 1 && endIdx <= commands.Count)
                {
                    // 如果 endIdx 位置已经是另一个 label 的起始，插入到 endIdx 之前
                    commands.Insert(endIdx, new EndCommand());
                    // 修正所有在 endIdx 之后的 label 索引（+1）
                    var labelsList = labels.ToList();
                    foreach (var kv in labelsList)
                    {
                        if (kv.Value > startIdx)
                            labels[kv.Key] = kv.Value + 1;
                    }
                    // 修正 JumpCommand 的 TargetIndex
                    for (int j = 0; j < commands.Count; j++)
                    {
                        if (commands[j] is JumpCommand jmp && jmp.TargetIndex > startIdx)
                            commands[j] = jmp with { TargetIndex = jmp.TargetIndex + 1 };
                    }
                }
            }

            // 第七遍：把 label 位置信息写入 JumpCommand
            for (int i = 0; i < commands.Count; i++)
            {
                if (commands[i] is JumpCommand jmp && jmp.TargetIndex < 0)
                {
                    if (labels.TryGetValue(jmp.TargetLabel, out var idx))
                    {
                        commands[i] = jmp with { TargetIndex = idx };
                    }
                }
            }

            return new ScriptResult(true, commands.AsReadOnly(), Labels: labels);
        }
        catch (Exception ex)
        {
            return new ScriptResult(false, [], $"DSL 编译错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 判断一行是否为 if 开头（区别于 elif/else/end）
    /// </summary>
    private static bool IsIfStart(string line)
    {
        return IfPattern().IsMatch(line) || ElseIfPattern().IsMatch(line);
    }

    /// <inheritdoc/>
    public ValueTask<ScriptResult> CompileAsync(string script, CancellationToken ct = default)
    {
        return ValueTask.FromResult(Compile(script));
    }

    private static ICommand? ParseLine(string line, Dictionary<string, int> labels)
    {
        // say
        var sayMatch = SayPattern().Match(line);
        if (sayMatch.Success)
        {
            return new ShowDialogCommand
            {
                Text = sayMatch.Groups[1].Value,
                Speaker = sayMatch.Groups[2].Success ? sayMatch.Groups[2].Value : null
            };
        }

        // navigate
        var navMatch = NavigatePattern().Match(line);
        if (navMatch.Success)
        {
            var path = navMatch.Groups[1].Value;
            var sceneName = navMatch.Groups[2].Success ? navMatch.Groups[2].Value : null;
            return new NavigateCommand { Path = path, SceneName = sceneName };
        }

        // set——包含表达式解析
        var setMatch = SetPattern().Match(line);
        if (setMatch.Success)
        {
            var valuePart = setMatch.Groups[2].Value.Trim();
            var val = ParseSetValue(valuePart);
            return new SetVariableCommand
            {
                Key = setMatch.Groups[1].Value,
                Value = val
            };
        }

        // define "key" value once —— 初始属性定义
        var defineMatch = DefinePattern().Match(line);
        if (defineMatch.Success)
        {
            var val = ParseSetValue(defineMatch.Groups[2].Value.Trim());
            return new SetVariableCommand
            {
                Key = defineMatch.Groups[1].Value,
                Value = val,
                IsDefine = true
            };
        }

        // let "key" value once —— 局部变量，键自动加 _local_ 前缀
        var letMatch = LetPattern().Match(line);
        if (letMatch.Success)
        {
            var rawKey = letMatch.Groups[1].Value;
            var localKey = "_local_" + rawKey.Replace('.', '_');
            var val = ParseSetValue(letMatch.Groups[2].Value.Trim());
            return new SetVariableCommand
            {
                Key = localKey,
                Value = val,
                IsDefine = true
            };
        }

        // input "提示" store "变量" options=["选1","选2"]
        var inputMatch = InputPattern().Match(line);
        if (inputMatch.Success)
        {
            var prompt = inputMatch.Groups[1].Value;
            var storeKey = inputMatch.Groups[2].Value;
            var optionsRaw = inputMatch.Groups[3].Success ? inputMatch.Groups[3].Value : null;
            var options = optionsRaw?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(o => o.Trim().Trim('"')).ToArray();
            return new InputCommand
            {
                Prompt = prompt,
                StoreKey = storeKey,
                Options = options
            };
        }

        // bgm
        var bgmMatch = BgmPattern().Match(line);
        if (bgmMatch.Success)
        {
            var volume = bgmMatch.Groups[2].Success ? float.Parse(bgmMatch.Groups[2].Value) : 1.0f;
            return new PlayBgmCommand { Path = bgmMatch.Groups[1].Value, Volume = volume };
        }

        // wait
        var waitMatch = WaitPattern().Match(line);
        if (waitMatch.Success)
        {
            var seconds = double.Parse(waitMatch.Groups[1].Value);
            return new WaitCommand { Seconds = seconds };
        }

        // transition
        var transMatch = TransitionPattern().Match(line);
        if (transMatch.Success)
        {
            var type = transMatch.Groups[1].Value.ToLowerInvariant() switch
            {
                "fade" or "crossfade" => "FadeIn",
                "fadeout" => "FadeOut",
                "slideleft" or "slideleftin" => "SlideLeftIn",
                "slideright" or "sliderightin" => "SlideRightIn",
                "slideup" or "slideupin" => "SlideUpIn",
                "slidedown" or "slidedownin" => "SlideDownIn",
                "zoomin" or "zoom" => "ZoomIn",
                "blink" or "blinkout" => "BlinkOut",
                _ => "CrossFade"
            };
            var duration = transMatch.Groups[2].Success
                ? double.Parse(transMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
                : 0.5;
            return new TransitionCommand { Type = type, Duration = duration };
        }

        // call label——调用子过程
        var callMatch = CallPattern().Match(line);
        if (callMatch.Success)
        {
            return new CallCommand { TargetLabel = callMatch.Groups[1].Value };
        }

        // return——从 call 返回
        if (ReturnPattern().IsMatch(line))
        {
            return new ReturnCommand();
        }

        // jump
        var jumpMatch = JumpPattern().Match(line);
        if (jumpMatch.Success)
        {
            var target = jumpMatch.Groups[1].Value;
            return new JumpCommand
            {
                TargetLabel = target,
                TargetIndex = labels.TryGetValue(target, out var idx) ? idx : -1
            };
        }

        // menu —— 这里只返回菜单起始命令，选项由 StoryLoader 的 ParseMenuOptions 处理
        var menuMatch = MenuStartPattern().Match(line);
        if (menuMatch.Success)
        {
            var prompt = menuMatch.Groups[1].Value;
            return new MenuCommand
            {
                Prompt = prompt,
                Options = []
            };
        }

        // show "path" at (x,y)
        var showMatch = ShowPattern().Match(line);
        if (showMatch.Success)
        {
            var path = showMatch.Groups[1].Value;
            var x = showMatch.Groups[2].Success ? double.Parse(showMatch.Groups[2].Value) : 0.0;
            var y = showMatch.Groups[3].Success ? double.Parse(showMatch.Groups[3].Value) : 0.0;
            return new ShowHideCommand { Target = path, X = x, Y = y, IsShow = true };
        }

        // hide "id"
        var hideMatch = HidePattern().Match(line);
        if (hideMatch.Success)
        {
            return new ShowHideCommand { Target = hideMatch.Groups[1].Value, IsShow = false };
        }

        // background "path"
        var bgMatch = BackgroundPattern().Match(line);
        if (bgMatch.Success)
        {
            return new ShowHideCommand { Target = bgMatch.Groups[1].Value, X = 0, Y = 0, IsShow = true, IsBackground = true };
        }

        // animate
        var animMatch = AnimatePattern().Match(line);
        if (animMatch.Success)
        {
            var duration = animMatch.Groups[4].Success ? double.Parse(animMatch.Groups[4].Value) : 1.0;
            var easing = animMatch.Groups[5].Success ? animMatch.Groups[5].Value : "EaseOutQuad";
            return new AnimateCommand
            {
                Target = animMatch.Groups[1].Value,
                Property = animMatch.Groups[2].Value,
                TargetValue = double.Parse(animMatch.Groups[3].Value),
                Duration = duration,
                Easing = easing
            };
        }

        // shake [intensity=10] [duration=0.5]
        var shakeMatch = ShakePattern().Match(line);
        if (shakeMatch.Success)
        {
            var intensity = shakeMatch.Groups[1].Success
                ? double.Parse(shakeMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                : 10.0;
            var duration = shakeMatch.Groups[2].Success
                ? double.Parse(shakeMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
                : 0.5;
            return new ShakeCommand { Intensity = intensity, Duration = duration };
        }

        // skip —— 切换跳过模式
        if (SkipPattern().IsMatch(line))
            return new ToggleSkipCommand();

        // auto —— 切换自动模式
        if (AutoPattern().IsMatch(line))
            return new ToggleAutoCommand();

        // gallery unlock "id" "path" [title="标题"] [scene="场景名"]
        var galleryMatch = GalleryUnlockPattern().Match(line);
        if (galleryMatch.Success)
        {
            return new UnlockGalleryCommand
            {
                Id = galleryMatch.Groups[1].Value,
                ImagePath = galleryMatch.Groups[2].Value,
                Title = galleryMatch.Groups[3].Success ? galleryMatch.Groups[3].Value : null,
                SceneName = galleryMatch.Groups[4].Success ? galleryMatch.Groups[4].Value : null
            };
        }

        // debug "message" [level=Info]
        var debugMatch = DebugLogPattern().Match(line);
        if (debugMatch.Success)
        {
            var level = debugMatch.Groups[2].Success ? debugMatch.Groups[2].Value : "Info";
            return new DebugLogCommand
            {
                Message = debugMatch.Groups[1].Value,
                Level = level
            };
        }

        // nvl / nvl clear
        var nvlMatch = NvlPattern().Match(line);
        if (nvlMatch.Success)
        {
            var isClear = nvlMatch.Groups[1].Success;
            return new NvlCommand { IsClear = isClear };
        }

        // save / load
        var svMatch = SaveLoadPattern().Match(line);
        if (svMatch.Success)
        {
            var isSave = svMatch.Groups[1].Value.Equals("save", StringComparison.OrdinalIgnoreCase);
            return new SaveLoadCommand { SlotId = svMatch.Groups[2].Value, IsSave = isSave };
        }

        // scene "name" —— 清空堆栈并切换场景
        var scMatch = SceneDirectPattern().Match(line);
        if (scMatch.Success)
        {
            return new SceneCommand { SceneName = scMatch.Groups[1].Value };
        }

        // back —— 弹出 SceneStack
        if (BackPattern().IsMatch(line))
            return new BackCommand();

        // forward —— 恢复 SceneStack 前进栈
        if (ForwardPattern().IsMatch(line))
            return new ForwardCommand();

        // end —— 块结束哨兵
        if (line.Trim() == "end")
            return new EndCommand();

        return null;
    }

    /// <summary>
    /// 解析 set 的值部分——支持 {表达式} 和普通值
    /// </summary>
    private static object? ParseSetValue(string valuePart)
    {
        // {表达式} 语法
        if (valuePart.StartsWith('{') && valuePart.EndsWith('}'))
        {
            var expr = valuePart[1..^1].Trim();
            return new DslExpressionPlaceholder(expr);
        }

        // 普通值
        if (valuePart == "true") return true;
        if (valuePart == "false") return false;
        if (valuePart == "null") return null;
        if (double.TryParse(valuePart, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var num))
        {
            if (num == (int)num) return (int)num;
            return num;
        }
        if (valuePart.StartsWith('"') && valuePart.EndsWith('"'))
            return valuePart[1..^1];
        return valuePart;
    }
}

/// <summary>
/// DSL 表达式占位符——在 set 命令中嵌入，GameLoop 运行时求值
/// </summary>
public class DslExpressionPlaceholder
{
    public string Expression { get; }
    public DslExpressionPlaceholder(string expression) { Expression = expression; }
    public override string ToString() => $"{{{Expression}}}";
}

/// <summary>
/// 块结束哨兵——DslExecutor 遇到此命令时停止推进
/// <para>编译时在每个 label 末尾自动插入。</para>
/// </summary>
public readonly record struct EndCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public EndCommand() { }
}
