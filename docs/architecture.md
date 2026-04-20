---
layout: default
title: "架构总览"
description: "从组件划分、写入路径、查询路径到服务端控制面，理解 TSLite 当前的真实系统结构。"
permalink: /architecture/
---

## 组件划分

| 组件 | 责任 |
| --- | --- |
| `TSLite` | 嵌入式引擎，负责 schema、写入、查询、删除、后台 flush、compaction、retention |
| `TSLite.Data` | ADO.NET 提供程序，统一本地和远程模式 |
| `TSLite.Cli` | 命令行工具，适合脚本化执行和交互式 REPL |
| `TSLite.Server` | HTTP API、首次安装、认证授权、SSE、Admin UI、帮助文档 |
| `web/admin` | 产品首页、首次安装向导、登录与管理后台 |
| `docs` | JekyllNet 帮助站点源码 |

## 总体结构

```text
Application / Service / Tooling
        |
        +-- Tsdb + SqlExecutor
        +-- TsdbConnection / TsdbCommand
        +-- tslite CLI
        +-- HTTP / Admin UI
                |
                v
      SQL / TableDirect / Control Plane
                |
                v
      Query Engine / Auth / Registry / SSE
                |
                v
 WAL -> MemTable -> Flush -> Segment -> Compaction
```

## 写入路径

当前写入路径大致如下：

1. 通过 SQL `INSERT`、`Point.Create + WriteMany`、ADO.NET `TableDirect` 或 HTTP 批量端点进入系统
2. 写入先落到 WAL
3. 同步追加到 MemTable
4. 后台或显式触发 flush 时，将 MemTable 写成新的 immutable segment
5. Compaction 在后台合并旧 segment
6. Delete/Retention 通过 tombstone 参与查询过滤并由 compaction 消化

优点：

- 写入路径简单直接
- 崩溃恢复依赖 WAL replay
- 读写职责分离，segment 保持不可变

## 查询路径

查询侧主要由 `QueryEngine` 负责：

1. 解析 SQL 或 ADO.NET 命令
2. 根据 measurement schema 校验投影和过滤条件
3. 从 catalog 找到命中的 series
4. 合并 MemTable 与多个 segment 的候选数据
5. 应用 tombstone 过滤
6. 输出原始点结果或聚合结果

当前聚合支持：

- `count`
- `sum`
- `min`
- `max`
- `avg`
- `first`
- `last`

当前分组仅支持：

- `GROUP BY time(...)`

## 嵌入式与远程的关系

`TSLite.Data` 把两种运行方式统一成一套 ADO.NET API：

- 嵌入式模式：直接打开本地数据库目录
- 远程模式：通过 HTTP 调用 `TSLite.Server`

切换方式主要由连接字符串决定：

```text
Data Source=./demo-data
Data Source=tslite://./demo-data
Data Source=tslite+http://127.0.0.1:5080/metrics;Token=...
```

这意味着：

- 应用侧代码可以尽量少改
- 本地开发可以先跑嵌入式
- 需要运维、权限和后台时再切到服务端

## 服务端控制面

`TSLite.Server` 在引擎之上增加了一个控制面：

- 首次安装与 `installation.json`
- 用户、密码哈希、Token 与 `users.json`
- 数据库授权与 `grants.json`
- `/admin/` 前端管理界面
- `/help/` 文档站点
- `/v1/events` SSE 实时事件流
- `/healthz` 与 `/metrics`

服务端还通过 `TsdbRegistry` 管理多个数据库目录。

## 帮助文档与镜像

`docs/` 目录中的文档会在 Docker 构建时通过 JekyllNet 生成，并随 `TSLite.Server` 一起打包到镜像中，运行时挂在 `/help`。

这让镜像本身就携带：

- 产品介绍
- SQL 文档
- API 示例
- 部署说明

## 当前设计取向

- 嵌入式优先，而不是只做远程服务
- 强调 schema-first，而不是完全 schema-less
- 强调当前真实实现，而不是未来规划接口
- 数据库目录持久化优先于单文件目标
- 把帮助文档作为产品的一部分，而不是仓库外部说明
