# 如何做 CG 鉴赏

CG 鉴赏（Gallery）让玩家回顾已解锁的插图。本章展示如何实现。

## 基本用法

### 解锁 CG

```dsl
label scene_with_cg:
  say "你们来到了山顶。" speaker="旁白"
  // 显示 CG
  image "Images/cg_mountain.jpg" x=0 y=0 width=100% height=100% tag="cg_mountain"
  say "夕阳下的风景美极了。" speaker="旁白"
  // 解锁 CG 到鉴赏库
  gallery_unlock "cg_mountain"
  // 继续剧情...
```

### 检查解锁状态

Gallery 解锁状态存储在 `__gallery_unlocked`（`List<GalleryEntry>`）中，DSL 无法直接用 `{gallery.xxx}` 检查。有两种方式处理：

**方式一：自定义变量跟踪（DSL 友好）**

```dsl
define "cg.mountain_unlocked" false once

label unlock_mountain:
  gallery_unlock "cg_mountain" title="山顶夕阳"
  set "cg.mountain_unlocked" true

// 在场景中检查
if {cg.mountain_unlocked}
  // 已解锁
else
  // 未解锁
```

**方式二：C# API 检查**

```csharp
bool isUnlocked = gameController.IsGalleryUnlocked("cg_mountain");
var allUnlocked = gameController.GetGalleryUnlocked();  // List<GalleryEntry>
```

## 鉴赏室场景

用自定义变量在 DSL 中控制显示：

```dsl
define "cg.mountain_unlocked" false once
define "cg.forest_unlocked" false once
define "cg.lighthouse_unlocked" false once

scene "gallery" type=menu
  image "Images/bg_gallery.jpg" x=0 y=0 width=100% height=100% opacity=0.3
  text "CG 鉴赏" x=50% y=8% size=36 color="#FFD700" halign=center

  // 3x3 网格布局
  hbox x=10% y=20% spacing=20
    vbox spacing=20
      if {cg.mountain_unlocked}
        button "" width=200 height=150 nav="cg_mountain_view"
        text "山顶夕阳" x=50% y=10% size=12 halign=center
      else
        image "Images/locked.png" width=200 height=150
        text "???" x=50% y=10% size=12 color="#666666" halign=center

    vbox spacing=20
      if {cg.forest_unlocked}
        button "" width=200 height=150 nav="cg_forest_view"
        text "森林晨雾" x=50% y=10% size=12 halign=center
      else
        image "Images/locked.png" width=200 height=150
        text "???" x=50% y=10% size=12 color="#666666" halign=center

    vbox spacing=20
      if {cg.lighthouse_unlocked}
        button "" width=200 height=150 nav="cg_lighthouse_view"
        text "灯塔之夜" x=50% y=10% size=12 halign=center
      else
        image "Images/locked.png" width=200 height=150
        text "???" x=50% y=10% size=12 color="#666666" halign=center

  button "返回" x=50% y=90% width=160 height=40 nav="title_main" halign=center
```

::: tip 解锁时同步变量
记得在 `gallery_unlock` 的同时设置自定义变量：
```dsl
gallery_unlock "cg_mountain" title="山顶夕阳"
set "cg.mountain_unlocked" true
```
:::

## CG 查看场景

```dsl
scene "cg_mountain_view" type=menu
  image "Images/cg_mountain.jpg" x=0 y=0 width=100% height=100%
  text "山顶夕阳" x=50% y=5% size=24 color="#FFD700" halign=center
  button "返回鉴赏室" x=50% y=92% width=200 height=40 nav="gallery" halign=center
```

## 批量管理

用变量和循环管理大量 CG：

```dsl
define "cg.total" 10 once
define "cg.unlocked" 0 once

// 每次解锁时更新计数
label unlock_cg:
  gallery_unlock "cg_new"
  set "cg.unlocked" {cg.unlocked + 1}
```

## C# 实现高级鉴赏室

对于复杂的鉴赏室 UI（分页、分类、缩略图），用 C# 实现：

```csharp
public class GalleryPanel : UserControl
{
    private readonly IGalleryService _galleryService;
    private readonly List<string> _allCgs = new()
    {
        "cg_mountain", "cg_forest", "cg_lighthouse",
        "cg_ending_hero", "cg_ending_villain"
    };

    public GalleryPanel(IGalleryService galleryService)
    {
        _galleryService = galleryService;
        BuildUi();
    }

    private void BuildUi()
    {
        var grid = new UniformGrid { Columns = 3, Rows = 3 };

        foreach (var cgId in _allCgs)
        {
            var isUnlocked = _galleryService.IsUnlocked(cgId);
            var button = new Button
            {
                Content = isUnlocked ? cgId : "???",
                IsEnabled = isUnlocked,
                Width = 200,
                Height = 150
            };
            button.Click += (_, _) => ViewCg(cgId);
            grid.Children.Add(button);
        }

        Content = grid;
    }

    private void ViewCg(string cgId)
    {
        // 导航到 CG 查看场景
    }
}
```

## 进度统计

```dsl
scene "gallery" type=menu
  text "CG 鉴赏" x=50% y=8% size=36 halign=center
  text "已解锁：{cg.unlocked} / {cg.total}" x=50% y=14% size=18 color="#88CCFF" halign=center
  // ... CG 网格
```

## 隐藏 CG

某些 CG 需要特殊条件解锁：

```dsl
label secret_scene:
  if {story.completed_all_endings && story.found_secret_item}
    image "Images/cg_secret.jpg" x=0 y=0 width=100% height=100%
    gallery_unlock "cg_secret"
    say "这是隐藏的 CG。" speaker="系统"
```

## 完整示例

```dsl
// 在剧情中解锁
label chapter1_climax:
  say "关键时刻！" speaker="旁白"
  transition "fade" duration=1.0
  image "Images/cg_chapter1.jpg" x=0 y=0 width=100% height=100%
  say "..." speaker="角色A"
  gallery_unlock "cg_chapter1"
  transition "fade" duration=1.0
  navigate "chapter2"

// 鉴赏室
scene "gallery" type=menu
  text "CG 鉴赏 ({cg.unlocked}/{cg.total})" x=50% y=10% halign=center
  // ... 网格
```
