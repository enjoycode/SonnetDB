---
layout: default
title: "文件格式与目录布局"
description: "TSLite 当前真实的磁盘布局，包括数据库目录、.system 控制面目录和帮助文档挂载位置。"
permalink: /file-format/
---

## 服务端数据根目录

`TSLite.Server` 的数据根目录通常如下：

```text
<DataRoot>/
├─ .system/
├─ metrics/
├─ telemetry/
└─ ...
```

其中：

- `.system/` 用于服务端控制面
- 其余子目录各自代表一个数据库实例

## 数据库目录布局

每个数据库目录当前的真实布局如下：

```text
<database-root>/
├─ catalog.tslcat
├─ measurements.tslschema
├─ tombstones.tslmanifest
├─ wal/
│  └─ {startLsn:X16}.tslwal
└─ segments/
   └─ {id:X16}.tslseg
```

关键文件说明：

| 文件 | 作用 |
| --- | --- |
| `measurements.tslschema` | measurement schema 集合 |
| `catalog.tslcat` | series catalog |
| `tombstones.tslmanifest` | 删除与 retention 的 tombstone 清单 |
| `wal/*.tslwal` | 分段 WAL 文件 |
| `segments/*.tslseg` | 不可变数据段 |

## 与旧描述的差异

当前版本需要明确：

- 数据库存储不是单个 `.tsl` 文件
- schema、catalog、WAL、segments、tombstones 分别落在不同文件中
- 数据库的最小持久化单位是“数据库目录”

## WAL 兼容性说明

当前运行时使用分段 WAL：

```text
wal/{startLsn:X16}.tslwal
```

仓库中仍保留对旧 `wal/active.tslwal` 形态的兼容升级逻辑，旧目录在打开时会自动迁移到当前布局。

## `.system/` 控制面目录

服务端控制面数据保存在：

```text
<DataRoot>/.system/
├─ installation.json
├─ users.json
└─ grants.json
```

它们分别负责：

- `installation.json`：首次安装元数据，例如服务器 ID、组织、初始管理员和初始 token id
- `users.json`：用户、密码哈希、已签发 token 的摘要与哈希
- `grants.json`：数据库级授权

当 `.system/` 为空或尚未完成初始化时，访问 `/admin/` 会进入首次安装流程。

## 帮助文档在镜像中的位置

`docs/` 会在 Docker 构建阶段通过 JekyllNet 生成静态站点，并打包到镜像中的：

```text
wwwroot/help/
```

运行时通过 `/help/*` 对外提供帮助文档。

## 小结

如果你在排查启动、迁移、备份或容器挂载问题，请优先把注意力放在：

- `.system/`
- 各数据库子目录
- `measurements.tslschema`
- `catalog.tslcat`
- `wal/`
- `segments/`
- `tombstones.tslmanifest`
