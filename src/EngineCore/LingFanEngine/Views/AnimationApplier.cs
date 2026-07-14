using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Views;

/// <summary>
/// 动画应用器——每帧读取 __anim_*_current 状态，更新运行时控件的 Transform/Opacity。
/// <para>性能优化：</para>
/// <para>1. x/y 动画用 TranslateTransform 而非 Margin（避免触发全量布局）</para>
/// <para>2. 复用 Transform 对象（避免每帧 GC 压力）</para>
/// <para>3. 维护 Tag→Control 查找表（避免每帧线性扫描）</para>
/// <para>4. 维护活跃动画键缓存（避免每帧扫描全部 StateKeys）</para>
/// </summary>
internal sealed class AnimationApplier : IAnimationApplier
{
    private readonly IStateContainer _state;

    /// <summary>Tag → Control 查找表（RebuildScene 后重建）</summary>
    private Dictionary<string, Control> _controlMap = new(StringComparer.Ordinal);

    /// <summary>控制表是否已初始化（区分"空场景"和"未初始化"）</summary>
    private bool _controlMapInitialized;

    /// <summary>活跃动画键缓存：baseKey → (target, property)</summary>
    private readonly Dictionary<string, (string target, string property)> _activeAnimCache = new(StringComparer.Ordinal);

    /// <summary>每控件的 Transform 缓存，避免每帧创建新对象</summary>
    private readonly Dictionary<Control, TransformBundle> _transformCache = new();

    /// <summary>新动画扫描节流计数器——每 N 帧才全量扫描 _state.Keys 发现新动画</summary>
    private int _newAnimScanCounter;

    public AnimationApplier(IStateContainer state)
    {
        _state = state;
    }

    /// <summary>
    /// 场景重建后调用，重建 Tag→Control 查找表并清理缓存。
    /// </summary>
    public void RebuildControlMap(Panel? sceneRoot)
    {
        _controlMap.Clear();
        _transformCache.Clear();
        _activeAnimCache.Clear();
        _controlMapInitialized = true;
        // 重置扫描计数器——场景重建后强制下一帧立即扫描新动画
        _newAnimScanCounter = 30;
        if (sceneRoot == null) return;
        foreach (var child in sceneRoot.Children)
        {
            if (child is Control c && c.Tag is string tag && !string.IsNullOrEmpty(tag))
                _controlMap[tag] = c;
        }
    }

    public void Apply(Panel? sceneRoot)
    {
        if (sceneRoot == null) return;

        // 如果控制表未初始化，尝试重建（首次调用或场景重建后）
        if (!_controlMapInitialized)
            RebuildControlMap(sceneRoot);

        // 扫描活跃动画——先检查缓存中的已知键，再扫描新键
        List<string>? toRemove = null;
        foreach (var (baseKey, (target, property)) in _activeAnimCache)
        {
            var activeKey = baseKey + StateKeys.Animation.ActiveSuffix;
            if (!_state.Get<bool>(activeKey))
            {
                (toRemove ??= new List<string>()).Add(baseKey);
                continue;
            }
            ApplyAnimation(baseKey, target, property);
        }

        // 移除已完成的动画
        if (toRemove != null)
        {
            foreach (var key in toRemove)
                _activeAnimCache.Remove(key);
        }

        // 扫描新的活跃动画键——节流到每 30 帧（~0.25s @120fps）扫描一次
        // 大部分帧没有新动画启动，跳过全量 _state.Keys 枚举可显著减少每帧开销
        if (++_newAnimScanCounter < 30) return;
        _newAnimScanCounter = 0;

        foreach (var key in _state.Keys)
        {
            if (key is not string sk) continue;
            if (!sk.StartsWith(StateKeys.Animation.Prefix)) continue;
            if (!sk.EndsWith(StateKeys.Animation.ActiveSuffix)) continue;
            if (!_state.Get<bool>(sk)) continue;

            var baseKey = sk[..^StateKeys.Animation.ActiveSuffix.Length];
            if (_activeAnimCache.ContainsKey(baseKey)) continue;

            // 解析 target 和 property
            // baseKey 格式: __anim_{target}_{property}
            // parts[0]="", parts[1]="", parts[2]="anim", parts[3..^1]=target, parts[^1]=property
            var parts = baseKey.Split('_');
            if (parts.Length < 4) continue;

            var target = string.Join("_", parts.Skip(3).Take(parts.Length - 4));
            var property = parts[^1];

            _activeAnimCache[baseKey] = (target, property);
            ApplyAnimation(baseKey, target, property);
        }
    }

    private void ApplyAnimation(string baseKey, string target, string property)
    {
        var current = _state.Get<double>(baseKey + StateKeys.Animation.CurrentSuffix);
        if (double.IsNaN(current)) return;

        if (!_controlMap.TryGetValue(target, out var match)) return;

        switch (property)
        {
            case "x":
                GetOrCreateTransform(match).Translate.X = current;
                break;
            case "y":
                GetOrCreateTransform(match).Translate.Y = current;
                break;
            case "opacity":
                // opacity 不是 transform，不需要创建 TransformBundle
                match.Opacity = Math.Clamp(current, 0, 1);
                break;
            case "scale":
            case "zoom":
                var scaleBundle = GetOrCreateTransform(match);
                scaleBundle.Scale.ScaleX = current;
                scaleBundle.Scale.ScaleY = current;
                break;
            case "rotate":
                GetOrCreateTransform(match).Rotate.Angle = current;
                break;
        }
    }

    /// <summary>
    /// 获取或创建控件的 Transform 包。
    /// 如果控件已有 ControlFactory 设置的 RenderTransform，提取初始值后替换为可复用的 TransformBundle。
    /// </summary>
    private TransformBundle GetOrCreateTransform(Control control)
    {
        if (_transformCache.TryGetValue(control, out var existing))
            return existing;

        var bundle = new TransformBundle();

        // 尝试从 ControlFactory 设置的初始 RenderTransform 中提取初始值
        ExtractInitialTransform(control.RenderTransform, bundle);

        var group = new TransformGroup();
        group.Children.Add(bundle.Translate);
        group.Children.Add(bundle.Scale);
        group.Children.Add(bundle.Rotate);
        control.RenderTransform = group;
        control.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        _transformCache[control] = bundle;
        return bundle;
    }

    /// <summary>
    /// 从 ControlFactory 设置的初始 RenderTransform 中提取 rotation/scale 值，
    /// 避免动画启动时重置控件已有的变换。
    /// </summary>
    private static void ExtractInitialTransform(Avalonia.Media.ITransform? existing, TransformBundle bundle)
    {
        if (existing == null) return;

        switch (existing)
        {
            case RotateTransform rt:
                bundle.Rotate.Angle = rt.Angle;
                break;
            case ScaleTransform st:
                bundle.Scale.ScaleX = st.ScaleX;
                bundle.Scale.ScaleY = st.ScaleY;
                break;
            case TransformGroup tg:
                foreach (var child in tg.Children)
                {
                    if (child is RotateTransform rt2)
                        bundle.Rotate.Angle = rt2.Angle;
                    else if (child is ScaleTransform st2)
                    {
                        bundle.Scale.ScaleX = st2.ScaleX;
                        bundle.Scale.ScaleY = st2.ScaleY;
                    }
                }
                break;
        }
    }

    /// <summary>每控件的 Transform 对象包，复用避免 GC</summary>
    private sealed class TransformBundle
    {
        public readonly TranslateTransform Translate = new();
        public readonly ScaleTransform Scale = new(1, 1);
        public readonly RotateTransform Rotate = new(0);
    }
}
