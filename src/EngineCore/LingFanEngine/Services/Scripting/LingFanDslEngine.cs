using System.Globalization;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.DslCore;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Services.Scripting;

/// <summary>
/// 灵泛 DSL 脚本引擎 v3
/// <para>缩进式块结构（无 end 关键字），2 遍编译（AST 构建 + 命令生成）。</para>
/// <para>完全基于 Pidgin 解析器，零正则表达式，NativeAOT 友好。</para>
/// </summary>
public class LingFanDslEngine : IScriptEngine
{
    /// <summary>
    /// 循环上下文栈——用于编译 break/continue
    /// <para>记录循环内 break/continue 的 JumpCommand 索引，在循环编译完成后直接解析目标。</para>
    /// <para>不使用 labels 字典注册循环内部标签，避免 InsertEndSentinels 在循环边界插入多余的 EndCommand。</para>
    /// </summary>
    private readonly Stack<LoopContext> _loopStack = new();

    /// <summary>循环编译上下文——跟踪 break/continue 跳转目标</summary>
    private sealed class LoopContext
    {
        /// <summary>break 跳转的 JumpCommand 索引列表（循环体编译完成后解析）</summary>
        public List<int> BreakJumpIndices { get; } = [];
        /// <summary>continue 跳转的 JumpCommand 索引列表（循环体编译完成后解析）</summary>
        public List<int> ContinueJumpIndices { get; } = [];
    }
    /// <inheritdoc/>
    public string Name => "LingFanDSL";

    /// <inheritdoc/>
    public ScriptResult Compile(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new ScriptResult(true, []);

        // 清理循环上下文栈——防止上次编译异常中断导致栈残留
        _loopStack.Clear();

        try
        {
            // ========== 第一遍：解析行 + 追踪缩进 ==========
            var lines = script.Split('\n');
            var parsed = new List<ParsedLine>();

            for (int i = 0; i < lines.Length; i++)
            {
                var rawLine = lines[i].TrimEnd('\r');
                var trimmed = StripInlineComment(rawLine.Trim());
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//") || trimmed.StartsWith('#'))
                    continue;

                var indent = CountIndent(rawLine);
                DslStatement? stmt;
                try
                {
                    stmt = DslStatementParser.ParseLine(trimmed, i);
                }
                catch (Exception parseEx)
                {
                    return new ScriptResult(false, [],
                        $"DSL 解析错误（第 {i + 1} 行）: {parseEx.Message}\n  → {trimmed}");
                }

                parsed.Add(new ParsedLine(indent, stmt, trimmed, i));
            }

            // ========== 第二遍：生成命令 ==========
            var commands = new List<ICommand>();
            var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var pendingJumps = new List<(int CmdIndex, string TargetLabel)>();

            int idx = 0;
            while (idx < parsed.Count)
            {
                var pl = parsed[idx];
                var stmt = pl.Stmt;

                // label：记录位置，不生成命令
                if (stmt is LabelStmt lbl)
                {
                    labels[lbl.Name] = commands.Count;
                    idx++;
                    continue;
                }

                // func（DSL 2.0）：注册为 label + 存储参数列表
                if (stmt is FuncStmt fn)
                {
                    labels[fn.Name] = commands.Count;
                    // 存储参数名列表到状态容器（供 call 时绑定实参）
                    commands.Add(new SetVariableCommand
                    {
                        Key = $"__func_params_{fn.Name}",
                        Value = fn.Parameters
                    });
                    idx++;
                    continue;
                }

                // end（已废弃 no-op）
                if (stmt is EndStmt)
                {
                    idx++;
                    continue;
                }

                // if 块（含 else if / else）
                if (stmt is IfStmt)
                {
                    idx = CompileIfBlock(parsed, idx, commands, labels, pendingJumps);
                    continue;
                }

        // while 块
        if (stmt is WhileStmt)
        {
            idx = CompileWhileBlock(parsed, idx, commands, labels, pendingJumps);
            continue;
        }

        // for 块（Phase 24）
        if (stmt is ForStmt)
        {
            idx = CompileForBlock(parsed, idx, commands, labels, pendingJumps);
            continue;
        }

        // switch 块（DSL 2.0）
        if (stmt is SwitchStmt)
        {
            idx = CompileSwitchBlock(parsed, idx, commands, labels, pendingJumps);
            continue;
        }

        // foreach 块（DSL 2.0——编译为 for 循环）
        if (stmt is ForeachStmt fe)
        {
            // 将 foreach 转换为 for 并编译
            var forStmt = new ForStmt { VarName = fe.VarName, SourceExpr = fe.SourceKey };
            // 替换 parsed 中的语句
            parsed[idx] = parsed[idx] with { Stmt = forStmt };
            idx = CompileForBlock(parsed, idx, commands, labels, pendingJumps);
            continue;
        }

                // menu 块（含选项）
                if (stmt is MenuStmt)
                {
                    idx = CompileMenuBlock(parsed, idx, commands);
                    continue;
                }

                // set_time_event 块（回调驱动——子块编译为独立 ICommand[]）
                if (stmt is SetTimeEventStmt ste)
                {
                    // 编译子块到独立命令列表
                    var childCommands = new List<ICommand>();
                    var childLabels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    var childPendingJumps = new List<(int CmdIndex, string TargetLabel)>();
                    int nextIdx = CompileBody(parsed, idx + 1, pl.Indent, childCommands, childLabels, childPendingJumps);
                    ResolvePendingJumps(childCommands, childLabels, childPendingJumps);

                    commands.Add(new SetTimeEventCommand
                    {
                        Id = ste.Id,
                        Hour = ste.Hour,
                        Minute = ste.Minute,
                        Day = ste.Day,
                        DaysOfWeek = ste.DaysOfWeek,
                        IsOneShot = ste.IsOneShot,
                        Commands = childCommands.AsReadOnly(),
                        Condition = ste.Condition,
                        Description = ste.Description
                    });

                    idx = nextIdx;
                    continue;
                }

                // 普通语句
                var cmd = StatementToCommand(stmt, labels, pendingJumps, commands.Count);
                if (cmd != null)
                    commands.Add(cmd);

                // show/hide 带过渡——在 ShowHideCommand 之后追加 TransitionCommand
                // 命令流：[ShowHideCommand, TransitionCommand]
                // 执行顺序：先显示/隐藏元素 → 再启动过渡效果作用于新出现的元素
                if (stmt is ShowStmt sh && !string.IsNullOrEmpty(sh.Transition))
                {
                    commands.Add(new TransitionCommand
                    {
                        Type = MapTransitionType(sh.Transition),
                        Duration = sh.TransitionDuration ?? 1.0
                    });
                }
                else if (stmt is HideStmt hd && !string.IsNullOrEmpty(hd.Transition))
                {
                    commands.Add(new TransitionCommand
                    {
                        Type = MapTransitionType(hd.Transition),
                        Duration = hd.TransitionDuration ?? 1.0
                    });
                }

                // 批量动画——追加额外命令（第一个已由 StatementToCommand 返回）
                if (stmt is AnimateBlockStmt ab && ab.Animations.Count > 1)
                {
                    for (int i = 1; i < ab.Animations.Count; i++)
                    {
                        commands.Add(new AnimateCommand
                        {
                            Target = ab.Target,
                            Property = ab.Animations[i].Property,
                            TargetValue = ab.Animations[i].Value,
                            Duration = ab.Duration ?? 1.0,
                            Easing = ab.Easing ?? "EaseOutQuad"
                        });
                    }
                }

                // undef——同时清理 _local_ 前缀的局部变量
                if (stmt is UndefStmt u)
                {
                    var localKey = "_local_" + u.Key.Replace('.', '_');
                    if (localKey != u.Key)
                        commands.Add(new SetVariableCommand { Key = localKey, Value = null });
                }

                // return with value——SetVariableCommand 已由 StatementToCommand 返回，追加 ReturnCommand
                if (stmt is ReturnStmt ret && ret.ValuePart != null)
                {
                    commands.Add(new ReturnCommand());
                }

                idx++;
            }

            // ========== 后处理：JumpCommand 解析 + label 边界哨兵 ==========

            // 先解析 pending JumpCommand 目标（此时命令索引未被 InsertEndSentinels 偏移）
            ResolvePendingJumps(commands, labels, pendingJumps);

            // 再在每个 label 末尾插入 EndCommand 哨兵
            InsertEndSentinels(commands, labels);

            return new ScriptResult(true, commands.AsReadOnly(), Labels: labels);
        }
        catch (Exception ex)
        {
            return new ScriptResult(false, [],
                $"DSL 编译错误: {ex.GetType().Name}: {ex.Message}\n  堆栈: {ex.StackTrace?.AsSpan(0, Math.Min(200, ex.StackTrace.Length)).ToString()}");
        }
    }

