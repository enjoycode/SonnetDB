---
layout: default
title: "CLI 参考"
description: "tslite 命令行工具的安装、命令和本地/远程示例。"
permalink: /cli-reference/
---

## 安装

作为全局工具：

```bash
dotnet tool install --global TSLite.Cli --version 0.1.0
```

如果你在仓库源码里直接运行，也可以使用：

```bash
dotnet run --project src/TSLite.Cli -- version
```

## 命令速览

```text
tslite version
tslite sql     --connection "<conn>" (--command "<sql>" | --file ./q.sql)
tslite repl    --connection "<conn>"

tslite local   --path ./data [--save-profile home] [--default] [--command "<sql>" | --file ./q.sql | --repl]
tslite local   --profile home [--command "<sql>" | --file ./q.sql | --repl]
tslite local   --use-default [--command "<sql>" | --file ./q.sql | --repl]
tslite local   list
tslite local   remove --profile home

tslite remote  --url http://127.0.0.1:5080 --database db [--token t] [--timeout 30] [--save-profile dev] [--default] [--command "<sql>" | --file ./q.sql | --repl]
tslite remote  --profile dev [--command "<sql>" | --file ./q.sql | --repl]
tslite remote  --use-default [--command "<sql>" | --file ./q.sql | --repl]
tslite remote  list
tslite remote  remove --profile dev

tslite connect <profile-name> [--command "<sql>" | --file ./q.sql | --repl]
tslite connect --default      [--command "<sql>" | --file ./q.sql | --repl]
```

---

## `version`

```bash
tslite version
```

---

## `local`

### 直接使用路径

输出连接字符串：

```bash
tslite local --path ./demo-data
```

执行 SQL：

```bash
tslite local --path ./demo-data --command "SELECT count(*) FROM cpu"
```

进入 REPL：

```bash
tslite local --path ./demo-data --repl
```

### 保存 local profile

```bash
tslite local --path ./demo-data --save-profile home --default
```

列出已保存的 local profile：

```bash
tslite local list
```

使用 profile：

```bash
tslite local --profile home --command "SELECT count(*) FROM cpu"
tslite local --use-default --repl
```

删除 profile：

```bash
tslite local remove --profile home
```

---

## `remote`

### 直接连接

输出连接字符串：

```bash
tslite remote \
  --url http://127.0.0.1:5080 \
  --database metrics \
  --token your-token
```

执行 SQL：

```bash
tslite remote \
  --url http://127.0.0.1:5080 \
  --database metrics \
  --token your-token \
  --command "SHOW DATABASES"
```

进入 REPL：

```bash
tslite remote \
  --url http://127.0.0.1:5080 \
  --database metrics \
  --token your-token \
  --repl
```

### 保存 remote profile

```bash
tslite remote \
  --url http://127.0.0.1:5080 \
  --database metrics \
  --token your-token \
  --save-profile dev \
  --default
```

列出 / 使用 / 删除：

```bash
tslite remote list
tslite remote --profile dev --command "SHOW DATABASES"
tslite remote --use-default --repl
tslite remote remove --profile dev
```

---

## `connect`

`connect` 是统一快捷入口，按名称在 local/remote 两个 profile 列表中查找（local 优先）并分发。

```bash
# 使用名为 "home" 的 local profile
tslite connect home

# 使用名为 "dev" 的 remote profile，并进入 REPL
tslite connect dev --repl

# 使用默认 profile 执行 SQL
tslite connect --default --command "SELECT count(*) FROM cpu"
```

---

## `sql` / `repl`（兼容原有用法）

```bash
tslite sql \
  --connection "Data Source=./demo-data" \
  --command "SELECT count(*) FROM cpu"

tslite sql \
  --connection "Data Source=tslite+http://127.0.0.1:5080/metrics;Token=your-token" \
  --file ./query.sql

tslite repl --connection "Data Source=./demo-data"
```

---

## profile 文件

所有 profile 保存在：

```text
~/.tslite/profiles.json
```

文件结构示例：

```json
{
  "defaultProfile": "home",
  "profiles": [
    { "name": "dev", "baseUrl": "http://127.0.0.1:5080", "database": "metrics", "token": "...", "timeout": 30 }
  ],
  "localProfiles": [
    { "name": "home", "path": "/data/demo" }
  ]
}
```

---

## 输出形式

| 情况 | 输出 |
| --- | --- |
| 非查询 SQL | `OK (n rows affected)` |
| 查询 SQL | 文本表格 + `(n row(s))` |
| `local` / `remote` 无 SQL 也无 `--repl` | 打印连接字符串 |
| `local list` / `remote list` | profile 列表，默认项前带 `*` |

---

## 连接字符串

`sql` / `repl` 命令与 ADO.NET 使用同一套连接字符串：

- 本地：`Data Source=./demo-data`
- 远程：`Data Source=tslite+http://127.0.0.1:5080/metrics;Token=...`

详细说明见 [ADO.NET 参考]({{ site.docs_baseurl | default: '/help' }}/ado-net/)。
