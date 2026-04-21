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

## 当前命令

```text
tslite version
tslite sql  --connection "<connection-string>" (--command "<sql>" | --file ./query.sql)
tslite repl --connection "<connection-string>"
```

## `version`

```bash
tslite version
```

## `sql --command`

本地嵌入式：

```bash
tslite sql \
  --connection "Data Source=./demo-data" \
  --command "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)"
```

```bash
tslite sql \
  --connection "Data Source=./demo-data" \
  --command "SELECT count(*) FROM cpu"
```

远程服务端：

```bash
tslite sql \
  --connection "Data Source=tslite+http://127.0.0.1:5080/metrics;Token=your-token" \
  --command "SHOW DATABASES"
```

## `sql --file`

把 SQL 放进文件中执行：

```bash
tslite sql \
  --connection "Data Source=./demo-data" \
  --file ./query.sql
```

适合：

- 批量执行脚本
- 保存查询模板
- 回放建表和初始化脚本

## `repl`

```bash
tslite repl --connection "Data Source=./demo-data"
```

进入 REPL 后：

- 输入单行 SQL 并回车执行
- 输入 `exit` 或 `quit` 退出

## 输出形式

- 非查询语句：输出 `OK (n rows affected)`
- 查询语句：以文本表格形式输出列和结果行

## 连接字符串

CLI 与 ADO.NET 使用同一套连接字符串：

- 本地：`Data Source=./demo-data`
- 远程：`Data Source=tslite+http://127.0.0.1:5080/metrics;Token=...`

详细说明见 [ADO.NET 参考]({{ site.docs_baseurl | default: '/help' }}/ado-net/)。
