---
layout: default
title: 文件格式与目录布局
description: 了解 TSLite 当前的磁盘组织方式、`.system` 控制面目录和服务端帮助文档的挂载位置。
permalink: /file-format/
---

## 服务端目录结构

`TSLite.Server` 的数据根目录通常长这样：

```text
<DataRoot>/
├─ .system/
├─ db-a/
├─ db-b/
└─ ...
```

其中：

- `.system/` 保存安装状态、用户、授权和 token
- 每个数据库目录下保存该库自己的 catalog、WAL、segments 和 tombstone manifest

## 数据库内部布局

一个数据库目录的典型布局如下：

```text
<database-root>/
├─ catalog.tslcat
├─ tombstones.tslmanifest
├─ wal/
│  └─ {startLsn:X16}.tslwal
└─ segments/
   └─ {id:X16}.tslseg
```

## 关键文件说明

| 文件 | 作用 |
| --- | --- |
| `catalog.tslcat` | 持久化 series catalog |
| `wal/*.tslwal` | 追加写 WAL，支持 segmented rolling |
| `segments/*.tslseg` | 不可变段文件 |
| `tombstones.tslmanifest` | 删除与 retention 信息 |

## `.system/` 控制面目录

首次安装相关的元数据保存在：

```text
<DataRoot>/.system/
```

当前至少会看到：

- `installation.json`
- `users.json`
- `grants.json`

当这个目录还没有完成初始化时，访问 `/admin/` 会先进入首次安装向导。

## 帮助文档在镜像中的位置

在 Docker 构建过程中，`docs/` 会通过 `JekyllNet` 生成静态站点，并被放进镜像内的：

```text
wwwroot/help/
```

运行时通过 `/help/*` 对外提供帮助文档。

## 兼容性提醒

核心二进制结构采用 little-endian，布局变更必须同步版本号与变更记录。对于旧格式文件，不应在没有迁移策略的情况下直接读取或覆盖。
