---
layout: default
title: 开始使用
description: 从首次安装到进入管理后台的最短路径，包括 Docker 启动、初始化和登录方式。
permalink: /getting-started/
---

## 1. 启动 `TSLite.Server`

如果你通过 Docker 运行服务器，容器会默认监听 `5080` 端口，并把数据目录映射到 `/data`：

```bash
docker run -p 5080:5080 -v ./tslite-data:/data tslite-server
```

如果你使用的是完整发布包或安装包，直接启动 `TSLite.Server` 可执行文件即可。

## 2. 打开管理入口

浏览器访问：

```text
http://127.0.0.1:5080/admin/
```

当 `<DataRoot>/.system` 为空时，后台不会直接落到登录页，而是进入首次安装流程。

## 3. 完成首次安装

首次安装需要设置以下内容：

- 服务器 ID
- 组织名称
- 管理员用户名
- 管理员密码
- 首个静态 Bearer Token

初始化完成后，系统会在 `<DataRoot>/.system/` 下写入安装元数据、用户和 token 信息。

## 4. 登录与访问控制

当前有两种常见访问方式：

- 用户名 + 密码，通过 `POST /v1/auth/login` 获取动态 Bearer Token
- 直接使用初始化时设置的静态 Bearer Token 调用 API

管理后台本身匿名可访问，但实际管理操作仍然通过登录后的 API 授权完成。

## 5. 推荐的首次验证

初始化完成后，建议依次完成这几个动作：

1. 在管理后台创建一个数据库。
2. 打开 SQL Console，执行一次 `CREATE MEASUREMENT`。
3. 写入一批测试点位，再执行 `SELECT` 验证读取。
4. 查看 Dashboard 和 Events 页面，确认实时指标与事件流正常。

## 常用端点

| 地址 | 用途 |
| --- | --- |
| `/admin/` | 产品首页、首次安装、登录和后台入口 |
| `/help/` | 帮助中心 |
| `/healthz` | 健康检查 |
| `/metrics` | Prometheus 指标 |
| `/v1/setup/status` | 查询是否需要首次安装 |
| `/v1/setup/initialize` | 执行首次安装 |

## 数据目录概览

一个典型的数据目录如下：

```text
<DataRoot>/
├─ .system/
├─ <database-a>/
├─ <database-b>/
└─ ...
```

其中 `.system/` 保存服务端控制面的元数据，其余子目录对应数据库实例。
