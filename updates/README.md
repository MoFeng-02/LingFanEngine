# 引擎分发（NuGet，2026-09 起）

引擎三库（LingFanEngine / LingFanEngine.DslCore / LingFanEngine.Abstractions）以 **NuGet 包**分发：

- **源**：nuget.org（OIDC 受信任发布）+ GitHub Packages 镜像
- **发布**：推送 `v*` tag 触发 `.github/workflows/publish-engine.yml`，自动 `dotnet pack` + push
- **消费**：用户项目 csproj `PackageReference Include="LingFanEngine"`（传递携带 DslCore/Abstractions/Parlot），`dotnet restore` 自动还原
- **升级**：在项目里升级 PackageReference 版本（NuGet 自带版本管理与自愈，替代旧 engine.lock.json / DLL 热更 / 4-DLL zip 全套机制）

**前置配置（一次性）**：
1. nuget.org 账户 → Trusted Publishing → 为本仓库创建策略（owner=MoFeng-02 / repo=LingFanEngine / workflow=publish-engine.yml / environment=production）
2. 仓库 Secrets 配置 `NUGET_USER` = nuget.org 用户名（profile name，非邮箱）

**本地开发**：SDK 链为 ProjectReference 源码编译，无需本地 feed 即可构建/发布；仓库根 `nuget.config` 仅含 nuget.org 源。
模板/Demo 若需对标特定引擎版本，直接从 nuget.org 还原对应 `LingFanEngine` 包（或临时 `dotnet pack -o <本地目录>` 后还原）。

---
# 模板更新（GitHub Release）

SDK 内置模板独立更新机制：从 GitHub Release 拉取模板清单，下载并应用模板 zip，覆盖本地模板缓存，无需重新分发整包 SDK。官方更新模板时，用户一键即可获取最新模板。

## 工作原理

```
GitHub Release (LingFanEngine.Template.zip + template-latest.json)
        │  HTTPS（复用 engine-update 命名客户端，handler 池避免套接字耗尽）
        ▼
   TemplateUpdateService
   ├─ GET manifest → 版本比对（manifest.version > 本地当前模板版本即视为有更新）
   ├─ 下载模板 zip → 整包 sha256 校验
   ├─ 解压（排除 bin/obj/.vs/.git 等构建产物）
   └─ 覆盖本地模板缓存 template-cache/current/ → 写 template.lock.json（source=download）
        │
        ▼
   TemplateService.CreateProjectFromTemplateAsync 选择最佳源：
   ├─ 开发模式：src/Template/V1 目录（存在时优先）
   ├─ 分发模式：模板缓存 current/（若版本 > 内置基线 1.0.0）
   └─ 回退：内置嵌入 template.zip
```

## 关键设计

- **复用引擎更新架构**：HTTP 走同一 `engine-update` 命名客户端；manifest 主机白名单、`minSdkVersion` 校验、sha256 校验逻辑与引擎更新一致。
- **版本管理（模板独立版本）**：内置嵌入模板基线版本 `TemplateDefaults.BuiltinVersion = 1.0.0`。下载的模板写 `template.lock.json`（记录版本与来源）；`GetCachedTemplateDir()` 仅在缓存版本 **高于** 内置基线时才优先使用，避免旧下载覆盖随 SDK 升级的内置模板。
- **与内置模板关系**：模板缓存仅作为「覆盖内置嵌入模板」的源。开发模式下仍优先用 `src/Template/V1`（源码总是最新）。
- **离线友好**：未下载时回退内置嵌入 zip，功能不依赖联网。
- **安全**：manifest 主机白名单 + 整包 sha256 校验（与引擎更新同等信任边界）。Authenticode 校验对模板 zip 此次未强制（模板为文本/资源文件，非可执行 DLL；sha256 已足够防篡改）。

## manifest 格式（updates/template-latest.json）

```json
{
  "version": "1.1.0",
  "assetUrl": "https://github.com/MoFeng-02/LingFanEngine/releases/download/v1.1.0/LingFanEngine.Template.zip",
  "assetSha256": "<sha256>",
  "publishedUtc": "2026-07-20T00:00:00Z",
  "minSdkVersion": "0.1.5",
  "releaseNotesUrl": "https://github.com/MoFeng-02/LingFanEngine/releases/tag/v1.1.0"
}
```

## 发布一次模板更新的步骤

1. 将 `src/Template/V1` 目录打成 `LingFanEngine.Template.zip`（排除 bin/obj/.vs）
2. 计算 zip 的 sha256（`Get-FileHash LingFanEngine.Template.zip -Algorithm SHA256`）
3. 填写 `updates/template-latest.json`（version / assetUrl / assetSha256；可选 minSdkVersion / releaseNotesUrl）
4. GitHub 创建 Release（tag 如 `v1.1.0`），上传 `LingFanEngine.Template.zip` 作为 asset
5. 提交 `updates/template-latest.json` 到 main 分支

SDK 端点：设置页"检查模板更新"即可拉取并应用；之后新建项目将使用更新后的模板。

## 配置

| 项 | 位置 | 默认值 |
|---|---|---|
| manifest URL | `SdkSettings.TemplateUpdateManifestUrl` | `https://raw.githubusercontent.com/MoFeng-02/LingFanEngine/main/updates/template-latest.json` |
| 内置模板基线版本 | `TemplateDefaults.BuiltinVersion` | `1.0.0` |
| 模板缓存目录 | `PathHelper.GetTemplateCacheDirectory()` | `%LOCALAPPDATA%\LingFanEngine\template-cache\` |
| 缓存版本锁定 | `template-cache\template.lock.json` | — |

## 代码位置

- 接口：`src/SDK_Toolkit/LingFanEngine.SDK/Services/Abstractions/ITemplateUpdateService.cs`
- 实现：`src/SDK_Toolkit/LingFanEngine.SDK/Services/Implementations/TemplateUpdateService.cs`
- 模型：`src/SDK_Toolkit/LingFanEngine.SDK/Models/TemplateUpdateManifest.cs` / `TemplateLockFile.cs` / `TemplateUpdateResult.cs`
- 默认值：`src/SDK_Toolkit/LingFanEngine.SDK/Constants/TemplateDefaults.cs`
- 接入：`src/SDK_Toolkit/LingFanEngine.SDK/Services/Implementations/TemplateService.cs`（CreateProjectFromTemplateAsync 第三优先级源）
- DI 注册：`src/SDK_Toolkit/LingFanEngine.SDK/Extensions/ServiceCollectionExtensions.cs`
- UI 入口：设置页"检查模板更新"按钮 + 模板版本信息行
