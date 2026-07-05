using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Services.Platform;

/// <summary>
/// 平台输入服务
/// <para>将桌面（键鼠）和移动端（触屏/陀螺仪）输入统一转换为虚拟事件，携带归一化坐标入管道。</para>
/// </summary>
public class InputService
{
    private readonly ICommandPipeline _pipeline;
    private readonly IStateContainer _state;

    /// <summary>
    /// 输入事件类型
    /// </summary>
    public enum InputEventType
    {
        PointerDown,
        PointerUp,
        PointerMove,
        Tap,
        Swipe,
        LongPress,
        KeyDown,
        KeyUp,
        Scroll,
        DeviceRotation
    }

    /// <summary>
    /// 统一输入事件
    /// </summary>
    public readonly record struct InputEvent(
        InputEventType Type,
        float NormalizedX,    // 0.0~1.0, 相对于屏幕宽度
        float NormalizedY,    // 0.0~1.0, 相对于屏幕高度
        float Pressure,       // 0.0~1.0 触摸压力（桌面端=1.0）
        float DeltaX,         // 滑动/滚轮距离
        float DeltaY,
        long TimestampMs,
        bool IsPrimary        // 是否主指针/手指
    );

    /// <summary>
    /// 构造函数
    /// </summary>
    public InputService(ICommandPipeline pipeline, IStateContainer state)
    {
        _pipeline = pipeline;
        _state = state;
    }

    /// <summary>
    /// 将原始桌面 PointerEventArgs 转换为归一化输入事件并投递
    /// </summary>
    public void EmitPointer(InputEventType type, double rawX, double rawY, double viewportWidth, double viewportHeight,
        float pressure = 1.0f, float deltaX = 0, float deltaY = 0, bool isPrimary = true)
    {
        var evt = new InputEvent
        {
            Type = type,
            NormalizedX = viewportWidth > 0 ? (float)(rawX / viewportWidth) : 0,
            NormalizedY = viewportHeight > 0 ? (float)(rawY / viewportHeight) : 0,
            Pressure = Math.Clamp(pressure, 0, 1),
            DeltaX = deltaX,
            DeltaY = deltaY,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            IsPrimary = isPrimary
        };

        // 将输入事件写入状态容器，供渲染层/逻辑层读取
        _pipeline.SendAsync(new SetVariableCommand
        {
            Key = StateKeys.Input.LastEvent,
            Value = evt
        });
    }

    /// <summary>
    /// 从状态容器读取最近的输入事件
    /// </summary>
    public bool TryGetLastEvent(out InputEvent evt)
    {
        // 当前简化：不做队列，只保留最后一个
        var raw = _state.Get<object>(StateKeys.Input.LastEvent);
        if (raw is InputEvent inputEvt)
        {
            evt = inputEvt;
            return true;
        }
        evt = default;
        return false;
    }
}
