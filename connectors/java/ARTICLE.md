# SonnetDB Java 连接器：双后端架构深度解析

SonnetDB Java 连接器为 Java 生态提供了嵌入式的时序数据库访问能力。其最大的亮点是**双后端架构**——同一套公共 API，底层自动适配 Java 8 的 JNI 和 Java 21 的 Foreign Function & Memory API（FFM），让应用在不同 JDK 版本上都能获得最佳体验。

## 架构总览

```
┌──────────────────────────────────────────────────┐
│              你的 Java 应用程序                    │
│  SonnetDbConnection / SonnetDbResult (公共 API)   │
└─────────────────────┬────────────────────────────┘
                      │
              NativeBackend (SPI)
                      │
        ┌─────────────┴─────────────┐
        │                           │
  ┌─────▼──────┐           ┌───────▼────────┐
  │ JNI Backend│           │  FFM Backend    │
  │ (Java 8+)  │           │  (Java 21+)     │
  └─────┬──────┘           └───────┬────────┘
        │                          │
  ┌─────▼──────┐           ┌───────▼────────┐
  │JNI Bridge  │           │  直接调用        │
  │.dll/.so    │           │  MethodHandle   │
  └─────┬──────┘           └───────┬────────┘
        │                          │
        └──────────┬───────────────┘
                   │
        ┌──────────▼──────────┐
        │ SonnetDB.Native     │
        │ (核心数据库引擎)      │
        └─────────────────────┘
```

## 快速开始

### 环境准备

- **Java 8+**：JNI 后端（默认）
- **JDK 21+**：可使用 FFM 后端（需开启预览特性）
- 先构建 C 连接器，产出 `SonnetDB.Native.dll` / `SonnetDB.Native.so`

### 构建

**双后端构建（JDK 21+）：**
```powershell
# 先构建 C 连接器
cmake -S connectors/c --preset windows-x64
cmake --build artifacts/connectors/c/win-x64 --config Release

# 再构建 Java 连接器（含 JNI + FFM）
cmake -S connectors/java --preset windows-x64
cmake --build artifacts/connectors/java/windows-x64 --config Release
```

**仅 JNI 后端（Java 8 兼容）：**
```powershell
cmake -S connectors/java --preset windows-x64-java8
cmake --build artifacts/connectors/java/windows-x64-java8 --config Release
```

构建产物：
- `sonnetdb-java.jar` —— 多版本 JAR（`Multi-Release: true`），在 Java 8 和 Java 21 上均可使用
- `SonnetDB.Java.Native.dll` / `.so` —— JNI 桥接库

### 第一个程序

```java
import com.sonnetdb.*;

public class Quickstart {
    public static void main(String[] args) {
        // 查看版本
        System.out.println("SonnetDB 版本: " + SonnetDbConnection.version());

        // 打开数据库（嵌入式，try-with-resources 自动关闭）
        try (SonnetDbConnection conn = SonnetDbConnection.open("./mydb")) {

            // DDL
            conn.executeNonQuery(
                "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)");

            // 写入数据
            int inserted = conn.executeNonQuery(
                "INSERT INTO cpu (time, host, usage) VALUES " +
                "(1710000000000, 'edge-1', 0.42), " +
                "(1710000001000, 'edge-1', 0.73)");
            System.out.println("插入了 " + inserted + " 行");

            // 查询
            try (SonnetDbResult result = conn.execute(
                    "SELECT time, host, usage FROM cpu " +
                    "WHERE host = 'edge-1' LIMIT 10")) {

                // 打印列头
                for (int i = 0; i < result.columnCount(); i++)
                    System.out.print(result.columnName(i) + "\t");
                System.out.println();

                // 遍历行
                while (result.next()) {
                    long time = result.getLong(0);
                    String host = result.getString(1);
                    double usage = result.getDouble(2);
                    System.out.printf("%d\t%s\t%.3f%n", time, host, usage);
                }
            }
        }
    }
}
```

### 运行

**JNI 后端（默认）：**
```powershell
java `
  -Dsonnetdb.native.path=artifacts/connectors/c/win-x64/Release/SonnetDB.Native.dll `
  -Dsonnetdb.jni.path=artifacts/connectors/java/windows-x64/Release/SonnetDB.Java.Native.dll `
  -cp "artifacts/connectors/java/windows-x64/sonnetdb-java.jar;myapp.jar" `
  com.example.Quickstart
```

**FFM 后端（JDK 21+）：**
```powershell
java --enable-preview --enable-native-access=ALL-UNNAMED `
  -Dsonnetdb.java.backend=ffm `
  -Dsonnetdb.native.path=artifacts/connectors/c/win-x64/Release/SonnetDB.Native.dll `
  -cp "artifacts/connectors/java/windows-x64/sonnetdb-java.jar;myapp.jar" `
  com.example.Quickstart
```

