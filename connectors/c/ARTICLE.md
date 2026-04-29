# SonnetDB C 连接器：嵌入式时序数据库的原生接入指南

SonnetDB 的 C 连接器提供了一套最小化、零依赖的 C ABI，让任何支持 C FFI 的语言都能直接操作 SonnetDB 嵌入式时序数据库。整套接口仅包含 **17 个函数**，一个头文件，学习成本极低。

## 架构概述

C 连接器的核心是一个由 .NET NativeAOT 编译而成的共享库（`SonnetDB.Native.dll` / `SonnetDB.Native.so`）。C 代码只需要包含 [`sonnetdb.h`](include/sonnetdb.h) 并链接这个库，即可在进程中嵌入一个完整的时序数据库引擎。

```
┌─────────────────────────┐
│  你的 C/C++ 应用程序      │
│  #include "sonnetdb.h"   │
└───────────┬─────────────┘
            │ C ABI (17 个导出函数)
┌───────────▼─────────────┐
│  SonnetDB.Native.dll/so  │
│  (.NET NativeAOT 编译)    │
│  内含完整数据库引擎        │
└──────────────────────────┘
```

## 快速开始

### 构建

确保安装了 .NET 10.0 SDK 和 CMake 3.20+。

**Windows (x64)：**
```powershell
cmake -S connectors/c --preset windows-x64
cmake --build artifacts/connectors/c/win-x64 --config Release
```

**Linux (x64)：**
```bash
cmake -S connectors/c -B artifacts/connectors/c/linux-x64 \
  -DSONNETDB_C_RID=linux-x64 -DCMAKE_BUILD_TYPE=Release
cmake --build artifacts/connectors/c/linux-x64
```

其他支持的平台预设：`windows-x86`、`windows-arm64`、`windows-xarm`。

构建产物：
- `SonnetDB.Native.dll`（Windows）或 `SonnetDB.Native.so`（Linux）—— 核心库
- `SonnetDB.Native.lib` —— Windows 导入库
- `sonnetdb_quickstart` / `sonnetdb_quickstart.exe` —— 示例程序

### 第一个程序

```c
#include <stdio.h>
#include "sonnetdb.h"

int main() {
    // 1. 打开数据库（目录路径或 sonnetdb:// URI）
    sonnetdb_connection* conn = sonnetdb_open("./mydb");
    if (!conn) {
        char err[1024];
        sonnetdb_last_error(err, sizeof(err));
        fprintf(stderr, "打开失败: %s\n", err);
        return 1;
    }

    // 2. 执行 DDL
    sonnetdb_result* r = sonnetdb_execute(conn,
        "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)");
    sonnetdb_result_free(r);

    // 3. 写入数据
    r = sonnetdb_execute(conn,
        "INSERT INTO cpu (time, host, usage) VALUES "
        "(1710000000000, 'edge-1', 0.42), "
        "(1710000001000, 'edge-1', 0.73)");
    printf("插入了 %d 行\n", sonnetdb_result_records_affected(r));
    sonnetdb_result_free(r);

    // 4. 查询数据
    r = sonnetdb_execute(conn,
        "SELECT time, host, usage FROM cpu WHERE host = 'edge-1' LIMIT 10");

    int cols = sonnetdb_result_column_count(r);
    for (int i = 0; i < cols; i++)
        printf("%s\t", sonnetdb_result_column_name(r, i));
    printf("\n");

    while (sonnetdb_result_next(r) == 1) {
        printf("%lld\t%s\t%.3f\n",
            (long long)sonnetdb_result_value_int64(r, 0),
            sonnetdb_result_value_text(r, 1),
            sonnetdb_result_value_double(r, 2));
    }
    sonnetdb_result_free(r);

    // 5. 关闭
    sonnetdb_flush(conn);
    sonnetdb_close(conn);
    return 0;
}
```

编译并链接（以 Linux 为例）：
```bash
gcc -I connectors/c/include -L artifacts/connectors/c/linux-x64 \
    -lSonnetDB.Native -o myapp myapp.c
```

## API 参考

### 数据类型

所有列值通过 `sonnetdb_value_type` 枚举描述：

