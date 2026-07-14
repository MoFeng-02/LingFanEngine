using System.Collections.Generic;
using System.IO;
using LingFanEngine.DslCore;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Editor;

/// <summary>
/// Hover 提示提供器（P1-2）
/// <para>接收光标位置 → 获取单词 → 查找定义 → 生成提示文本。</para>
/// </summary>
public static class DslHoverProvider
{
    /// <summary>获取光标下单词的 Hover 提示文本</summary>
    public static string? GetHoverText(
        string word,
        DslDefinitionIndexer? indexer,
        List<VariableInfo> variables)
    {
        if (string.IsNullOrEmpty(word) || indexer == null)
            return null;

        // 1. 检查是否是关键字
        if (DslKeywords.All.Contains(word))
        {
            return GetKeywordDoc(word);
        }

        // 2. 检查是否是变量
        var varInfo = variables.FirstOrDefault(v => v.Name == word);
        if (varInfo != null)
        {
            var lines = new List<string>
            {
                $"变量: {varInfo.Name}",
                $"值: {varInfo.Value ?? "—"}",
                $"定义行: {varInfo.DefinitionLine}",
                $"引用数: {varInfo.ReferenceLines.Count}",
            };
            return string.Join("\n", lines);
        }

        // 3. 检查定义索引
        var def = indexer.FindDefinition(word);
        if (def != null)
        {
            return def.Type switch
            {
                DefinitionType.Scene => $"场景: {def.Name}\n定义: {System.IO.Path.GetFileName(def.FilePath)}:{def.Line}",
                DefinitionType.Label => $"标签: {def.Name}\n定义: {System.IO.Path.GetFileName(def.FilePath)}:{def.Line}",
                DefinitionType.Character => $"角色: {def.Name}\n定义: {System.IO.Path.GetFileName(def.FilePath)}:{def.Line}",
                DefinitionType.Style => $"样式: {def.Name}\n定义: {System.IO.Path.GetFileName(def.FilePath)}:{def.Line}",
                DefinitionType.Variable => $"变量: {def.Name}\n定义: {System.IO.Path.GetFileName(def.FilePath)}:{def.Line}",
                DefinitionType.Function => $"函数: {def.Name}\n定义: {System.IO.Path.GetFileName(def.FilePath)}:{def.Line}",
                DefinitionType.Array => $"数组: {def.Name}\n定义: {System.IO.Path.GetFileName(def.FilePath)}:{def.Line}",
                DefinitionType.Dict => $"字典: {def.Name}\n定义: {System.IO.Path.GetFileName(def.FilePath)}:{def.Line}",
                DefinitionType.Sprite => $"立绘: {def.Name}\n定义: {System.IO.Path.GetFileName(def.FilePath)}:{def.Line}",
                _ => null,
            };
        }

        return null;
    }

    /// <summary>获取关键字文档（P1-2）</summary>
    private static string? GetKeywordDoc(string keyword) => keyword switch
    {
        "say" => "say \"text\" speaker=\"name\" [clickable=true] [instant=true] [noskip=true]\n显示对话文本",
        "set" => "set \"key\" {value}\n设置变量值。支持复合赋值: += -= *= /= %=",
        "define" => "define \"key\" value [once]\n定义变量（once=仅不存在时写入）",
        "let" => "let \"key\" value [once]\n定义局部变量",
        "local" => "local \"key\" value [once]\n定义局部变量（let 的别名）",
        "if" => "if {condition}\n    ...\nelse if {condition}\n    ...\nelse\n    ...\n条件分支（缩进式）",
        "while" => "while {condition}\n    ...\n循环（缩进式）",
        "for" => "for \"var\" in {expr}\n    ...\nfor 循环",
        "foreach" => "foreach \"var\" in \"key\"\n    ...\n遍历数组",
        "switch" => "switch {expr}\n    case N\n        ...\n    default\n        ...\n多分支",
        "func" => "func name(param1, param2)\n    ...\n    return value\n函数定义",
        "return" => "return [value]\n函数返回",
        "scene" => "scene \"name\" type=game|menu|ui\n场景定义",
        "label" => "label name:\n标签定义（跳转目标）",
        "navigate" => "navigate \"scene_name\"\n导航到场景（保留堆栈）",
        "jump" => "jump label\n跳转到标签",
        "call" => "call label\n调用标签子过程",
        "menu" => "menu \"提示\"\n    \"选项1\" -> label1\n    \"选项2\" -> label2\n菜单选择",
        "input" => "input \"提示\" store \"key\"\n用户输入",
        "bgm" => "bgm \"path\" [volume=N]\n播放BGM",
        "se" => "se \"path\" [volume=N]\n播放音效",
        "ambient" => "ambient \"path\" [loop=true] [volume=N]\n播放环境音",
        "character" => "character \"key\" name=\"显示名\" color=\"#FF4444\"\n定义角色样式",
        "style" => "style \"name\" color=#xxx size=18\n定义样式表",
        "show" => "show \"target\" [at (x,y)] [with \"transition\" duration=N]\n显示元素",
        "hide" => "hide \"target\" [with \"transition\" duration=N]\n隐藏元素",
        "animate" => "animate \"target\" property value [duration=N] [easing=xxx]\n控件动画",
        "transition" => "transition \"type\" [duration=N]\n过渡动画",
        "nvl" => "nvl / nvl clear / nvl exit\nNVL 模式（累积对话）",
        "call_screen" => "call_screen \"scene\" [store=\"key\"] [with \"k=v\"]\n调用 UI 场景",
        "save" => "save \"slot\" [title \"标题\"] [screenshot=true]\n存档",
        "load" => "load \"slot\"\n读档",
        "wait" => "wait N [skipable]\n等待N秒（skipable=可跳过）",
        "pause" => "pause [N] [hard]\n暂停（无参数=等待点击）",
        "sprite" => "sprite \"id\" src=\"path\" [x=N] [y=N] [fade=N]\n显示立绘",
        "bg_switch" => "bg_switch \"path\" [transition=fade] [duration=N]\n背景切换",
        "block_rollback" => "block_rollback\n阻止回溯",
        "fix_rollback" => "fix_rollback\n恢复回溯",
        "break" => "break\n退出当前循环",
        "continue" => "continue\n跳过当前迭代",
        _ => null,
    };

}