    // ========== 块编译 ==========

    /// <summary>
    /// 编译 if/else if/else 块
    /// <para>命令布局：</para>
    /// <para>  BranchCommand(cond, skipCount=body+1)  — false→跳到下一分支</para>
    /// <para>  ...body commands...</para>
    /// <para>  JumpCommand(endIf)                      — true→跳到块尾</para>
    /// <para>  [BranchCommand + body + JumpCommand]    — else if 重复</para>
    /// <para>  ...else body commands...                — else 无 BranchCommand</para>
    /// <para>  endIf: 下一条指令</para>
    /// </summary>
    private int CompileIfBlock(List<ParsedLine> parsed, int startIndex,
        List<ICommand> commands, Dictionary<string, int> labels,
        List<(int CmdIndex, string TargetLabel)> pendingJumps)
    {
        var baseIndent = parsed[startIndex].Indent;
        var ifStmt = (IfStmt)parsed[startIndex].Stmt!;
        var endIfLabel = $"__endif_{startIndex}_{commands.Count}";
        var hasElseBranch = false;

        // if 分支
        var branchCmdIndex = commands.Count;
        commands.Add(new BranchCommand
        {
            Condition = ifStmt.Condition,
            SkipCount = 0, // 稍后填充
            HasMatched = false
        });

        // if body（缩进 > baseIndent 的行）
        int nextIdx = CompileBody(parsed, startIndex + 1, baseIndent, commands, labels, pendingJumps);

        // 检查 else if / else（同缩进）
        while (nextIdx < parsed.Count && parsed[nextIdx].Indent == baseIndent)
        {
            var nextStmt = parsed[nextIdx].Stmt;

            if (nextStmt is ElseIfStmt elifStmt)
            {
                // if body 后插入 JumpCommand 跳到 endIf
                var jumpIdx = commands.Count;
                commands.Add(new JumpCommand { TargetLabel = endIfLabel, TargetIndex = -1 });
                pendingJumps.Add((jumpIdx, endIfLabel));
                labels[endIfLabel] = -1; // 占位，稍后在块尾设置

                // 填充上一分支的 SkipCount = body + JumpCommand
                commands[branchCmdIndex] = (BranchCommand)commands[branchCmdIndex] with
                {
                    SkipCount = commands.Count - branchCmdIndex - 1
                };

                // else if 分支
                branchCmdIndex = commands.Count;
                commands.Add(new BranchCommand
                {
                    Condition = elifStmt.Condition,
                    SkipCount = 0,
                    HasMatched = false
                });

                nextIdx = CompileBody(parsed, nextIdx + 1, baseIndent, commands, labels, pendingJumps);
            }
            else if (nextStmt is ElseStmt)
            {
                hasElseBranch = true;

                // 上一 body 后插入 JumpCommand 跳到 endIf
                var jumpIdx = commands.Count;
                commands.Add(new JumpCommand { TargetLabel = endIfLabel, TargetIndex = -1 });
                pendingJumps.Add((jumpIdx, endIfLabel));
                labels[endIfLabel] = -1;

                // 填充上一分支的 SkipCount = body + JumpCommand
                commands[branchCmdIndex] = (BranchCommand)commands[branchCmdIndex] with
                {
                    SkipCount = commands.Count - branchCmdIndex - 1
                };

                // else body（无 BranchCommand）
                nextIdx = CompileBody(parsed, nextIdx + 1, baseIndent, commands, labels, pendingJumps);
                break;
            }
            else
            {
                break;
            }
        }

        // 填充最后一个分支的 SkipCount
        if (hasElseBranch)
        {
            // 有 else：最后分支跳到 else body
            // SkipCount 已经在 else 处理时设置了
            // endIf 位置 = 当前 commands.Count（else body 之后）
            labels[endIfLabel] = commands.Count;
        }
        else
        {
            // 无 else：最后分支 false 时跳到 endIf（当前 commands.Count）
            commands[branchCmdIndex] = (BranchCommand)commands[branchCmdIndex] with
            {
                SkipCount = commands.Count - branchCmdIndex - 1
            };
            // 如果有 JumpCommand（因为前面有 else-if），设置 endIf 位置
            if (labels.ContainsKey(endIfLabel))
                labels[endIfLabel] = commands.Count;
        }

        return nextIdx;
    }

