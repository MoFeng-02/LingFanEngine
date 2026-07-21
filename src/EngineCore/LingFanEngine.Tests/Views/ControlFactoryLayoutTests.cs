using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using FluentAssertions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Services.Core;
using LingFanEngine.Views;
using Xunit;

namespace LingFanEngine.Tests.Views;

/// <summary>
/// E5（Views 渲染契约）Tier B 测试：在真实 Avalonia headless 宿主里验证 grid 的 col/row/colspan
/// 真的改变排版坐标（而不只是"设了 Grid 附加属性"）。
/// <para>做法：HeadlessUnitTestSession.StartNew 启动无头宿主（提供 IFontManagerImpl 等布局必需服务，
/// 否则 TextBlock.Measure 会抛 Unable to locate IFontManagerImpl）。Avalonia 控件是线程亲和的，
/// 故 ControlFactory 构建 Grid 与 Measure/Arrange 均须在 Dispatch 内同一线程完成；Grid 经
/// Measure+Arrange 触发与真实 LayoutManager 相同的布局路径，读取子控件 Bounds。</para>
/// <para>关键证明：若 ControlFactory 忽略 col/row，子控件会全部落在 x=0/y=0；本测试断言它们被放到对应列/行坐标，
/// 锁死"col/row 真的影响布局"这一渲染契约。</para>
/// <para>子控件用 width:"*" 拉伸填满所在列/跨列，故 Bounds.X/Width 精确等于列坐标，排除显式宽度导致的居中偏移。</para>
/// <para>仅测试工程引用 Avalonia.Headless，引擎本体零改动，AOT 安全、不影响最终编译产物。</para>
/// </summary>
public class ControlFactoryLayoutTests
{
    private sealed class FakeI18n : II18nService
    {
        public string Translate(string original) => original;
        public void SwitchLanguage(string lang) { }
        public System.Collections.Generic.IReadOnlyList<string> GetAvailableLanguages()
            => new[] { "zh-CN" };
    }

    private sealed class HeadlessApp : Application { }

    private static ControlFactory CreateFactory()
        => new(new FakeI18n(), new StateContainer());

    private static UIElementEntity Entity(string type,
        System.Collections.Generic.Dictionary<string, object>? props = null,
        System.Collections.Generic.List<UIElementEntity>? children = null)
        => new() { ElementType = type, Properties = props ?? new(), Children = children ?? new() };

    /// <summary>
    /// 子控件用 width:"*" 拉伸填满所在列（ApplyLayout 据此把 HorizontalAlignment 设为 Stretch），
    /// 使 Bounds.X 精确等于列起点、colspan 时 Width 等于跨列宽，排除显式宽度导致的居中偏移。
    /// </summary>
    private static UIElementEntity SizedChild(string text, int col, int row, int? colspan = null)
    {
        var p = new System.Collections.Generic.Dictionary<string, object>
        {
            ["text"] = text,
            ["col"] = col.ToString(),
            ["row"] = row.ToString(),
            ["width"] = "*",
            ["height"] = "80",
        };
        if (colspan.HasValue) p["colspan"] = colspan.Value.ToString();
        return Entity("text", p);
    }

    [Fact]
    public void Grid_ColPlacesChildAtExpectedX_ColumnZeroBased()
    {
        // 3 等列网格（900 宽 → 每列 300）。col 为 0 基（ParseInt 原始值直传 Grid.SetColumn）。
        //   col:1 → 第 2 列 → x≈300
        //   col:2 → 第 3 列 → x≈600
        // 若 col 被忽略，两子控件都会落在 x=0。
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
        var factory = CreateFactory();
        var gridEntity = Entity("grid",
            props: new() { ["columns"] = "*,*,*", ["rows"] = "auto" },
            children: new()
            {
                SizedChild("a", col: 1, row: 0),
                SizedChild("b", col: 2, row: 0),
            });

        session.Dispatch(() =>
        {
            var grid = (Grid)factory.ConvertToControl(gridEntity)!;
            grid.Measure(new Size(900, 600));
            grid.Arrange(new Rect(0, 0, 900, 600));

            var a = (Control)grid.Children[0];
            var b = (Control)grid.Children[1];

            a.Bounds.X.Should().BeApproximately(300, 1,
                "col:1（0 基）应落在第 2 列 x≈300，证明 col 真的影响排版");
            b.Bounds.X.Should().BeApproximately(600, 1,
                "col:2（0 基）应落在第 3 列 x≈600，证明 col 真的影响排版");
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void Grid_RowPlacesChildBelowFirstRow()
    {
        // 1 列、2 行（auto,auto）网格。row 为 0 基。
        //   row:0 → 第 1 行 → y≈0
        //   row:1 → 第 2 行 → y>0（= 第 1 行高度）
        // 若 row 被忽略，两子控件都会落在 y=0。
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
        var factory = CreateFactory();
        var gridEntity = Entity("grid",
            props: new() { ["columns"] = "*", ["rows"] = "auto,auto" },
            children: new()
            {
                SizedChild("top", col: 0, row: 0),
                SizedChild("bottom", col: 0, row: 1),
            });

        session.Dispatch(() =>
        {
            var grid = (Grid)factory.ConvertToControl(gridEntity)!;
            grid.Measure(new Size(600, 600));
            grid.Arrange(new Rect(0, 0, 600, 600));

            var top = (Control)grid.Children[0];
            var bottom = (Control)grid.Children[1];

            top.Bounds.Y.Should().BeApproximately(0, 1,
                "row:0（0 基）应落在第 1 行 y≈0");
            bottom.Bounds.Y.Should().BeGreaterThan(0,
                "row:1（0 基）应落在第 2 行 y>0，证明 row 真的影响排版");
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void Grid_ColSpan_OccupiesMultipleColumns()
    {
        // 3 等列（900 宽 → 每列 300）。colspan:2 的子控件应拉伸占满 2 列宽 ≈ 600，x≈0。
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
        var factory = CreateFactory();
        var gridEntity = Entity("grid",
            props: new() { ["columns"] = "*,*,*", ["rows"] = "auto" },
            children: new()
            {
                SizedChild("wide", col: 0, row: 0, colspan: 2),
            });

        session.Dispatch(() =>
        {
            var grid = (Grid)factory.ConvertToControl(gridEntity)!;
            grid.Measure(new Size(900, 600));
            grid.Arrange(new Rect(0, 0, 900, 600));

            var wide = (Control)grid.Children[0];
            wide.Bounds.Width.Should().BeApproximately(600, 1,
                "colspan:2 在 3 等列网格中应占满 2 列宽 ≈ 600，证明 colspan 真的影响布局");
            wide.Bounds.X.Should().BeApproximately(0, 1,
                "从第 1 列（col:0）起，x≈0");
        }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    }
}