## API 参考

### SonnetDbConnection —— 数据库连接

| 方法 | 说明 |
|------|------|
| `static SonnetDbConnection open(String dataSource)` | 打开嵌入式数据库，参数为目录路径 |
| `static String version()` | 返回核心库版本字符串 |
| `SonnetDbResult execute(String sql)` | 执行查询，返回可遍历的结果集 |
| `int executeNonQuery(String sql)` | 执行非查询语句，返回受影响行数 |
| `void flush()` | 强制刷盘 |
| `void close()` | 关闭连接 |

### SonnetDbResult —— 结果集

| 方法 | 说明 |
|------|------|
| `int recordsAffected()` | INSERT 行数 / DELETE tombstone 数 / SELECT 返回 -1 |
| `int columnCount()` | 列数 |
| `String columnName(int ordinal)` | 指定序号（从 0 开始）的列名 |
| `boolean next()` | 推进游标到下一行，无更多行时返回 `false` |
| `SonnetDbValueType valueType(int ordinal)` | 当前行指定列的值类型 |
| `long getLong(int ordinal)` | 以 int64 读取 |
| `double getDouble(int ordinal)` | 以 double 读取 |
| `boolean getBoolean(int ordinal)` | 以 boolean 读取 |
| `String getString(int ordinal)` | 以 UTF-8 文本读取，NULL 返回 null |
| `Object getObject(int ordinal)` | 自动装箱为 Long/Double/Boolean/String/null |
| `void close()` | 释放资源 |

### SonnetDbValueType —— 值类型枚举

| 枚举常量 | 说明 |
|---------|------|
| `NULL` | 空值 |
| `INT64` | 64 位有符号整数 |
| `DOUBLE` | 双精度浮点数 |
| `BOOL` | 布尔值 |
| `TEXT` | UTF-8 字符串（含 GeoPoint 的 WKT 表示和 float[] 的格式化表示） |

> 所有数值类型均可通过 `getLong()` 或 `getDouble()` 读取，引擎会自动进行类型转换。

### 使用模式

```java
// 始终使用 try-with-resources
try (SonnetDbConnection conn = SonnetDbConnection.open("./data")) {

    // 建表：executeNonQuery 自动释放资源
    conn.executeNonQuery("CREATE MEASUREMENT ...");

    // 写入：executeNonQuery 返回影响行数
    int n = conn.executeNonQuery("INSERT INTO ...");

    // 查询：execute 返回 SonnetDbResult，必须关闭
    try (SonnetDbResult rs = conn.execute("SELECT ...")) {
        while (rs.next()) {
            long v1 = rs.getLong(0);
            String v2 = rs.getString(1);
        }
    }
}
```

## Java 8 (JNI) 与 Java 21 (FFM) 深度对比

这是 SonnetDB Java 连接器最有特色的设计。下面从多个维度对比两种后端。

### 一、加载机制

| 维度 | JNI 后端 | FFM 后端 |
|------|---------|----------|
| 后端选择 | 默认，`sonnetdb.java.backend=jni` | 需指定 `sonnetdb.java.backend=ffm` |
| 核心库加载 | 由 JNI 桥接库内部 `LoadLibrary` | Java 侧直接 `System.load()` |
| 符号绑定 | 编译时 JNI 名称修饰 + `GetProcAddress`/`dlsym` 动态解析 | `SymbolLookup.loaderLookup()` + `MethodHandle` 下行调用 |
| 额外库 | 需要 `SonnetDB.Java.Native.dll` | 不需要，纯 Java 实现 |

**JNI 路径**需要两个原生库：

```
SonnetDB.Java.Native.dll  ←  JNI 桥接（C 代码编译）
        │
        └── LoadLibrary ── SonnetDB.Native.dll  ← 核心引擎
```

**FFM 路径**只需要一个原生库：

```
Java (MethodHandle) ── 直接调用 ── SonnetDB.Native.dll  ← 核心引擎
```

### 二、内存管理

**JNI 后端：**
- 字符串通过 `GetStringUTFChars` / `ReleaseStringUTFChars` 传递
- 结果集中的 C 字符串在 `next()` 或 `resultFree()` 时由原生层释放
- Java 侧只需关注 `SonnetDbResult.close()`

**FFM 后端：**
- 使用 `Arena.ofConfined()` 创建每次调用的临时内存作用域
- 字符串通过 `MemorySegment.getUtf8String()` 读取
- 调用结束后自动释放该作用域内的所有内存
- 零拷贝读取 C 字符串（`reinterpret(Long.MAX_VALUE)` 将整个内存视为以 `\0` 结尾的 C 字符串）

### 三、JAR 结构