    /// <summary>
    /// 编译 while 块
    /// <para>命令布局：</para>
    /// <para>  [condIdx] BranchCommand(cond, skipCount=body+1)  — false→跳到块尾</para>
    /// <para>  ...body commands...</para>
    /// <para>  JumpCommand(condIdx)                              — 循环回条件</para>
    /// <para>  [endIdx] 下一条指令</para>
    /// <para>注意：不向 labels 注册循环内部标签，避免 InsertEndSentinels 在循环边界</para>
    /// <para>插入多余 EndCommand 导致 BranchCommand SkipCount 失效。</para>
    /// <para>break/continue 的 JumpCommand 目标在循环编译完成后直接解析。</para>
    /// </summary>
    private int CompileWhileBlock(List<ParsedLine> parsed, int startIndex,
        List<ICommand> commands, Dictionary<string, int> labels,
        List<(int CmdIndex, string TargetLabel)> pendingJumps)
    {
        var baseIndent = parsed[startIndex].Indent;
        var whileStmt = (WhileStmt)parsed[startIndex].Stmt!;

        // 条件检查（false 时跳过 body + JumpCommand）
        var condIdx = commands.Count;
        commands.Add(new BranchCommand
        {
            Condition = whileStmt.Condition,
            SkipCount = 0, // 稍后填充
            HasMatched = false
        });

        // 循环上下文——跟踪 break/continue 跳转
        // continue 目标 = condIdx（条件检查），break 目标 = 循环之后（编译完 body + 回跳后确定）
        var ctx = new LoopContext();
        _loopStack.Push(ctx);
        int nextIdx = CompileBody(parsed, startIndex + 1, baseIndent, commands, labels, pendingJumps);
        _loopStack.Pop();

        // 循环回条件检查
        commands.Add(new JumpCommand { TargetIndex = condIdx });

        // 填充 SkipCount = body 大小 + 1（JumpCommand）
        // BranchCommand 处理器 false 跳转: currentIndex + SkipCount + 1
        // 需要跳过 body + JumpCommand，所以 SkipCount = body + JumpCommand = commands.Count - condIdx - 1
        commands[condIdx] = (BranchCommand)commands[condIdx] with
        {
            SkipCount = commands.Count - condIdx - 1 // body + JumpCommand
        };

        // 解析 continue 跳转：目标 = condIdx（条件检查）
        foreach (var jumpIdx in ctx.ContinueJumpIndices)
            commands[jumpIdx] = ((JumpCommand)commands[jumpIdx]) with { TargetIndex = condIdx };

        // 解析 break 跳转：目标 = commands.Count（循环之后）
        var breakTarget = commands.Count;
        foreach (var jumpIdx in ctx.BreakJumpIndices)
            commands[jumpIdx] = ((JumpCommand)commands[jumpIdx]) with { TargetIndex = breakTarget };

        return nextIdx;
    }

    /// <summary>
    /// 编译 for 块（Phase 24）——展开为 while + 索引变量
    /// <para>语法：for "var" in {expr} { ... }</para>
    /// <para>编译为：</para>
    /// <para>  SetVariableCommand(__for_idx = 0)</para>
    /// <para>  SetVariableCommand(__for_len = len(expr))</para>
    /// <para>  [condIdx] BranchCommand(__for_idx < __for_len)</para>
    /// <para>  SetVariableCommand(var = expr[__for_idx])</para>
    /// <para>  ...body commands...</para>
    /// <para>  SetVariableCommand(__for_idx = __for_idx + 1)</para>
    /// <para>  JumpCommand(condIdx)</para>
    /// <para>  SetVariableCommand(__for_idx = null)  — 清理</para>
    /// <para>注意：不向 labels 注册循环内部标签，break/continue 直接解析。</para>
    /// <para>continue 目标 = 递增指令位置，break 目标 = 清理之后。</para>
    /// </summary>
    private int CompileForBlock(List<ParsedLine> parsed, int startIndex,
        List<ICommand> commands, Dictionary<string, int> labels,
        List<(int CmdIndex, string TargetLabel)> pendingJumps)
    {
        var baseIndent = parsed[startIndex].Indent;
        var forStmt = (ForStmt)parsed[startIndex].Stmt!;

        // 生成唯一变量名（基于命令索引避免冲突）
        var idxVar = $"_local___for_idx_{commands.Count}";
        var lenVar = $"_local___for_len_{commands.Count}";

        // __for_idx = 0
        commands.Add(new SetVariableCommand { Key = idxVar, Value = 0 });

        // __for_len = len(expr) — 使用表达式占位符在运行时求值
        commands.Add(new SetVariableCommand
        {
            Key = lenVar,
            Value = new DslForLengthPlaceholder(forStmt.SourceExpr)
        });

        // 条件检查：__for_idx < __for_len
        var condIdx = commands.Count;
        commands.Add(new BranchCommand
        {
            Condition = $"{idxVar} < {lenVar}",
            SkipCount = 0,
            HasMatched = false
        });

        // var = expr[__for_idx] — 使用表达式占位符
        commands.Add(new SetVariableCommand
        {
            Key = "_local_" + forStmt.VarName.Replace('.', '_'),
            Value = new DslForIndexPlaceholder(forStmt.SourceExpr, idxVar)
        });

        // 循环上下文——跟踪 break/continue 跳转
        var ctx = new LoopContext();
        _loopStack.Push(ctx);
        int nextIdx = CompileBody(parsed, startIndex + 1, baseIndent, commands, labels, pendingJumps);
        _loopStack.Pop();

        // continue 跳转目标 = 递增指令位置（当前 commands.Count）
        var continueTarget = commands.Count;

        // __for_idx = __for_idx + 1
        commands.Add(new SetVariableCommand
        {
            Key = idxVar,
            Value = new DslExpressionPlaceholder($"{idxVar} + 1")
        });

        // 循环回条件检查
        commands.Add(new JumpCommand { TargetIndex = condIdx });

        // 填充 SkipCount = body + increment + JumpCommand
        commands[condIdx] = (BranchCommand)commands[condIdx] with
        {
            SkipCount = commands.Count - condIdx - 1
        };

        // 清理临时变量
        commands.Add(new SetVariableCommand { Key = idxVar, Value = null });
        commands.Add(new SetVariableCommand { Key = lenVar, Value = null });

        // 解析 continue 跳转：目标 = 递增指令位置
        foreach (var jumpIdx in ctx.ContinueJumpIndices)
            commands[jumpIdx] = ((JumpCommand)commands[jumpIdx]) with { TargetIndex = continueTarget };

        // 解析 break 跳转：目标 = 清理之后（当前 commands.Count）
        var breakTarget = commands.Count;
        foreach (var jumpIdx in ctx.BreakJumpIndices)
            commands[jumpIdx] = ((JumpCommand)commands[jumpIdx]) with { TargetIndex = breakTarget };

        return nextIdx;
    }

