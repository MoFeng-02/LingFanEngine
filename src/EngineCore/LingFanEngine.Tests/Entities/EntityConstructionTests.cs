using FluentAssertions;
using LingFanEngine.Abstractions.Entities;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Entities.Medias;
using LingFanEngine.Abstractions.Entities.Scenes;
using LingFanEngine.Abstractions.Entities.Transitions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using Xunit;

namespace LingFanEngine.Tests.Entities;

/// <summary>
/// 数据实体构造测试：实例化 Abstractions.Entities 下所有数据类，断言 required 属性与默认值约定。
/// 这些类多为纯数据契约，显式构造可锁定默认值、捕获破坏性改动（如某默认值被误改）。
/// </summary>
public class EntityConstructionTests
{
    [Fact]
    public void BaseEntity_HasGeneratedId_AndMutableCommandFields()
    {
        var e = new BaseEntity();
        e.Id.Should().NotBe(Guid.Empty);
        e.Command.Should().BeNull();
        e.CommandValue.Should().BeNull();

        e.Command = "Navigate";
        e.CommandValue = 42;
        e.Command.Should().Be("Navigate");
        e.CommandValue.Should().Be(42);
    }

    [Fact]
    public void SceneEntity_RequiresSceneNameAndElements_DefaultsApplied()
    {
        var bg = new MediaEntity { MediaType = "Image", Path = "bg.png" };
        var scene = new SceneEntity
        {
            SceneName = "title",
            Elements = [new UIElementEntity { ElementType = "Text" }],
            Background = bg,
            Bgm = new MediaEntity { MediaType = "Audio", Path = "a.mp3" },
            LayoutMode = "canvas",
            IsSingleton = true,
            IsTransient = true,
        };

        scene.SceneName.Should().Be("title");
        scene.SceneType.Should().Be(SceneType.Game);
        scene.LayoutMode.Should().Be("canvas");
        scene.Elements.Should().ContainSingle();
        scene.Background.Should().BeSameAs(bg);
        scene.IsSingleton.Should().BeTrue();
        scene.IsTransient.Should().BeTrue();
    }

    [Fact]
    public void MediaEntity_RequiresMediaTypeAndPath_DefaultsApplied()
    {
        var m = new MediaEntity
        {
            MediaType = "Video",
            Path = "c.mp4",
            Width = 800,
            Height = 600,
            Loop = true,
            Volume = 0.5f,
        };
        m.MediaType.Should().Be("Video");
        m.Path.Should().Be("c.mp4");
        m.Width.Should().Be(800);
        m.Height.Should().Be(600);
        m.Loop.Should().BeTrue();
        m.Volume.Should().Be(0.5f);

        var d = new MediaEntity { MediaType = "Image", Path = "x" };
        d.Loop.Should().BeFalse();
        // 真实 bug 已修复（B4）：MediaEntity.Volume 字段无初始化器，默认 0 与注释/媒体层约定（MediaData=1.0f、StateInitializer 视频音量=1.0f）冲突。
        // 修复：字段加 = 1.0f 初始化器，恢复与注释一致的正确默认音量（满音量）。
        d.Volume.Should().Be(1.0f);
    }

    [Fact]
    public void TransitionEntity_Defaults()
    {
        var t = new TransitionEntity();
        t.Type.Should().Be(TransitionType.CrossFade);
        t.Duration.Should().Be(0.5);
        t.Easing.Should().Be(EasingType.Linear);
        t.Delay.Should().Be(0);
        t.OnCompleteTarget.Should().BeNull();
        t.Description.Should().BeNull();
    }

    [Fact]
    public void UIElementEntity_RequiresElementType_Defaults()
    {
        var e = new UIElementEntity { ElementType = "Button" };
        e.ElementType.Should().Be("Button");
        e.InCustom.Should().BeFalse();
        e.CustomElement.Should().BeNull();
        e.Properties.Should().NotBeNull();
        e.Children.Should().NotBeNull();
        e.Order.Should().Be(0);
    }

