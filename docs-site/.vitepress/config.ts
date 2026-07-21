import { defineConfig } from "vitepress";

// 部署到 GitHub Pages 项目站点（https://mofeng-02.github.io/LingFanEngine/），
// 必须设置 base 为仓库名，否则所有 /assets/* 资源会指向根路径而 404。
const base = "/LingFanEngine/";

export default defineConfig({
  base,
  title: "灵泛引擎",
  description: "高性能跨平台视觉小说 / 互动叙事引擎",
  lang: "zh-CN",
  lastUpdated: true,
  cleanUrls: true,

  head: [
    ["meta", { name: "theme-color", content: "#3c8772" }],
    // 浏览器标签页图标（head 中的 link 不会自动加 base，需手动拼前缀）
    [
      "link",
      { rel: "icon", href: `${base}LingFanIcon_64x64.png`, type: "image/png" },
    ],
  ],

  markdown: {
    lineNumbers: true,
    languageAlias: {
      dsl: "ini",
      renpy: "ini",
    },
  },

  themeConfig: {
    // 站点标识（侧边栏/导航栏左上角），VitePress 会自动加上 base 前缀
    logo: "/LingFanIcon_64x64.png",

    nav: [
      {
        text: "快速入门",
        link: "/guide/00-什么是灵泛引擎",
        activeMatch: "/guide/",
      },
      {
        text: "完整教程",
        link: "/tutorial/00-准备工作",
        activeMatch: "/tutorial/",
      },
      { text: "进阶专题", link: "/cookbook/", activeMatch: "/cookbook/" },
      { text: "API 参考", link: "/reference/dsl", activeMatch: "/reference/" },
    ],

    sidebar: {
      "/guide/": [
        {
          text: "快速入门",
          items: [
            { text: "什么是灵泛引擎", link: "/guide/00-什么是灵泛引擎" },
            { text: "安装与创建项目", link: "/guide/01-安装与创建项目" },
            { text: "第一个场景", link: "/guide/02-第一个场景" },
            { text: "让角色说话", link: "/guide/03-让角色说话" },
            { text: "分支选择", link: "/guide/04-分支选择" },
            { text: "打包发布", link: "/guide/05-打包发布" },
          ],
        },
      ],
      "/tutorial/": [
        {
          text: "基础篇",
          collapsed: false,
          items: [
            { text: "00 · 准备工作", link: "/tutorial/00-准备工作" },
            { text: "01 · 场景与 UI 元素", link: "/tutorial/01-场景与UI元素" },
            { text: "02 · 对话系统", link: "/tutorial/02-对话系统" },
            { text: "03 · 对话框模板", link: "/tutorial/03-对话框模板" },
            { text: "04 · 分支与导航", link: "/tutorial/04-分支与导航" },
            { text: "05 · 变量与表达式", link: "/tutorial/05-变量与表达式" },
          ],
        },
        {
          text: "视觉音频篇",
          collapsed: false,
          items: [
            {
              text: "06 · 背景立绘与视觉",
              link: "/tutorial/06-背景立绘与视觉",
            },
            { text: "07 · 过渡与动画", link: "/tutorial/07-过渡与动画" },
            { text: "08 · 音频系统", link: "/tutorial/08-音频系统" },
            { text: "09 · NVL 模式", link: "/tutorial/09-NVL模式" },
          ],
        },
        {
          text: "系统进阶篇",
          collapsed: false,
          items: [
            { text: "10 · 存档与回溯", link: "/tutorial/10-存档与回溯" },
            { text: "11 · 时间事件系统", link: "/tutorial/11-时间事件系统" },
            { text: "12 · 用 C# 扩展", link: "/tutorial/12-用C-Sharp扩展" },
            { text: "13 · 自定义 UI 面板", link: "/tutorial/13-自定义UI面板" },
            {
              text: "14 · 自定义对话框模板",
              link: "/tutorial/14-自定义对话框模板",
            },
          ],
        },
        {
          text: "发布篇",
          collapsed: false,
          items: [
            { text: "15 · 加密与发布", link: "/tutorial/15-加密与发布" },
            { text: "16 · 完整作品", link: "/tutorial/16-完整作品" },
          ],
        },
      ],
      "/cookbook/": [
        {
          text: "进阶专题",
          items: [
            { text: "小说世界模式", link: "/cookbook/小说世界模式" },
            {
              text: "如何实现好感度系统",
              link: "/cookbook/如何实现好感度系统",
            },
            { text: "如何做多结局", link: "/cookbook/如何做多结局" },
            { text: "如何做 CG 鉴赏", link: "/cookbook/如何做CG鉴赏" },
            { text: "如何做多语言", link: "/cookbook/如何做多语言" },
            { text: "如何做 Live2D 立绘", link: "/cookbook/如何做Live2D立绘" },
            {
              text: "如何迁移 Ren'Py 项目",
              link: "/cookbook/如何迁移RenPy项目",
            },
            { text: "性能优化技巧", link: "/cookbook/性能优化技巧" },
          ],
        },
      ],
      "/reference/": [
        {
          text: "API 参考",
          items: [
            { text: "DSL 语法参考", link: "/reference/dsl" },
            { text: "C# API 参考", link: "/reference/csharp-api" },
          ],
        },
      ],
    },

    search: {
      provider: "local",
    },

    outline: {
      label: "本页目录",
      level: [2, 3],
    },

    docFooter: {
      prev: "上一页",
      next: "下一页",
    },

    lastUpdatedText: "最后更新",

    returnToTopLabel: "回到顶部",
    sidebarMenuLabel: "菜单",
    darkModeSwitchLabel: "主题",
    lightModeSwitchTitle: "切换到浅色模式",
    darkModeSwitchTitle: "切换到深色模式",

    socialLinks: [
      { icon: "github", link: "https://github.com/MoFeng-02/LingFanEngine" },
    ],

    footer: {
      message: "基于 .NET 10 + Avalonia 12 · Apache-2.0 License",
      copyright: "Copyright © 2026 灵泛引擎",
    },
  },
});