    /// <summary>
    /// 编译 switch 块——DSL 2.0
    /// <para>语法：</para>
    /// <para>  switch {expr}</para>
    /// <para>    case 0 ... </para>
    /// <para>    case 5 ... </para>
    /// <para>    default ... </para>
    /// <para>编译为 if/else 链：将 switch 表达式存入临时变量，每个 case 编译为 BranchCommand 比较相等。</para>
    /// </summary>
    private int CompileSwitchBlock(List<ParsedLine> parsed, int startIndex,
        List<ICommand> commands, Dictionary<string, int> labels,
        List<(int CmdIndex, string TargetLabel)> pendingJumps)
    {
        var baseIndent = parsed[startIndex].Indent;
        var switchStmt = (SwitchStmt)parsed[startIndex].Stmt!;
        var endSwitchLabel = $"__endswitch_{startIndex}_{commands.Count}";

        // 将 switch 表达式存入临时变量
        var switchVar = $"_local___switch_val_{commands.Count}";
        commands.Add(new SetVariableCommand
        {
            Key = switchVar,
            Value = new DslExpressionPlaceholder(switchStmt.Expression)
        });

        int idx = startIndex + 1;

        // 空 switch——没有 case/default 行
        if (idx >= parsed.Count || parsed[idx].Indent <= baseIndent)
        {
            labels[endSwitchLabel] = commands.Count;
            commands.Add(new SetVariableCommand { Key = switchVar, Value = null });
            return idx;
        }

        // case/default 的缩进级别 = switch 的第一个子行缩进
        var caseIndent = parsed[idx].Indent;

        while (idx < parsed.Count && parsed[idx].Indent == caseIndent)
        {
            var stmt = parsed[idx].Stmt;

            if (stmt is CaseStmt caseStmt)
            {
                // BranchCommand: switchVar == caseValue
                var branchIdx = commands.Count;
                commands.Add(new BranchCommand
                {
                    Condition = $"{switchVar} == {caseStmt.Value}",
                    SkipCount = 0,
                    HasMatched = false
                });

                // case body（缩进 > caseIndent 的行）
                idx = CompileBody(parsed, idx + 1, caseIndent, commands, labels, pendingJumps);

                // JumpCommand to endSwitch
                var jumpIdx = commands.Count;
                commands.Add(new JumpCommand { TargetLabel = endSwitchLabel, TargetIndex = -1 });
                pendingJumps.Add((jumpIdx, endSwitchLabel));
                labels[endSwitchLabel] = -1;

                // Fill SkipCount = body + JumpCommand
                commands[branchIdx] = (BranchCommand)commands[branchIdx] with
                {
                    SkipCount = commands.Count - branchIdx - 1
                };
            }
            else if (stmt is DefaultStmt)
            {
                // default body (no BranchCommand)
                idx = CompileBody(parsed, idx + 1, caseIndent, commands, labels, pendingJumps);
            }
            else
            {
                break;
            }
        }

        // endSwitch position
        labels[endSwitchLabel] = commands.Count;

        // Cleanup temp variable
        commands.Add(new SetVariableCommand { Key = switchVar, Value = null });

        return idx;
    }

    /// <summary>
    /// 编译块体（所有缩进 > baseIndent 的行），递归处理嵌套块
    /// </summary>
    private int CompileBody(List<ParsedLine> parsed, int startIndex, int parentIndent,
        List<ICommand> commands, Dictionary<string, int> labels,
        List<(int CmdIndex, string TargetLabel)> pendingJumps)
    {
        int idx = startIndex;
        while (idx < parsed.Count)
        {
            var pl = parsed[idx];

            // 缩进 <= parentIndent → 块体结束
            if (pl.Indent <= parentIndent)
                break;

            var stmt = pl.Stmt;

            // label 在块内：记录位置
            if (stmt is LabelStmt lbl)
            {
                labels[lbl.Name] = commands.Count;
                idx++;
                continue;
            }

            // func 在块内：注册为 label + 存储参数列表
            if (stmt is FuncStmt fn)
            {
                labels[fn.Name] = commands.Count;
                commands.Add(new SetVariableCommand
                {
                    Key = $"__func_params_{fn.Name}",
                    Value = fn.Parameters
                });
                idx++;
                continue;
            }

            // end（已废弃 no-op）
            if (stmt is EndStmt)
            {
                idx++;
                continue;
            }

            // break — 跳出当前循环（目标在循环编译完成后解析）
            if (stmt is BreakStmt)
            {
                if (_loopStack.Count > 0)
                {
                    var ctx = _loopStack.Peek();
                    var jumpIdx = commands.Count;
                    commands.Add(new JumpCommand { TargetIndex = -1 });
                    ctx.BreakJumpIndices.Add(jumpIdx);
                }
                idx++;
                continue;
            }

            // continue — 跳回循环条件检查/递增（目标在循环编译完成后解析）
            if (stmt is ContinueStmt)
            {
                if (_loopStack.Count > 0)
                {
                    var ctx = _loopStack.Peek();
                    var jumpIdx = commands.Count;
                    commands.Add(new JumpCommand { TargetIndex = -1 });
                    ctx.ContinueJumpIndices.Add(jumpIdx);
                }
                idx++;
                continue;
            }

            // 嵌套 if 块
            if (stmt is IfStmt)
            {
                idx = CompileIfBlock(parsed, idx, commands, labels, pendingJumps);
                continue;
            }

            // 嵌套 while 块
            if (stmt is WhileStmt)
            {
                idx = CompileWhileBlock(parsed, idx, commands, labels, pendingJumps);
                continue;
            }

            // 嵌套 for 块（Phase 24）
            if (stmt is ForStmt)
            {
                idx = CompileForBlock(parsed, idx, commands, labels, pendingJumps);
                continue;
            }

            // 嵌套 switch 块（DSL 2.0）
            if (stmt is SwitchStmt)
            {
                idx = CompileSwitchBlock(parsed, idx, commands, labels, pendingJumps);
                continue;
            }

            // 嵌套 foreach 块（DSL 2.0——编译为 for 循环）
            if (stmt is ForeachStmt fe2)
            {
                var forStmt2 = new ForStmt { VarName = fe2.VarName, SourceExpr = fe2.SourceKey };
                parsed[idx] = parsed[idx] with { Stmt = forStmt2 };
                idx = CompileForBlock(parsed, idx, commands, labels, pendingJumps);
                continue;
            }

            // 嵌套 menu 块
            if (stmt is MenuStmt)
            {
                idx = CompileMenuBlock(parsed, idx, commands);
                continue;
            }

            // 普通语句
            var cmd = StatementToCommand(stmt, labels, pendingJumps, commands.Count);
            if (cmd != null)
                commands.Add(cmd);

            // show/hide 带过渡——在 ShowHideCommand 之后追加 TransitionCommand
            if (stmt is ShowStmt sh2 && !string.IsNullOrEmpty(sh2.Transition))
            {
                commands.Add(new TransitionCommand
                {
                    Type = MapTransitionType(sh2.Transition),
                    Duration = sh2.TransitionDuration ?? 1.0
                });
            }
            else if (stmt is HideStmt hd2 && !string.IsNullOrEmpty(hd2.Transition))
            {
                commands.Add(new TransitionCommand
                {
                    Type = MapTransitionType(hd2.Transition),
                    Duration = hd2.TransitionDuration ?? 1.0
                });
            }

            // 批量动画——追加额外命令（第一个已由 StatementToCommand 返回）
            if (stmt is AnimateBlockStmt ab2 && ab2.Animations.Count > 1)
            {
                for (int i = 1; i < ab2.Animations.Count; i++)
                {
                    commands.Add(new AnimateCommand
                    {
                        Target = ab2.Target,
                        Property = ab2.Animations[i].Property,
                        TargetValue = ab2.Animations[i].Value,
                        Duration = ab2.Duration ?? 1.0,
                        Easing = ab2.Easing ?? "EaseOutQuad"
                    });
                }
            }

            // undef——同时清理 _local_ 前缀的局部变量
            if (stmt is UndefStmt u2)
            {
                var localKey = "_local_" + u2.Key.Replace('.', '_');
                if (localKey != u2.Key)
                    commands.Add(new SetVariableCommand { Key = localKey, Value = null });
            }

            // return with value——SetVariableCommand 已由 StatementToCommand 返回，追加 ReturnCommand
            if (stmt is ReturnStmt ret2 && ret2.ValuePart != null)
            {
                commands.Add(new ReturnCommand());
            }

            idx++;
        }
        return idx;
    }

