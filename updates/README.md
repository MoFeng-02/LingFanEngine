# 引擎 DLL 独立更新（GitHub Release）

SDK 内置引擎 DLL 独立更新机制：从 GitHub Release 拉取清单，下载并应用引擎 DLL，无需重新安装 SDK。

## 工作原理

```
GitHub Release (DLLs.zip + manifest.json)
        │  HTTPS（IHttpClientFactory 命名客户端，handler 池避免套接字耗尽）
        ▼
   EngineUpdateService
   ├─ GET manifest → 逐 DLL 版本比对（远端任一 DLL > 本地对应 DLL 即视为有更新）
   ├─ 下载 asset zip → 整包 sha256 校验
   ├─ 解压 → 逐 DLL sha256 校验
   └─ 应用
        ├─ UpdateProjectAsync → 用户项目 DLL/（4 个逐 DLL 覆盖，先杀游戏进程）
        └─ UpdateSdkCacheAsync → SDK/DLL/
             └─ 包内 4 个 DLL 是独立于 SDK 运行时、仅供用户项目再分发的载荷，
                不被 SDK 加载，故统一直接覆盖；仅外部进程偶发锁定时回落 pending，
                重启后由 ApplyPendingUpdatesAsync 应用（防御性兜底）
```

## 关键设计

