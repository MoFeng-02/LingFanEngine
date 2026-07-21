# 灵泛引擎 LingFanEngine v1.0.0 发布说明

> 本文档中的能力描述均经代码核实，与 `docs-site` 文档红线保持一致。

## 质量状态

### 单元测试
共 **206** 项单元测试，全部通过：

| 模块 | 数量 |
|------|------|
| DSL 引擎 | 101 |
| 状态容器 | 31 |
| 存档 | 19 |
| 回滚 | 18 |
| 表达式 | 21 |
| 命令管道 | 7 |
| 加密 | 7 |
| 时间事件注册 | 1 |
| 存档 dump | 1 |

### 测试覆盖率
- 本版已接入 `coverlet.collector` 收集：本地执行 `dotnet test --collect:"XPlat Code Coverage"` 即可产出 Cobertura 覆盖率报告（`TestResults/**/coverage.cobertura.xml`）。
- 当前整体覆盖率：**行约 20.6% / 分支约 11.1%**。核心逻辑模块（DSL 解析、状态容器、存档/加密、表达式、命令管道）覆盖充分；UI / 视图层（Avalonia 渲染代码）因难以进行单元测试未纳入，故整体数字偏低。
- **工程尚未配置强制阈值门禁**（CI 无覆盖率卡点）。计划在质量保障阶段针对核心逻辑模块引入阈值，而非对整体数字设硬线。

### CI
- 发版流水线 `publish-engine.yml`（tag `v*` 触发）：构建 4 个引擎库 → 打包 `LingFanEngine.DLLs.zip` + `updates/latest.json` → 发布三平台 SDK（win-x64 / linux-x64 / osx-arm64，AOT self-contained）→ 打包并发布模板 `LingFanEngine.Template.zip` + `updates/template-latest.json` → 汇总创建 GitHub Release。
- 文档站 `deploy-docs.yml`（`push main` + 改动 `docs-site/**` 触发）：VitePress 构建并部署到 GitHub Pages 项目站点 `https://mofeng-02.github.io/LingFanEngine/`。

## 布局能力

- ✅ **网格布局已实现**：`grid` 二维网格容器。DSL 写法 `grid columns=N rows=M { … }`，子元素使用 `col` / `row` / `colspan` / `rowspan` 定位（`ControlFactory` 已实现 `Grid` 的行列与跨列附着属性）。
- 可用容器类型：`panel` / `vbox` / `hbox` / `grid` / `container` / `scrollview`。
- `fixed` 是**场景绝对定位布局模式**（`scene layout=fixed`），不是独立容器类型，请勿与 `grid` / `vbox` / `hbox` 并列理解为容器。
- 注：SDK 编辑器关键字高亮集合 `DslKeywords.UiElementTypes` 已于本版补入 `grid`，此前 `grid` 会被误标为未知元素（运行时本就可用）。

## 已知限制（与文档红线一致）

- **WASM / Browser**：⚠️ 实验性（待测试）。
- **Live2D**：管线 / 接口已实现，视觉渲染控件待接入（Cubism 授权未到位）。
- **模板更新**：模板版本独立于引擎 tag（来源 `src/Template/template-meta.json`），仅模板内容变化才 bump 版本，避免每次引擎发版触发重复下载；SDK 自动更新支持 GitHub 主源 + Gitee 镜像回退。
- **退出 / 存档 API**：引擎无统一 `Exit` API（退出由宿主层负责）；无 `AutoSaveAsync` 方法，自动存档通过状态键 `__auto_save` 实现。