    /// <summary>
    /// 编译 menu 块
    /// <para>语法：</para>
    /// <para>  menu "提示文本"</para>
    /// <para>    "选项1" -> label1</para>
    /// <para>    "选项2" -> label2</para>
    /// </summary>
    private int CompileMenuBlock(List<ParsedLine> parsed, int startIndex,
        List<ICommand> commands)
    {
        var baseIndent = parsed[startIndex].Indent;
        var menuStmt = (MenuStmt)parsed[startIndex].Stmt!;
        var options = new List<(string Text, string TargetLabel)>();

        // 收集选项（缩进 > baseIndent 的 MenuOptionStmt 行）
        int idx = startIndex + 1;
        while (idx < parsed.Count && parsed[idx].Indent > baseIndent)
        {
            if (parsed[idx].Stmt is MenuOptionStmt opt)
            {
                options.Add((opt.Text, opt.TargetLabel));
            }
            idx++;
        }

        // 生成 MenuCommand
        commands.Add(new MenuCommand
        {
            Prompt = menuStmt.Prompt,
            Options = options
        });

        return idx;
    }

    // ========== 后处理 ==========

    /// <summary>
    /// 在每个 label 末尾插入 EndCommand 哨兵
    /// <para>label 的范围：从 label 开始到下一个 label 或文件末尾</para>
    /// </summary>
    private static void InsertEndSentinels(List<ICommand> commands, Dictionary<string, int> labels)
    {
        if (labels.Count == 0) return;

        // 按 label 位置降序排列，从后往前插入避免索引偏移
        var sortedLabels = labels
            .Where(kv => kv.Value >= 0) // 排除占位 -1
            .OrderByDescending(kv => kv.Value)
            .ToList();

        foreach (var (name, startIdx) in sortedLabels)
        {
            // 找到下一个 label 的位置
            int endIdx = commands.Count;
            foreach (var (_, otherIdx) in sortedLabels)
            {
                if (otherIdx > startIdx && otherIdx < endIdx)
                    endIdx = otherIdx;
            }

            // 在 endIdx 处插入 EndCommand（如果还没有）
            if (endIdx >= startIdx && endIdx <= commands.Count)
            {
                commands.Insert(endIdx, new EndCommand());

                // 修正后续 label 的位置（+1）
                var keysToUpdate = labels.Where(kv => kv.Value > startIdx && kv.Value >= endIdx).ToList();
                foreach (var (key, val) in keysToUpdate)
                    labels[key] = val + 1;

                // 修正 JumpCommand 的 TargetIndex（+1）
                for (int i = 0; i < commands.Count; i++)
                {
                    if (commands[i] is JumpCommand jmp && jmp.TargetIndex > startIdx && jmp.TargetIndex >= endIdx)
                        commands[i] = jmp with { TargetIndex = jmp.TargetIndex + 1 };
                }
            }
        }
    }

    /// <summary>解析 pending JumpCommand 目标</summary>
    private static void ResolvePendingJumps(List<ICommand> commands,
        Dictionary<string, int> labels,
        List<(int CmdIndex, string TargetLabel)> pendingJumps)
    {
        foreach (var (cmdIdx, targetLabel) in pendingJumps)
        {
            if (labels.TryGetValue(targetLabel, out var targetIdx))
            {
                commands[cmdIdx] = ((JumpCommand)commands[cmdIdx]) with { TargetIndex = targetIdx };
            }
        }
    }

    // ========== 辅助 ==========

    /// <summary>
    /// 将 DSL 过渡类型名映射为引擎过渡类型标识符
    /// <para>与 TransitionStmt 编译逻辑保持一致</para>
    /// </summary>
    private static string MapTransitionType(string type) => type.ToLowerInvariant() switch
    {
        "fade" or "crossfade" => "FadeIn",
        "fadeout" => "FadeOut",
        "dissolve" => "CrossFade",
        "slideleft" or "slideleftin" => "SlideLeftIn",
        "slideright" or "sliderightin" => "SlideRightIn",
        "slideup" or "slideupin" => "SlideUpIn",
        "slidedown" or "slidedownin" => "SlideDownIn",
        "fadeup" => "FadeUp",
        "fadedown" => "FadeDown",
        "blur" => "Blur",
        "zoomin" or "zoom" => "ZoomIn",
        "shrink" => "ZoomOut",
        "blink" or "blinkout" => "BlinkOut",
        _ => "CrossFade"
    };

