package com.sonnetdb.internal;

import com.sonnetdb.SonnetDbValueType;

/**
 * SonnetDB native 调用后端。
 */
public interface NativeBackend {
    /**
     * 打开数据库。
     *
     * @param dataSource 数据库根目录。
     * @return native connection 句柄。
     */
    long open(String dataSource);

    /**
     * 关闭数据库连接。
     *
     * @param connection native connection 句柄。
     */
    void close(long connection);

    /**
     * 执行 SQL。
     *
     * @param connection native connection 句柄。
     * @param sql SQL 文本。
     * @return native result 句柄。
     */
    long execute(long connection, String sql);

    /**
     * 释放结果。
     *
     * @param result native result 句柄。
     */
    void resultFree(long result);

    /**
     * 返回受影响行数。
     *
     * @param result native result 句柄。
     * @return 受影响行数。
     */
    int recordsAffected(long result);

    /**
     * 返回列数。
     *
     * @param result native result 句柄。
     * @return 列数。
     */
    int columnCount(long result);

    /**
     * 返回列名。
     *
     * @param result native result 句柄。
     * @param ordinal 列序号。
     * @return 列名。
     */
    String columnName(long result, int ordinal);

    /**
     * 移动到下一行。
     *
     * @param result native result 句柄。
     * @return 是否存在下一行。
     */
    boolean next(long result);

    /**
     * 返回值类型。
     *
     * @param result native result 句柄。
     * @param ordinal 列序号。
     * @return 值类型。
     */
    SonnetDbValueType valueType(long result, int ordinal);

    /**
     * 读取 int64 值。
     *
     * @param result native result 句柄。
     * @param ordinal 列序号。
     * @return int64 值。
     */
    long valueInt64(long result, int ordinal);

    /**
     * 读取 double 值。
     *
     * @param result native result 句柄。
     * @param ordinal 列序号。
     * @return double 值。
     */
    double valueDouble(long result, int ordinal);

    /**
     * 读取 bool 值。
     *
     * @param result native result 句柄。
     * @param ordinal 列序号。
     * @return bool 值。
     */
    boolean valueBool(long result, int ordinal);

    /**
     * 读取字符串值。
     *
     * @param result native result 句柄。
     * @param ordinal 列序号。
     * @return 字符串值。
     */
    String valueText(long result, int ordinal);

    /**
     * 主动 Flush。
     *
     * @param connection native connection 句柄。
     */
    void flush(long connection);

    /**
     * 返回 native library 版本。
     *
     * @return 版本。
     */
    String version();

    /**
     * 返回最近错误。
     *
     * @return 错误消息。
     */
    String lastError();
}
