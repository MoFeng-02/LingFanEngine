namespace LingFanEngine.Abstractions.Entities.Enums;

/// <summary>
/// 场景类型——决定存档/堆栈/导航行为
/// </summary>
public enum SceneType
{
    /// <summary>实际游戏场景（存档、入 SceneStack）</summary>
    Game = 0,

    /// <summary>菜单/标题/设置（不存档、不入栈、可覆盖）</summary>
    Menu = 1,

    /// <summary>UI 覆盖层/弹窗（不存档、不入栈、不改变栈指针）</summary>
    UI = 2
}
