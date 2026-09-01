# LSP 语言服务

灵泛引擎提供符合 [Language Server Protocol (LSP)](https://microsoft.github.io/language-server-protocol/) 标准的 DSL 语言服务，为 `.story` 文件提供智能编辑体验。

LSP 是**编辑器无关**的标准协议——同一份语言服务可接入 VS Code、Neovim、Vim、Helix、Sublime Text 等任何支持 LSP 的编辑器。

## 功能清单

| 功能 | 说明 |
|:---|:---|
| **智能补全** | 行首关键字、参数名、资源路径、变量名、布尔值、枚举值、Grid 属性、元素专属属性 |
| **语义高亮** | 29 种分类着色：控制流/导航/数据操作/媒体/显示/UI元素/存档/章节/回溯/时间事件等 |
| **诊断** | 未定义变量、未找到资源、未注册命令、重复场景定义 |
| **跳转定义** | F12 跳转到变量/场景/标签/资源的定义位置 |
| **查找引用** | Shift+F12 查找变量/场景/标签的所有引用 |
| **重命名** | F2 跨文件重命名变量/场景/标签 |
| **格式化** | 缩进归一化（Tab/空格切换）、去除行尾空白、注释保留 |
| **折叠** | 场景块/if/while/for/func/switch/menu 等块结构折叠 |
| **悬停提示** | 关键字文档、符号类型与作用域、资源信息 |
| **文档符号** | 场景/标签/函数/变量/角色/样式定义树 |
| **工作区符号** | Ctrl+T 跨文件模糊搜索符号 |

### 补全分类

补全项按语义分类显示，带有图标区分：

| 图标 | 分类 | 示例 |
|:---|:---|:---|
| 🏷️ | 关键字（控制流） | `if` / `while` / `for` / `break` |
| 🏷️ | 关键字（导航） | `scene` / `navigate` / `jump` |
| 🏷️ | 关键字（数据） | `set` / `define` / `let` |
| 🏷️ | 关键字（媒体） | `bgm` / `se` / `video` |
| 🏷️ | 关键字（显示） | `transition` / `show` / `hide` |
| 🏷️ | 关键字（存档） | `save` / `load` / `auto_save` |
| 🏷️ | 关键字（时间） | `time_event` / `skip_time` |
| 🏷️ | 关键字（Live2D） | `live2d_char` / `live2d_show` |
| 📦 | UI 元素 | `text` / `button` / `image` / `grid` |
| ⚙️ | 参数名 | `width` / `opacity` / `col` / `row` |
| 🔤 | 变量名 | `player.gold` / `_local_temp` |
| 📂 | 资源路径 | `Audio/bgm.mp3` / `Images/hero.png` |
| ✅ | 枚举值 | `true` / `false` / `fade` / `center` |

## 安装

### 方式一：预编译二进制（推荐）

从 GitHub Releases 下载对应平台的 LSP 二进制：

| 平台 | 文件名 |
|:---|:---|
| Windows x64 | `LingFan.Dsl.LanguageServer-win-x64.exe` |
| macOS x64 | `LingFan.Dsl.LanguageServer-osx-x64` |
| macOS ARM64 | `LingFan.Dsl.LanguageServer-osx-arm64` |
| Linux x64 | `LingFan.Dsl.LanguageServer-linux-x64` |

下载后将二进制放在任意目录，配置编辑器时指向该路径即可。

::: tip AOT 编译
LSP 二进制采用 .NET Native AOT 编译，无需安装 .NET 运行时，开箱即用。
:::

### 方式二：从源码构建

```bash
cd src/EngineCore

# Windows
dotnet publish LingFanEngine.Dsl.LanguageServer/LingFanEngine.Dsl.LanguageServer.csproj \
  -c Release -r win-x64 -p:PublishAot=true -p:PublishSingleFile=true

# macOS x64
dotnet publish LingFanEngine.Dsl.LanguageServer/LingFanEngine.Dsl.LanguageServer.csproj \
  -c Release -r osx-x64 -p:PublishAot=true -p:PublishSingleFile=true

# macOS ARM64
dotnet publish LingFanEngine.Dsl.LanguageServer/LingFanEngine.Dsl.LanguageServer.csproj \
  -c Release -r osx-arm64 -p:PublishAot=true -p:PublishSingleFile=true

# Linux x64
dotnet publish LingFanEngine.Dsl.LanguageServer/LingFanEngine.Dsl.LanguageServer.csproj \
  -c Release -r linux-x64 -p:PublishAot=true -p:PublishSingleFile=true
```

产物位于 `LingFanEngine.Dsl.LanguageServer/bin/Release/net10.0/<RID>/publish/`。

## 编辑器配置

### VS Code

1. 安装 [LSP Client 扩展](https://marketplace.visualstudio.com/items?itemName=ms-vscode.vscode-language-server-protocol)（或使用内置 LSP 支持）
2. 在 `settings.json` 中配置：

```json
{
  "lingfan.dsl.server.path": "${workspaceFolder}/bin/LingFan.Dsl.LanguageServer",
  "lingfan.dsl.enable": true
}
```

将 `server.path` 替换为 LSP 二进制的实际路径。打开 `.story` 文件即可自动启动语言服务。

### Neovim (nvim-lspconfig)

```lua
-- init.lua 或 lspconfig 配置
local lspconfig = require('lspconfig')

lspconfig.lingfan_dsl.setup {
  cmd = { '/usr/local/bin/LingFan.Dsl-LanguageServer' },
  filetypes = { 'story' },
  root_dir = lspconfig.util.root_pattern('.git', 'Stories'),
}
```

文件类型关联：

```lua
vim.filetype.add {
  extension = { story = 'story' },
}
```

### Vim (vim-lsp)

```vim
" .vimrc
autocmd BufRead,BufNewFile *.story setfiletype story

if executable('LingFan.Dsl-LanguageServer')
  au User lsp_setup call lsp#register_server({
    \ 'name': 'lingfan-dsl',
    \ 'cmd': { server_info -> ['LingFan.Dsl-LanguageServer'] },
    \ 'whitelist': ['story'],
    \ })
endif
```

### Helix

```toml
# ~/.config/helix/languages.toml
[[language]]
name = "lingfan-dsl"
language-servers = ["lingfan-dsl"]

[language-server.lingfan-dsl]
command = "LingFan.Dsl-LanguageServer"
```

### Sublime Text (LSP 插件)

1. 安装 [LSP](https://packagecontrol.io/packages/LSP) 包
2. `Preferences > Package Settings > LSP > Settings` 添加：

```json
{
  "clients": {
    "lingfan-dsl": {
      "command": ["LingFan.Dsl-LanguageServer"],
      "selector": "source.story",
      "enabled": true
    }
  }
}
```

## 传输协议

LSP 语言服务使用标准的 **stdin/stdout** 传输：

```
编辑器 ←→ stdin/stdout ←→ LingFan.Dsl.LanguageServer
```

- 编辑器通过 stdin 发送 JSON-RPC 请求/通知
- 语言服务通过 stdout 返回响应
- 日志输出到 stderr（不影响协议通信）
- 无需网络端口，本地进程通信

## 已知限制

| 限制 | 说明 |
|:---|:---|
| 单根工作区 | 当前仅支持单个项目根目录，多根工作区的符号索引不隔离 |
| 大文件性能 | 超过 5000 行的单文件首次补全可能有短暂延迟（后台索引后恢复） |
| 无调试支持 | 尚未集成 Debug Adapter Protocol (DAP)，不支持断点/单步调试 |
| 无代码片段 | 尚未实现 Snippet 补全（如 `scene` 块模板、`if` 块模板） |
| 无 SignatureHelp | 函数参数签名提示尚未实现（如 `random(min, max)`） |
| 无代码动作 | Quick Fix / Refactoring 尚未实现 |