- **IHttpClientFactory**：通过命名客户端 `engine-update` 获取 HttpClient，由工厂管理 handler 池，避免 DNS 变更/端口耗尽导致的套接字耗尽。**禁止**直接 `new HttpClient()`。
- **版本隔离（逐 DLL）**：更新判定不再只看 `LingFanEngine.dll` 一个版本，而是对 4 个 DLL 逐一比对——清单中**任一** DLL 版本严格大于本地对应 DLL 版本即触发更新。应用阶段（67.3）**仅覆盖版本真正变化的 DLL**（本地已最新的跳过），实现手术式更新、幂等、低风险。各更新目标（用户项目 / SDK 缓存）传入**自身**的 4-DLL 版本表做基线，互不误降级。
- **sha256 双校验 + Authenticode（67.6）**：整包 zip（`assetSha256`，可选）+ 逐 DLL（`dllChecksums`，必需）。任一不符拒绝应用。另对每个 DLL 做 Authenticode 代码签名校验：带签名则验证证书链（无效拒绝），无签名放行回落 sha256（CI 未配证书时不阻塞）。
- **manifest 主机白名单（67.6）**：仅允许 `raw.githubusercontent.com` / `github.com` / `*.github.io` 等官方主机拉取 manifest，非法主机拒绝（防恶意 manifest 投毒）。
- **最低 SDK 版本（67.4 v2）**：manifest 的 `minSdkVersion` 高于本地 SDK 版本时拒绝热更，提示先升级 SDK。
- **文件锁策略（修正）**：`SDK/DLL/` 内的 4 个 DLL 是**独立于 SDK 运行时、仅供用户项目再分发**的载荷——SDK 经 `ProjectReference` 编译期依赖引擎源码（其中 3 个被烤进 exe），**运行时不加载这些松散 DLL 文件**，因此更新时 4 个可统一直接覆盖，不存在「被 SDK 进程锁定」的情况。
  - 更新逻辑：`UpdateSdkCacheAsync` 对 4 个 DLL 统一直接覆盖（逐 DLL 粒度，本地已最新跳过；`breakingDlls` 列出者跳过）；仅当被**外部进程**（如杀软实时扫描、文件管理器预览）偶发锁定时，`TryCopyOrPending` 才回落写入 `%LOCALAPPDATA%\LingFanEngine\updates\pending\` + `pending.json`，SDK 下次启动时 `ApplyPendingUpdatesAsync` 应用（防御性兜底，罕见）。
  - `UpdateProjectAsync`（用户项目）则不同：用户项目的 DLL 在游戏运行时被加载，故先 `KillRunningProcesses` 杀掉占用进程再覆盖。
  - `BreakingAbstractions=true` 时拒绝热更，提示升级 SDK 整体（契约层变更须同步 SDK 编译期依赖）。
  - `breakingDlls`（v2）逐 DLL 破坏性标记：列出的 DLL 不可热更（需升级 SDK），同批次其它 DLL 仍可正常热替换。

## manifest.json 格式

发布到 `updates/latest.json`（默认拉取地址，可被 `SdkSettings.EngineUpdateManifestUrl` 覆盖）：

```json
{
  "version": "0.2.0",
  "sdkVersion": "0.1.5",
  "releaseNotesUrl": "https://github.com/MoFeng-02/LingFanEngine/releases/tag/v0.2.0",
  "assetUrl": "https://github.com/MoFeng-02/LingFanEngine/releases/download/v0.2.0/LingFanEngine.DLLs.zip",
  "assetSha256": "",
  "breakingAbstractions": false,
  "breakingDlls": [],
  "publishedUtc": "2026-07-20T00:00:00Z",
  "minSdkVersion": "0.1.5",
  "dllChecksums": {
    "LingFanEngine.dll": "<sha256>",
    "LingFanEngine.Abstractions.dll": "<sha256>",
    "LingFanEngine.DslCore.dll": "<sha256>",
    "Pidgin.dll": "<sha256>"
  },
  "dllVersions": {
    "LingFanEngine.dll": "0.2.0",
    "LingFanEngine.Abstractions.dll": "0.2.0",
    "LingFanEngine.DslCore.dll": "0.2.0",
    "Pidgin.dll": "0.2.0"
  }
}
```

`dllVersions` 为**可选**逐 DLL 版本表：
- 提供时，比对按各 DLL 自身版本进行（支持「只发某个依赖的小版本」）。
- 不提供时，回落到全局 `version` 对所有 4 个 DLL 比对。
- 缺失的 DLL 视为 `"0.0.0"`，会被任何远端版本判定为「需更新」。

`version` 作为全局兜底版本，两种模式下都参与：未提供 `dllVersions` 时即用它比对全部 DLL。

**v2 新增字段**（均可选）：
- `breakingDlls`（`string[]`）：逐 DLL 破坏性标记。列出的 DLL 不可热更（需升级 SDK），同批次其它 DLL 仍可热替换。与全局 `breakingAbstractions` 互补，实现更精细的兼容性控制。
- `publishedUtc`（`string`）：发布时间（UTC ISO8601），供 UI 展示。
- `minSdkVersion`（`string`）：所需最低 SDK 版本（X.Y.Z）。本地 SDK 版本低于此值拒绝热更，提示先升级 SDK。

## 发布一次更新的步骤

1. 编译引擎核心（Release）：`dotnet build src/EngineCore/LingFanEngine -c Release`
2. 将 4 个 DLL 打成 `LingFanEngine.DLLs.zip`：
   - `LingFanEngine.dll`（EngineCore 输出）
   - `LingFanEngine.Abstractions.dll`
   - `LingFanEngine.DslCore.dll`
   - `Pidgin.dll`
3. 计算每个 DLL 的 sha256（PowerShell）：
   ```powershell
   Get-FileHash LingFanEngine.dll -Algorithm SHA256 | % { $_.Hash.ToLower() }
   ```
4. 填写 `updates/latest.json`（version / assetUrl / dllChecksums / breakingAbstractions；可选 dllVersions 声明逐 DLL 版本）
5. GitHub 创建 Release（tag 如 `v0.2.0`），上传 `LingFanEngine.DLLs.zip` 作为 asset
6. 提交 `updates/latest.json` 到 main 分支

SDK 端点"设置 → 检查引擎更新"即可拉取并应用。

## 安全

下载并执行 DLL = 代码执行信任边界。当前校验链：

- manifest 与 asset 均走 HTTPS
- manifest URL **主机白名单**校验：仅官方 GitHub 主机可拉取，非法主机拒绝
- 逐 DLL sha256 比对（manifest 提供，必需）
- 整包 sha256（可选）
- **Authenticode 代码签名校验**：带签名则验证证书链（无效拒绝），无签名放行回落 sha256（CI 未配证书时不阻塞发布）；非 Windows 平台自动放行

## 发布一次更新的步骤

1. 编译引擎核心（Release）：`dotnet build src/EngineCore/LingFanEngine -c Release`
2. 将 4 个 DLL 打成 `LingFanEngine.DLLs.zip`：
   - `LingFanEngine.dll`（EngineCore 输出）
   - `LingFanEngine.Abstractions.dll`
   - `LingFanEngine.DslCore.dll`
   - `Pidgin.dll`
3. 计算每个 DLL 的 sha256（PowerShell）：
   ```powershell
   Get-FileHash LingFanEngine.dll -Algorithm SHA256 | % { $_.Hash.ToLower() }
   ```
4. 填写 `updates/latest.json`（version / assetUrl / dllChecksums / breakingAbstractions；可选 dllVersions 声明逐 DLL 版本；可选 v2 字段 breakingDlls / publishedUtc / minSdkVersion）
5. GitHub 创建 Release（tag 如 `v0.2.0`），上传 `LingFanEngine.DLLs.zip` 作为 asset
6. 提交 `updates/latest.json` 到 main 分支

> 自动化：`.github/workflows/publish-engine.yml`（**DRAFT / 未经实跑验证**）可在推送 `v*` tag 时自动构建 4 DLL、计算 sha256、生成 `latest.json` 并发布 Release。提交前请人工复核路径与权限。

SDK 端点：
- 设置页"检查引擎更新" → `UpdateSdkCacheAsync`（刷新 SDK 包内 DLL/ 再分发载荷 + 引擎缓存，4 个独立 DLL 统一覆盖）
- 构建页"引擎依赖"分区"检查并更新项目引擎" → `UpdateProjectAsync`（更新用户项目 DLL，仅覆盖版本变化的 DLL，需重新构建生效）
- 设置页"检查模板更新" → `UpdateTemplateAsync`（下载模板 zip → sha256 校验 → 解压覆盖模板缓存 `template-cache/current/`，之后新建项目优先用缓存模板）

## 配置

| 项 | 位置 | 默认值 |
|---|---|---|
| manifest URL | `SdkSettings.EngineUpdateManifestUrl` | `https://raw.githubusercontent.com/MoFeng-02/LingFanEngine/main/updates/latest.json` |
| manifest 主机白名单 | `EngineUpdateDefaults.AllowedManifestHosts` | `raw.githubusercontent.com` / `github.com` / `github.io`（含子域） |
| HTTP 超时 | `EngineUpdateDefaults.RequestTimeoutSeconds` | 60s |
| 工作目录 | `%LOCALAPPDATA%\LingFanEngine\updates\` | — |
| pending 清单 | `updates\pending.json` | — |

## 代码位置

- 接口：`src/SDK_Toolkit/LingFanEngine.SDK/Services/Abstractions/IEngineUpdateService.cs`
- 实现：`src/SDK_Toolkit/LingFanEngine.SDK/Services/Implementations/EngineUpdateService.cs`
- 模型：`src/SDK_Toolkit/LingFanEngine.SDK/Models/EngineUpdateManifest.cs` / `EngineUpdateResult.cs` / `PendingUpdateManifest.cs`
- 默认值：`src/SDK_Toolkit/LingFanEngine.SDK/Constants/EngineUpdateDefaults.cs`
- DI 注册：`src/SDK_Toolkit/LingFanEngine.SDK/Extensions/ServiceCollectionExtensions.cs`（`AddHttpClient` + 单例服务）
- 启动 pending 应用：`src/SDK_Toolkit/LingFanEngine.SDK.Desktop/Program.cs`
- UI 入口：设置页"检查引擎更新"按钮 + 构建页"引擎依赖"分区按钮

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
