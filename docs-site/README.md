# 灵泛引擎教程文档站

基于 VitePress 构建的官方教程文档站。

## 开发

```bash
# 安装依赖（使用 pnpm）
pnpm install

# 启动开发服务器（热更新）
pnpm docs:dev

# 构建静态站点
pnpm docs:build

# 预览构建产物
pnpm docs:preview
```

开发服务器默认运行在 `http://localhost:5173`。

## 目录结构

```
docs-site/
├── .vitepress/config.ts     # 站点配置
├── guide/                    # 快速入门（1h）
├── tutorial/                 # 完整教程（1-2 天）
├── cookbook/                 # 进阶专题
├── reference/                # API 参考
├── examples/                 # 配套可运行示例
└── index.md                  # 首页
```

## 技术栈

- **VitePress** 1.6.x — 静态站点生成器
- **pnpm** 11.x — 包管理器
- **Shiki** — 代码语法高亮

## 部署

### GitHub Pages（自动）

文档站通过 GitHub Actions 自动部署到 GitHub Pages。

**触发条件**：推送到 `main` 分支且改动涉及 `docs-site/` 目录。

**配置步骤**：
1. 仓库 Settings → Pages → Source 选择 "GitHub Actions"
2. 推送代码后，`.github/workflows/deploy-docs.yml` 自动构建并部署
3. 访问地址：`https://<用户名>.github.io/<仓库名>/`

### Gitee Pages（手动同步）

通过 `.github/workflows/sync-gitee.yml` 将仓库同步到 Gitee。

**配置步骤**：
1. 在 Gitee 创建同名仓库
2. 在 GitHub 仓库 Settings → Secrets 添加：
   - `GITEE_USERNAME` — Gitee 用户名
   - `GITEE_PRIVATE_KEY` — Gitee SSH 私钥
   - `GITEE_TOKEN` — Gitee API Token
3. 同步后在 Gitee 仓库设置中开启 Gitee Pages 服务

### 本地预览构建产物

```bash
pnpm docs:build
pnpm docs:preview
```

预览服务器默认运行在 `http://localhost:4173`。

### 自定义部署

构建产物在 `docs-site/.vitepress/dist/` 目录，可以部署到任何静态站点托管服务：

- Vercel
- Netlify
- Cloudflare Pages
- Nginx / Apache

## 写作规范

- 使用简体中文
- 代码示例用 ```dsl 代码块（DSL 语法）
- C# 代码用 ```csharp 代码块
- 每页保持 100-300 行
- 使用 `::: tip` / `::: warning` 等提示框
