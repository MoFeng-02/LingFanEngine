using FluentAssertions;
using LingFanEngine.Services.Core;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// GalleryService CG 鉴赏服务测试
/// <para>用 StateContainer 验证 Unlock/IsUnlocked/GetUnlocked/Clear/SetVisible。</para>
/// </summary>
public class GalleryServiceTests
{
    private readonly StateContainer _state = new();
    private readonly GalleryService _service;

    public GalleryServiceTests()
    {
        _service = new GalleryService(_state);
    }

    [Fact]
    public void Unlock_AddsEntry()
    {
        _service.Unlock("cg1", "images/cg1.png");

        _service.IsUnlocked("cg1").Should().BeTrue();
        _service.GetUnlocked().Should().ContainSingle(e => e.Id == "cg1" && e.ImagePath == "images/cg1.png");
    }

    [Fact]
    public void Unlock_WithTitleAndScene_PersistsMetadata()
    {
        _service.Unlock("cg2", "images/cg2.png", "黄昏", "chapter3");

        var entry = _service.GetUnlocked().Single(e => e.Id == "cg2");
        entry.Title.Should().Be("黄昏");
        entry.SceneName.Should().Be("chapter3");
    }

    [Fact]
    public void Unlock_SameIdTwice_IsIdempotent()
    {
        _service.Unlock("cg3", "a.png");
        _service.Unlock("cg3", "b.png");

        _service.GetUnlocked().Should().ContainSingle(e => e.Id == "cg3");
    }

    [Fact]
    public void IsUnlocked_NotUnlocked_ReturnsFalse()
    {
        _service.IsUnlocked("never").Should().BeFalse();
    }

    [Fact]
    public void GetUnlocked_MultipleEntries_ReturnsAll()
    {
        _service.Unlock("a", "a.png");
        _service.Unlock("b", "b.png");
        _service.Unlock("c", "c.png");

        _service.GetUnlocked().Should().HaveCount(3);
    }

    [Fact]
    public void Clear_RemovesAllUnlocked()
    {
        _service.Unlock("a", "a.png");
        _service.Unlock("b", "b.png");

        _service.Clear();

        _service.GetUnlocked().Should().BeEmpty();
        _service.IsUnlocked("a").Should().BeFalse();
    }

    [Fact]
    public void SetVisible_UpdatesVisibility()
    {
        _service.SetVisible(true);
        _service.IsVisible.Should().BeTrue();

        _service.SetVisible(false);
        _service.IsVisible.Should().BeFalse();
    }
}
