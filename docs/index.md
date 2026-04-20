---
layout: default
title: TSLite 文档中心
description: 面向部署者、开发者和管理员的 TSLite 帮助文档，覆盖首次安装、SQL、文件布局和发布内容。
permalink: /
---

TSLite 是一个使用 C# / .NET 10 编写的嵌入式单文件时序数据库，同时提供了 `TSLite.Server` 作为 HTTP 服务形态。当前帮助中心围绕三条主线组织：

- 首次安装与运维入口
- 数据模型、SQL 和批量写入
- 服务端发布、打包和文件布局

<div class="hero-link-row">
  <a class="hero-link hero-link-primary" href="/admin/">打开管理界面</a>
  <a class="hero-link hero-link-secondary" href="/help/getting-started/">阅读开始使用</a>
</div>

<div class="callout-grid">
  <section class="callout-card">
    <strong>首次安装</strong>
    <p>当数据目录下的 <code>.system</code> 为空时，访问 <code>/admin</code> 会进入首次安装向导，设置组织、服务器 ID、管理员账号和首个 Bearer Token。</p>
  </section>
  <section class="callout-card">
    <strong>统一管理</strong>
    <p>安装完成后，继续在同一套后台里管理数据库、用户、授权、Token、SQL Console 和实时事件流。</p>
  </section>
  <section class="callout-card">
    <strong>嵌入式内核</strong>
    <p>核心库保持零第三方运行时依赖，存储布局由 catalog、WAL、segments 和 tombstone manifest 组成。</p>
  </section>
</div>

## 快速入口

| 主题 | 说明 |
| --- | --- |
| [开始使用](/help/getting-started/) | 首次安装、Docker 运行、登录和管理入口 |
| [数据模型](/help/data-model/) | measurement、tags、fields、timestamp 与数据库层级 |
| [SQL 参考](/help/sql-reference/) | `CREATE MEASUREMENT`、`INSERT`、`SELECT`、`DELETE`、批量写入 |
| [文件格式](/help/file-format/) | `catalog.tslcat`、`wal/*.tslwal`、`segments/*.tslseg`、`.system/` |
| [发布与打包](/help/releases/) | SDK Bundle、Server Bundle、安装包和默认启动信息 |

## 当前服务形态

TSLite 现在同时支持两种主要使用方式：

1. 作为进程内嵌入式数据库，通过 `TSLite` 与 `TSLite.Data` 接入。
2. 作为 `TSLite.Server` 运行，通过 HTTP API、管理后台和 SSE 事件流对外提供能力。

## 导航建议

- 如果你正在第一次部署服务器，先看 [开始使用](/help/getting-started/)。
- 如果你准备接入业务数据，接着看 [数据模型](/help/data-model/) 和 [SQL 参考](/help/sql-reference/)。
- 如果你在排查存储结构、打包产物或迁移问题，再看 [文件格式](/help/file-format/) 与 [发布与打包](/help/releases/)。
