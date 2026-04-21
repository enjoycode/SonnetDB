---
layout: default
title: "SQL 参考"
description: "当前版本真实支持的数据面与控制面 SQL 语法、限制和示例。"
permalink: /sql-reference/
---

## 数据面 SQL

### `CREATE MEASUREMENT`

定义 measurement schema：

```sql
CREATE MEASUREMENT cpu (
    host TAG,
    region TAG STRING,
    usage FIELD FLOAT,
    count FIELD INT,
    ok FIELD BOOL,
    label FIELD STRING
)
```

规则：

- `TAG` 列默认为字符串，`TAG` 和 `TAG STRING` 等价。
- `FIELD` 列支持 `FLOAT`、`INT`、`BOOL`、`STRING`。
- schema 中至少要有一个 `FIELD` 列。
- `time` 不属于 schema 定义的一部分。

### `INSERT INTO ... VALUES`

```sql
INSERT INTO cpu (time, host, region, usage, count, ok, label)
VALUES
    (1713676800000, 'server-01', 'cn-hz', 0.71, 10, TRUE, 'ok'),
    (1713676860000, 'server-01', 'cn-hz', 0.73, 11, TRUE, 'ok')
```

规则：

- `time` 是保留伪列，表示 Unix 毫秒时间戳。
- `time` 省略时会使用当前 UTC 毫秒时间。
- 每一行至少需要提供一个 `FIELD` 列值。
- `TAG` 列必须是字符串字面量。
- `FIELD FLOAT` 可以接受整数或浮点字面量。
- `NULL` 不能用于当前 `INSERT`。

### 原始查询 `SELECT`

查询所有列：

```sql
SELECT * FROM cpu WHERE host = 'server-01'
```

显式投影：

```sql
SELECT time, host, usage
FROM cpu
WHERE host = 'server-01' AND time >= 1713676800000 AND time < 1713677400000
```

当前行为：

- `SELECT *` 会展开为 `time + 所有 tag 列 + 所有 field 列`。
- 当某个时间点缺少某个 field 时，结果列会返回 `NULL`。
- 结果按时间升序返回。

### 聚合查询

支持的聚合函数：

- `count`
- `sum`
- `min`
- `max`
- `avg`
- `first`
- `last`

示例：

```sql
SELECT sum(usage), avg(usage), min(usage), max(usage)
FROM cpu
WHERE host = 'server-01'
```

`count(*)` 也受支持：

```sql
SELECT count(*) FROM cpu WHERE host = 'server-01'
```

### `GROUP BY time(...)`

按时间桶聚合：

```sql
SELECT avg(usage) AS mean, count(usage)
FROM cpu
WHERE host = 'server-01'
GROUP BY time(1m)
```

当前限制和真实行为：

- 仅支持 `GROUP BY time(duration)`。
- 仅可用于聚合查询。
- 不支持 `GROUP BY host` 这类按列分组。
- 结果当前只返回聚合列，不会自动带出桶起始时间列。
- duration 例子：`1000ms`、`30s`、`1m`。

### `DELETE FROM ... WHERE ...`

```sql
DELETE FROM cpu
WHERE host = 'server-01' AND time >= 1713676800000 AND time <= 1713677400000
```

也可以只按 tag 或只按时间范围删除：

```sql
DELETE FROM cpu WHERE host = 'server-01'
DELETE FROM cpu WHERE time >= 1713676800000 AND time <= 1713677400000
```

当前删除语义：

- 删除底层通过 tombstone 实现，不会原地改写旧 segment。
- 后续查询会过滤 tombstone 覆盖的点。
- compaction 会逐步消化已删除数据。

## WHERE 子句的当前限制

虽然解析器支持更多表达式形态，但当前执行器的稳定支持范围是：

- tag 等值条件，例如 `host = 'server-01'`
- `time` 的范围比较，例如 `time >= ... AND time < ...`
- 多个条件使用 `AND` 连接

当前不建议在生产示例中使用：

- `OR`
- tag 不等式
- field 条件过滤，例如 `usage > 0`
- 混合聚合列与普通列，例如 `SELECT host, sum(usage) ...`

这些写法中的不少在当前版本会直接报错。

## 控制面 SQL

控制面 SQL 仅在服务端模式可用。

### 用户与密码

```sql
CREATE USER alice WITH PASSWORD 'pa$$'
CREATE USER admin2 WITH PASSWORD 'secret' SUPERUSER
ALTER USER alice WITH PASSWORD 'new-password'
DROP USER alice
```

### 数据库

```sql
CREATE DATABASE metrics
DROP DATABASE metrics
SHOW DATABASES
```

### 授权

```sql
GRANT READ ON DATABASE metrics TO alice
GRANT WRITE ON DATABASE metrics TO alice
GRANT ADMIN ON DATABASE * TO admin2
REVOKE ON DATABASE metrics FROM alice
```

### 查询用户、授权与 Token

```sql
SHOW USERS
SHOW GRANTS
SHOW GRANTS FOR alice
SHOW TOKENS
SHOW TOKENS FOR alice
ISSUE TOKEN FOR alice
REVOKE TOKEN 'tok_abcdef'
```

说明：

- `SHOW TOKENS` 只返回 Token 元数据，不返回明文。
- `ISSUE TOKEN FOR ...` 会在结果里一次性返回明文 Token。
- `REVOKE TOKEN 'tok_xxx'` 按 token id 吊销。

## HTTP 端点

| 端点 | 用途 |
| --- | --- |
| `POST /v1/db/{db}/sql` | 单条 SQL，主要用于数据面；admin 也可通过它执行部分控制面语句 |
| `POST /v1/db/{db}/sql/batch` | 批量 SQL 脚本 |
| `POST /v1/sql` | 专用控制面 SQL 端点，仅 admin |

## 角色与权限

- `readonly`：仅查询
- `readwrite`：可写入和查询
- `admin`：可管理数据库、执行控制面 SQL、进入完整管理能力

## 相关页面

- [批量写入]({{ site.docs_baseurl | default: '/help' }}/bulk-ingest/)
- [ADO.NET 参考]({{ site.docs_baseurl | default: '/help' }}/ado-net/)
- [CLI 参考]({{ site.docs_baseurl | default: '/help' }}/cli-reference/)