| 枚举值 | 含义 | 读取方法 |
|--------|------|----------|
| `SONNETDB_TYPE_NULL` (0) | 空值 | 无需读取 |
| `SONNETDB_TYPE_INT64` (1) | 64 位有符号整数 | `sonnetdb_result_value_int64()` |
| `SONNETDB_TYPE_DOUBLE` (2) | 双精度浮点 | `sonnetdb_result_value_double()` |
| `SONNETDB_TYPE_BOOL` (3) | 布尔值 | `sonnetdb_result_value_bool()` |
| `SONNETDB_TYPE_TEXT` (4) | UTF-8 文本 | `sonnetdb_result_value_text()` |

> **隐式转换**：所有数值类型均可通过 `value_int64` 或 `value_double` 读取，引擎会自动转换。`GeoPoint` 格式化为 `POINT(lat,lon)`，`float[]` 向量格式化为 `[v0,v1,...]`。

### 连接管理

```c
sonnetdb_connection* sonnetdb_open(const char* data_source);
void                 sonnetdb_close(sonnetdb_connection* connection);
int32_t              sonnetdb_flush(sonnetdb_connection* connection);
```

- `data_source` 可以是普通目录路径（如 `"./data"`）或以 `sonnetdb://` 为前缀的 URI
- `sonnetdb_open()` 失败时返回 `NULL`，通过 `sonnetdb_last_error()` 获取错误信息
- `sonnetdb_flush()` 强制将内存中的 MemTable 持久化到磁盘，成功返回 0

### SQL 执行

```c
sonnetdb_result* sonnetdb_execute(sonnetdb_connection* connection, const char* sql);
void             sonnetdb_result_free(sonnetdb_result* result);
int32_t          sonnetdb_result_records_affected(sonnetdb_result* result);
```

- `execute()` 失败返回 `NULL`
- 每次 `execute()` 返回的 `result` 都必须调用 `result_free()` 释放，否则内存泄漏
- `records_affected()` 对 INSERT 返回插入行数，对 DELETE 返回 tombstone 数，对 SELECT 返回 -1

### 结果集遍历

```c
int32_t              sonnetdb_result_column_count(sonnetdb_result* result);
const char*          sonnetdb_result_column_name(sonnetdb_result* result, int32_t ordinal);
int32_t              sonnetdb_result_next(sonnetdb_result* result);
sonnetdb_value_type  sonnetdb_result_value_type(sonnetdb_result* result, int32_t ordinal);
```

遍历模式：

```c
// column_count 和 column_name 可以在 next() 之前调用
int cols = sonnetdb_result_column_count(result);
for (int i = 0; i < cols; i++)
    printf("%s\n", sonnetdb_result_column_name(result, i));

// 逐行遍历
while (sonnetdb_result_next(result) == 1) {
    // 读取当前行的各列值
}
// next() 返回 0 = 结束，-1 = 错误
```

**重要**：`value_text()` 返回的 `const char*` 指针仅在当前行有效。调用下一次 `next()` 或 `result_free()` 后指针即失效，如需保留请自行复制。

### 工具函数

```c
int32_t sonnetdb_version(char* buffer, int32_t buffer_length);
int32_t sonnetdb_last_error(char* buffer, int32_t buffer_length);
```

两者都采用「写入缓冲区」模式，返回所需字节数（不含空终止符）。典型用法：

```c
char buf[256];
int len = sonnetdb_version(buf, sizeof(buf));
if (len > 0) printf("SonnetDB 版本: %s\n", buf);

char err[1024];
if (sonnetdb_last_error(err, sizeof(err)) > 0)
    fprintf(stderr, "错误: %s\n", err);
```

错误信息是线程局部的，成功调用会清除上一个错误。

## 完整示例

见 [`examples/quickstart.c`](examples/quickstart.c)，涵盖：打开数据库 → 建表 → 插入 → 查询 → 遍历结果 → 关闭的完整生命周期，并包含错误处理辅助函数。

## 设计原则

- **最小化 ABI**：只有不透明句柄、基元类型和 UTF-8 字符串，不暴露内部结构
- **无外部依赖**：运行时仅依赖一个共享库文件
- **线程安全**：连接不可跨线程共享，但可以在不同线程各自打开连接；错误信息是线程局部的
- **跨语言基石**：Java、Rust、Go、Python 等语言的连接器都通过这个 C ABI 消费同一个共享库

## 支持的平台

| 平台 | 架构 |
|------|------|
| Windows | x64, x86, ARM64 |
| Linux | x64 |

需要 .NET 10.0 SDK 用于构建（运行时不需要）。