SonnetDB Java 连接器使用 **多版本 JAR（Multi-Release JAR）** 机制：

```
sonnetdb-java.jar
├── com/sonnetdb/
│   ├── SonnetDbConnection.class          ← Java 8 编译
│   ├── SonnetDbResult.class
│   ├── SonnetDbException.class
│   ├── SonnetDbValueType.class
│   ├── internal/
│   │   ├── NativeBackend.class
│   │   └── NativeBackendLoader.class
│   ├── jni/
│   │   ├── SonnetDbJni.class
│   │   └── SonnetDbJniBackend.class
│   └── ffm/
│       └── SonnetDbFfmBackend.class      ← 占位实现，直接抛异常
└── META-INF/
    ├── MANIFEST.MF  (Multi-Release: true)
    └── versions/21/
        └── com/sonnetdb/ffm/
            └── SonnetDbFfmBackend.class  ← 真正的 FFM 实现
```

- 在 **JDK < 21** 上，`META-INF/versions/21/` 被忽略，FFM 类为占位实现
- 在 **JDK 21+** 上，版本化类自动覆盖基类，获得真正的 FFM 后端
- 同一个 JAR 无需任何修改即可在两个 JDK 版本上运行

### 四、后端自动选择

除了手动指定 `jni` 或 `ffm`，还支持 `auto` 模式：

```java
// 系统属性
-Dsonnetdb.java.backend=auto

// 或环境变量
export SONNETDB_JAVA_BACKEND=auto
```

`auto` 模式的决策逻辑：

```
JDK 版本 >= 21？
  ├── 是 → 尝试加载 FFM 后端
  │         ├── 成功 → 使用 FFM
  │         └── 异常 → 回退到 JNI
  └── 否 → 使用 JNI
```

### 五、运行时要求

| 要求 | JNI 后端 | FFM 后端 |
|------|---------|----------|
| 最低 JDK | 8 | 21 |
| JVM 参数 | 无 | `--enable-preview --enable-native-access=ALL-UNNAMED` |
| 原生编译 | 需要为每个平台编译 `sonnetdb_jni.c` | 不需要额外原生编译 |
| 系统属性 | `sonnetdb.jni.path` / `sonnetdb.native.path` | 仅 `sonnetdb.native.path` |

### 六、何时选择哪个后端？

**选择 JNI 后端的情况：**
- 需要在 Java 8 / 11 / 17 等旧版本 JDK 上运行
- 已经有一个成熟的 JNI 原生库分发流程
- 不想添加 `--enable-preview` 等 JVM 启动参数

**选择 FFM 后端的情况：**
- 目标运行环境是 JDK 21+
- 希望减少原生库的编译和分发（不需要 JNI 桥接层）
- 追求更简洁的部署：少一个 `.dll`/`.so` 文件
- 希望利用 Panama 项目的高性能内存模型

## 配置参考

| 用途 | 系统属性 | 环境变量 |
|------|---------|----------|
| 后端选择 | `sonnetdb.java.backend` | `SONNETDB_JAVA_BACKEND` |
| JNI 桥接库路径 | `sonnetdb.jni.path` | `SONNETDB_JNI_LIBRARY` |
| 核心库路径 | `sonnetdb.native.path` | `SONNETDB_NATIVE_LIBRARY` |

> 优先级：系统属性 > 环境变量 > 内置默认值

## 最佳实践

1. **始终使用 try-with-resources**：`SonnetDbConnection` 和 `SonnetDbResult` 都实现了 `AutoCloseable`
2. **优先用 `executeNonQuery()`**：对于 INSERT、DELETE、DDL 等不需要遍历结果的语句，它会自动释放资源
3. **生产环境指定后端**：不要依赖 `auto`，通过系统属性显式设置 `sonnetdb.java.backend`
4. **验证版本兼容性**：启动时调用 `SonnetDbConnection.version()` 确认原生库版本匹配
5. **异常处理**：所有异常均为 `SonnetDbException`（继承自 `RuntimeException`），包含来自原生库的详细错误信息

## 完整示例

见 [`examples/Quickstart.java`](examples/Quickstart.java)，演示了打开数据库、DDL 建表、批量插入、条件查询、结果遍历和自动资源管理的完整流程。

## 设计理念

Java 连接器的设计遵循三个核心原则：

1. **API 与实现分离**：公共 API（`SonnetDbConnection`、`SonnetDbResult`）完全不依赖任何原生调用细节，通过 `NativeBackend` 接口解耦
2. **一个 JAR，随处运行**：多版本 JAR 使得同一个构建产物在 Java 8 和 Java 21 上都能正常运行，自动选择最优后端
3. **渐进式增强**：Java 8 用户获得完整的 JNI 体验，升级到 Java 21 后无需更改代码即可获得 FFM 的性能优势
