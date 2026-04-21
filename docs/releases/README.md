---
layout: default
title: 发布与打包
description: 了解 NuGet、SDK Bundle、Server Bundle 和安装包的组成与默认启动方式。
permalink: /releases/
---

TSLite 当前的发布物主要分为五类：

| 类型 | 产物 | 说明 |
| --- | --- | --- |
| NuGet | `TSLite.*.nupkg` | 嵌入式核心库、远程 ADO.NET 接入与 CLI 工具包 |
| SDK Bundle | `tslite-sdk-<version>-<rid>` | 面向开发者，包含 NuGet 包、本地 CLI 与配套文档 |
| Server Bundle | `tslite-server-full-<version>-<rid>` | 面向部署者，包含 `TSLite.Server`、前端、CLI 与默认启动配置 |
| Installer | `.msi` / `.deb` / `.rpm` | 面向最终安装的操作系统包 |
| Docker Image | `iotsharp/tslite-server` / `ghcr.io/<owner>/tslite-server` | 面向容器化部署的服务端镜像，包含后台、帮助中心与默认运行配置 |

## 默认启动信息

完整服务端发布物通常默认监听：

```text
http://127.0.0.1:5080
```

常见入口包括：

- `/admin/`
- `/help/`
- `/healthz`
- `/metrics`

## 推荐阅读顺序

1. [SDK Bundle]({{ site.docs_baseurl | default: '/help' }}/releases/sdk-bundle/)
2. [Server Bundle]({{ site.docs_baseurl | default: '/help' }}/releases/server-bundle/)
3. [安装包]({{ site.docs_baseurl | default: '/help' }}/releases/installers/)
4. [Docker 镜像]({{ site.docs_baseurl | default: '/help' }}/releases/docker-image/)