    /// <summary>计算行的缩进空格数（Tab 按 4 空格）</summary>
    private static int CountIndent(string line)
    {
        int indent = 0;
        foreach (var ch in line)
        {
            if (ch == ' ') indent++;
            else if (ch == '\t') indent += 4;
            else break;
        }
        return indent;
    }

    /// <summary>
    /// 剥离行尾注释——检测引号外的 // 并截断
    /// <para>规则：跟踪引号开闭状态，仅在引号外遇到 // 时截断。</para>
    /// <para>这避免了误切 URL 路径 "http://..." 中的 //。</para>
    /// </summary>
    private static string StripInlineComment(string line)
    {
        bool inQuotes = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"')
                inQuotes = !inQuotes;
            else if (!inQuotes && line[i] == '/' && line[i + 1] == '/')
                return line[..i].TrimEnd();
        }
        return line;
    }

    /// <summary>将 DslStatement AST 节点转换为 ICommand</summary>
    private static ICommand? StatementToCommand(DslStatement? stmt,
        Dictionary<string, int> labels,
        List<(int CmdIndex, string TargetLabel)> pendingJumps,
        int currentCmdIndex)
    {
        if (stmt == null) return null;

        return stmt switch
        {
SayStmt s => new ShowDialogCommand
{
Text = s.Text,
Speaker = s.Speaker,
Clickable = s.Clickable,
Noskip = s.Noskip,
Instant = s.Instant,
TypewriterEnabled = s.Typewriter ?? true
},

            NavigateStmt n => new NavigateCommand { Path = n.Path, SceneName = n.SceneName },

            SetStmt st => new SetVariableCommand
            {
                Key = st.Key,
                Value = ParseSetValueWithKey(st.Key, st.ValuePart)
            },

            DefineStmt d => new SetVariableCommand
            {
                Key = d.Key,
                Value = ParseSetValue(d.ValuePart),
                IsDefine = true
            },

            LetStmt lt => new SetVariableCommand
            {
                Key = "_local_" + lt.Key.Replace('.', '_'),
                Value = ParseSetValue(lt.ValuePart),
                IsDefine = true
            },

            // DSL 2.0: undef "key" — 编译为 SetVariableCommand(null)
            UndefStmt u => new SetVariableCommand
            {
                Key = u.Key,
                Value = null
            },

            InputStmt ip => new InputCommand
            {
                Prompt = ip.Prompt,
                StoreKey = ip.StoreKey,
                Options = ip.Options
            },

            BgmStmt bg => new PlayBgmCommand
            {
                Path = bg.Path,
                Volume = bg.Volume ?? 1.0f
            },

            // DSL 2.0: se "path" [volume=N]
            SeStmt se => new PlaySeCommand
            {
                Path = se.Path,
                Volume = se.Volume ?? 1.0f
            },

            // DSL 2.0: ambient "path" [loop=true] [volume=N]
            AmbientStmt amb => new PlayAmbientCommand
            {
                Path = amb.Path,
                Loop = amb.Loop,
                Volume = amb.Volume ?? 0.8f
            },

            // DSL 2.0: stop_ambient
            StopAmbientStmt => new StopAmbientCommand(),

            VideoStmt v => new PlayVideoCommand
            {
                Path = v.Path,
                Volume = v.Volume ?? 1.0f,
                Loop = v.Loop,
                AutoPlay = v.AutoPlay
            },

            StopVideoStmt => new StopVideoCommand(),
            PauseVideoStmt => new PauseVideoCommand(),
            ResumeVideoStmt => new ResumeVideoCommand(),
            SeekVideoStmt s => new SeekVideoCommand { Position = s.Position },

            CutsceneStmt c => new CutsceneCommand
            {
                Path = c.Path,
                Skipable = c.Skipable,
                Volume = c.Volume ?? 1.0f
            },

            WaitStmt w => new WaitCommand { Seconds = w.Seconds, IsSkipable = w.IsSkipable },

            PauseStmt p => p.Seconds.HasValue
                ? new WaitCommand { Seconds = p.Seconds.Value, IsSkipable = !p.IsHard }
                : new HardPauseCommand(),

TransitionStmt t => new TransitionCommand
{
Type = MapTransitionType(t.Type),
Duration = t.Duration ?? 0.5
},

            CallStmt c => new CallCommand { TargetLabel = c.TargetLabel },

            ReturnStmt r when r.ValuePart == null => new ReturnCommand(),

            JumpStmt j =>
                // 先用 label 名占位，稍后在 ResolvePendingJumps 中解析
                AddPendingJump(pendingJumps, currentCmdIndex, j.TargetLabel,
                    new JumpCommand { TargetLabel = j.TargetLabel, TargetIndex = -1 }),

            MenuStmt m => new MenuCommand
            {
                Prompt = m.Prompt,
                Options = []
            },

            // MenuOptionStmt 由 CompileMenuBlock 处理，不应到达此处
            MenuOptionStmt => null,

            ShowStmt sh => new ShowHideCommand
            {
                Target = sh.Target,
                X = sh.X ?? 0.0,
                Y = sh.Y ?? 0.0,
                IsShow = true
            },

            HideStmt h => new ShowHideCommand
            {
                Target = h.Target,
                IsShow = false
            },

            BackgroundStmt bg => new ShowHideCommand
            {
                Target = bg.Path,
                X = 0,
                Y = 0,
                IsShow = true,
                IsBackground = true
            },

            AnimateStmt an => new AnimateCommand
            {
                Target = an.Target,
                Property = an.Property,
                TargetValue = an.TargetValue,
                Duration = an.Duration ?? 1.0,
                Easing = an.Easing ?? "EaseOutQuad"
            },

            ShakeStmt sk => new ShakeCommand
            {
                Intensity = sk.Intensity ?? 10.0,
                Duration = sk.Duration ?? 0.5
            },

            ToggleSkipStmt => new ToggleSkipCommand(),
            ToggleAutoStmt => new ToggleAutoCommand(),

            GalleryUnlockStmt g => new UnlockGalleryCommand
            {
                Id = g.Id,
                ImagePath = g.ImagePath,
                Title = g.Title,
                SceneName = g.SceneName
            },

            DebugLogStmt d => new DebugLogCommand
            {
                Message = d.Message,
                Level = d.Level ?? "Info"
            },

            NvlStmt n => new NvlCommand { IsClear = n.IsClear, IsExit = n.IsExit },

            // 角色定义——存储到 __characters[key] 字典
            CharacterStmt ch => new SetVariableCommand
            {
                Key = StateKeys.Characters.Prefix + ch.Key,
                Value = ch.Properties.ToDictionary(p => p.Key, p => (object?)p.Value)
            },

            SaveStmt sv => new SaveLoadCommand { SlotId = sv.SlotId, IsSave = true, Title = sv.Title, Screenshot = sv.Screenshot },
            LoadStmt ld => new SaveLoadCommand { SlotId = ld.SlotId, IsSave = false },

            SceneStmt sc => new SceneCommand { SceneName = sc.SceneName },

            BackStmt => new BackCommand(),
            ForwardStmt => new ForwardCommand(),

            // 块结构语句已被 CompileIfBlock/CompileWhileBlock 处理，不应到达此处
            IfStmt or ElseIfStmt or ElseStmt or WhileStmt or ForStmt or LabelStmt or MenuOptionStmt
                or SwitchStmt or CaseStmt or DefaultStmt or ForeachStmt => null,

            // func——编译为 label 注册 + 参数列表存储
            FuncStmt fn => null, // func 在编译循环中作为 label 注册处理

            // return with value——设置 __return_value 后 ReturnCommand
            ReturnStmt r when r.ValuePart != null => new SetVariableCommand
            {
                Key = "__return_value",
                Value = ParseSetValue(r.ValuePart)
            },

            // array——初始化数组
            ArrayStmt arr => new SetVariableCommand
            {
                Key = arr.Key,
                Value = arr.Items.Select(ParseSetValue).ToList(),
                IsDefine = arr.IsDefine
            },

            // array_push
            ArrayPushStmt ap => new ArrayPushCommand { Key = ap.Key, ValuePart = ap.ValuePart },

            // array_pop
            ArrayPopStmt pop => new ArrayPopCommand { Key = pop.Key },

            // dict——初始化字典
            DictStmt dc => new SetVariableCommand
            {
                Key = dc.Key,
                Value = dc.Fields.ToDictionary(f => f.Field, f => ParseSetValue(f.Value)),
                IsDefine = dc.IsDefine
            },

            // dict_set
            DictSetStmt ds => new DictSetCommand { Key = ds.Key, Field = ds.Field, ValuePart = ds.ValuePart },

            // sprite
            SpriteStmt sp => new SpriteCommand
            {
                Operation = "show", Id = sp.Id, Source = sp.Source,
                X = sp.X, Y = sp.Y, Fade = sp.Fade
            },

            // sprite_state
            SpriteStateStmt ss => new SpriteCommand
            {
                Operation = "state", Id = ss.Id, Emotion = ss.Emotion
            },

            // sprite_move
            SpriteMoveStmt sm => new SpriteCommand
            {
                Operation = "move", Id = sm.Id, X = sm.X, Y = sm.Y, Duration = sm.Duration
            },

            // sprite_hide
            SpriteHideStmt sh => new SpriteCommand
            {
                Operation = "hide", Id = sh.Id, Fade = sh.Fade
            },

            // bg_switch——过渡类型在编译期映射为引擎内部标识符（与 TransitionStmt/ShowStmt 一致）
            BgSwitchStmt bs => new BgSwitchCommand
            {
                Path = bs.Path,
                Transition = string.IsNullOrEmpty(bs.Transition) ? null : MapTransitionType(bs.Transition),
                Duration = bs.Duration
            },

            // text_typewriter
            TextTypewriterStmt tt => new SetVariableCommand
            {
                Key = StateKeys.Dialog.TypewriterSpeed,
                Value = tt.Speed
            },

            // zindex
            ZindexStmt zi => new SetVariableCommand
            {
                Key = "__scene_zindex",
                Value = zi.ZIndex
            },

            // popup——编译为 ShowElementStmt（弹窗作为容器元素）
            PopupStmt pu => new SetVariableCommand
            {
                Key = "__popup_active",
                Value = new Dictionary<string, object?>
                {
                    ["name"] = pu.Name,
                    ["width"] = pu.Width,
                    ["height"] = pu.Height,
                    ["mask"] = pu.Mask
                }
            },

            // Live2D
            Live2DCharStmt lc => new Live2DCommand
            {
                Operation = "char", Id = lc.Id,
                Config = new Dictionary<string, object?>
                {
                    ["src"] = lc.Source, ["height"] = lc.Height,
                    ["x"] = lc.X, ["y"] = lc.Y, ["fade"] = lc.Fade,
                    ["loop"] = lc.Loop, ["seamless"] = lc.Seamless,
                    ["blink_rate"] = lc.BlinkRate,
                    ["mouse_track_head"] = lc.MouseTrackHead,
                    ["voice_sync_mouth"] = lc.VoiceSyncMouth
                }
            },

            Live2DShowStmt ls => new Live2DCommand { Operation = "show", Id = ls.Id },

            Live2DMotionStmt lm => new Live2DCommand
            {
                Operation = "motion", Id = lm.Id, Name = lm.Name,
                Fade = lm.Fade, Loop = lm.Loop
            },

            Live2DExprStmt le => new Live2DCommand
            {
                Operation = "expr", Id = le.Id, Name = le.Name, Fade = le.Fade
            },

            Live2DParamStmt lp => new Live2DCommand
            {
                Operation = "param", Id = lp.Id, ParamName = lp.ParamName,
                ParamValue = lp.Value, Weight = lp.Weight
            },

            Live2DHideStmt lh => new Live2DCommand
            {
                Operation = "hide", Id = lh.Id, Fade = lh.Fade
            },

            Live2DPauseStmt lpause => new Live2DCommand { Operation = "pause", Id = lpause.Id },

            Live2DResumeStmt lresume => new Live2DCommand { Operation = "resume", Id = lresume.Id },

            // 存档增强
            AutoSaveStmt autos => new SetVariableCommand
            {
                Key = StateKeys.PlaybackControl.AutoSave,
                Value = autos.Enabled
            },

            SaveDeleteStmt sd => new SaveDeleteCommand { SlotId = sd.SlotId },

            // 章节/成就
            ChapterStmt ch2 => new ChapterUnlockCommand
            {
                Id = ch2.Id, ChapterName = ch2.ChapterName, Unlock = ch2.Unlock
            },

            AchievementStmt ach => new AchievementUnlockCommand
            {
                Id = ach.Id, AchievementName = ach.AchievementName
            },

            // 播放控制
            AutoSpeedStmt asp => new SetVariableCommand
            {
                Key = StateKeys.Playback.AutoDelay,
                Value = asp.Speed
            },

            NoSkipStmt => new SetVariableCommand
            {
                Key = StateKeys.PlaybackControl.NoSkip,
                Value = true
            },

            ForceSkipStmt => new SetVariableCommand
            {
                Key = StateKeys.PlaybackControl.ForceSkip,
                Value = true
            },

            // 视频增强
            VideoSkipableStmt vs => new SetVariableCommand
            {
                Key = StateKeys.Video.CutsceneSkipable,
                Value = vs.Enabled
            },

            VideoAutoNavStmt van => new SetVariableCommand
            {
                Key = StateKeys.PlaybackControl.VideoAutoNav,
                Value = van.SceneName
            },

            // 场景元素——编译为 ShowElementCommand（由 DslExecutor 按序追加到 Scene.Elements）
            ShowElementStmt se => new ShowElementCommand { Element = se.Element },

            // 样式定义——存储到 __style_{name} 字典
            StyleStmt st => new SetVariableCommand
            {
                Key = StateKeys.Styles.Prefix + st.Name,
                Value = st.Properties.ToDictionary(p => p.Key, p => (object?)p.Value)
            },

            // 批量动画——第一个 AnimateCommand（其余在编译循环中追加）
            AnimateBlockStmt ab when ab.Animations.Count > 0 => new AnimateCommand
            {
                Target = ab.Target,
                Property = ab.Animations[0].Property,
                TargetValue = ab.Animations[0].Value,
                Duration = ab.Duration ?? 1.0,
                Easing = ab.Easing ?? "EaseOutQuad"
            },

            AnimateBlockStmt => null, // 空动画块

            // Phase 24: window 窗口管理——存储到 __dialog_window_mode
            WindowStmt w => new SetVariableCommand
            {
                Key = StateKeys.Dialog.WindowMode,
                Value = w.Mode
            },

            // Phase 24: block_rollback——设置阻止标记为当前命令索引
            BlockRollbackStmt => new SetVariableCommand
            {
                Key = StateKeys.Rollback.BlockedUntil,
                Value = currentCmdIndex
            },

            // Phase 24: fix_rollback——清除阻止标记
            FixRollbackStmt => new SetVariableCommand
            {
                Key = StateKeys.Rollback.BlockedUntil,
                Value = -1
            },

            // Phase 38: 时间事件与通知
            TimeEventStmt te => new TimeEventCommand
            {
                TriggerDay = te.TriggerDay,
                DaysOfWeek = te.DaysOfWeek,
                TriggerHour = te.TriggerHour,
                TriggerMinute = te.TriggerMinute,
                Target = te.Target,
                IsOneShot = te.IsOneShot,
                Condition = te.Condition,
                Description = te.Description
            },

            TimePauseStmt => new TimePauseCommand(),
            TimeResumeStmt => new TimeResumeCommand(),
            SkipTimeStmt st => new SkipTimeCommand { Minutes = st.Minutes },
            UnregisterTimeEventStmt ut => new UnregisterTimeEventCommand { Id = ut.Id },

            NotifyStmt n => new NotifyCommand
            {
                Text = n.Text,
                Type = n.Type ?? "info",
                Duration = n.Duration ?? 3.0
            },

            // call_screen——导航到 UI 场景并等待返回
            CallScreenStmt cs => new CallScreenCommand
            {
                SceneName = cs.SceneName,
                StoreKey = cs.StoreKey,
                Params = cs.Params?.ToDictionary(p => p.Key, p => (object?)p.Value)
            },

            _ => null
        };
    }

    /// <summary>添加 pending JumpCommand 并返回命令</summary>
    private static JumpCommand AddPendingJump(
        List<(int CmdIndex, string TargetLabel)> pendingJumps,
        int cmdIndex, string targetLabel, JumpCommand cmd)
    {
        pendingJumps.Add((cmdIndex, targetLabel));
        return cmd;
    }

    /// <inheritdoc/>
    public ValueTask<ScriptResult> CompileAsync(string script, CancellationToken ct = default)
    {
        return ValueTask.FromResult(Compile(script));
    }

    /// <summary>
    /// 解析 set 语句的值部分——支持复合赋值运算符（+= -= *= /= %=）
    /// <para>需要变量键来构建表达式：set "gold" += 20 → {gold + (20)}</para>
    /// </summary>
    private static object? ParseSetValueWithKey(string key, string valuePart)
    {
        // 复合赋值运算符：+= -= *= /= %=
        if (valuePart.Length >= 2)
        {
            var op2 = valuePart[..2];
            if (op2 is "+=" or "-=" or "*=" or "/=" or "%=")
            {
                var rest = valuePart[2..].Trim();
                // 去掉花括号（如 {base + bonus} → base + bonus）
                if (rest.StartsWith('{') && rest.EndsWith('}'))
                    rest = rest[1..^1].Trim();
                // 构建表达式：key <binaryOp> (rest)
                var binaryOp = op2[..1]; // "+=" → "+"
                return new DslExpressionPlaceholder($"{key} {binaryOp} ({rest})");
            }
        }
        return ParseSetValue(valuePart);
    }

    /// <summary>解析 set/define 的值部分——支持 {表达式} 和普通值</summary>
    private static object? ParseSetValue(string valuePart)
    {
        if (valuePart.StartsWith('{') && valuePart.EndsWith('}'))
        {
            var expr = valuePart[1..^1].Trim();
            return new DslExpressionPlaceholder(expr);
        }

        if (valuePart == "true") return true;
        if (valuePart == "false") return false;
        if (valuePart == "null") return null;
        if (double.TryParse(valuePart, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var num))
        {
            if (num == (int)num) return (int)num;
            return num;
        }
        if (valuePart.StartsWith('"') && valuePart.EndsWith('"'))
            return valuePart[1..^1];
        return valuePart;
    }

    /// <summary>已解析的行（缩进 + 语句 + 原始文本 + 行号）</summary>
    private sealed record ParsedLine(int Indent, DslStatement? Stmt, string Text, int LineNumber);
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
/// for 循环长度占位符——运行时对迭代源求值并返回其长度
/// </summary>
public class DslForLengthPlaceholder
{
    public string SourceExpr { get; }
    public DslForLengthPlaceholder(string sourceExpr) { SourceExpr = sourceExpr; }
    public override string ToString() => $"len({{{SourceExpr}}})";
}

/// <summary>
/// for 循环索引访问占位符——运行时从迭代源中取出指定索引的元素
/// </summary>
public class DslForIndexPlaceholder
{
    public string SourceExpr { get; }
    public string IndexVar { get; }
    public DslForIndexPlaceholder(string sourceExpr, string indexVar) { SourceExpr = sourceExpr; IndexVar = indexVar; }
    public override string ToString() => $"{{{SourceExpr}}}[{{{IndexVar}}}]";
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