    [Fact]
    public void TimeEventEntity_DefaultsAndSetters()
    {
        var e = new TimeEventEntity
        {
            TriggerDay = 3,
            DaysOfWeek = new[] { DayOfWeek.Monday },
            TriggerHour = 8,
            TriggerMinute = 30,
            TargetPath = "scene_a",
            IsOneShot = false,
            Priority = 5,
            Description = "daily",
            Condition = "gold >= 10",
        };
        e.TriggerDay.Should().Be(3);
        e.DaysOfWeek.Should().ContainSingle().Which.Should().Be(DayOfWeek.Monday);
        e.TriggerHour.Should().Be(8);
        e.TriggerMinute.Should().Be(30);
        e.TargetPath.Should().Be("scene_a");
        e.IsOneShot.Should().BeFalse();
        e.Priority.Should().Be(5);
        e.Description.Should().Be("daily");
        e.Condition.Should().Be("gold >= 10");

        var d = new TimeEventEntity();
        d.IsOneShot.Should().BeTrue();
        d.TriggerDay.Should().Be(0);
    }

    [Fact]
    public void TimeEventRegistration_InitProperties()
    {
        var r = new TimeEventRegistration
        {
            Id = "evt1",
            Hour = 12,
            Minute = 30,
            Day = 2,
            DaysOfWeek = new[] { DayOfWeek.Friday },
            IsOneShot = false,
            Callback = () => Task.CompletedTask,
            Priority = 9,
            Condition = "x > 1",
            Description = "desc",
            IsLegacyNavigation = true,
        };
        r.Id.Should().Be("evt1");
        r.Hour.Should().Be(12);
        r.Minute.Should().Be(30);
        r.Day.Should().Be(2);
        r.DaysOfWeek.Should().ContainSingle().Which.Should().Be(DayOfWeek.Friday);
        r.IsOneShot.Should().BeFalse();
        r.Callback.Should().NotBeNull();
        r.Priority.Should().Be(9);
        r.Condition.Should().Be("x > 1");
        r.Description.Should().Be("desc");
        r.IsLegacyNavigation.Should().BeTrue();
    }

    [Fact]
    public void TimeEventSaveState_DefaultCollectionsAndAdd()
    {
        var s = new TimeEventSaveState();
        s.RegisteredIds.Should().BeEmpty();
        s.FiredOneShotIds.Should().BeEmpty();
        s.DestroyedIds.Should().BeEmpty();
        s.SuspendedIds.Should().BeEmpty();

        s.RegisteredIds.Add("a");
        s.DestroyedIds.Add("b");
        s.SuspendedIds.Add("c");
        s.FiredOneShotIds.Add("d");

        s.RegisteredIds.Should().ContainSingle().Which.Should().Be("a");
        s.DestroyedIds.Should().ContainSingle().Which.Should().Be("b");
        s.SuspendedIds.Should().ContainSingle().Which.Should().Be("c");
        s.FiredOneShotIds.Should().ContainSingle().Which.Should().Be("d");
    }

    [Fact]
    public void TransitionType_EnumHasExpectedMembers()
    {
        Enum.GetNames<TransitionType>().Should().Contain(new[]
        {
            "FadeIn", "FadeOut", "CrossFade", "SlideLeftIn", "SlideLeftOut", "SlideRightIn",
            "SlideRightOut", "SlideUpIn", "SlideUpOut", "SlideDownIn", "SlideDownOut",
            "ZoomIn", "ZoomOut", "BlinkOut", "FadeUp", "FadeDown", "Blur",
        });
    }

    [Fact]
    public void SceneType_EnumValues()
    {
        ((int)SceneType.Game).Should().Be(0);
        ((int)SceneType.Menu).Should().Be(1);
        ((int)SceneType.UI).Should().Be(2);
    }
}
