package com.sonnetdb.ffm;

import com.sonnetdb.SonnetDbException;
import com.sonnetdb.SonnetDbValueType;
import com.sonnetdb.internal.NativeBackend;

/**
 * Java 8 基础 jar 中的 FFM 后端占位实现。
 *
 * <p>真正的 FFM 实现在 multi-release jar 的 Java 21 版本目录中。</p>
 */
public final class SonnetDbFfmBackend implements NativeBackend {
    /**
     * 构造 FFM 占位后端。
     */
    public SonnetDbFfmBackend() {
        throw unsupported();
    }

    @Override
    public long open(String dataSource) {
        throw unsupported();
    }

    @Override
    public void close(long connection) {
        throw unsupported();
    }

    @Override
    public long execute(long connection, String sql) {
        throw unsupported();
    }

    @Override
    public void resultFree(long result) {
        throw unsupported();
    }

    @Override
    public int recordsAffected(long result) {
        throw unsupported();
    }

    @Override
    public int columnCount(long result) {
        throw unsupported();
    }

    @Override
    public String columnName(long result, int ordinal) {
        throw unsupported();
    }

    @Override
    public boolean next(long result) {
        throw unsupported();
    }

    @Override
    public SonnetDbValueType valueType(long result, int ordinal) {
        throw unsupported();
    }

    @Override
    public long valueInt64(long result, int ordinal) {
        throw unsupported();
    }

    @Override
    public double valueDouble(long result, int ordinal) {
        throw unsupported();
    }

    @Override
    public boolean valueBool(long result, int ordinal) {
        throw unsupported();
    }

    @Override
    public String valueText(long result, int ordinal) {
        throw unsupported();
    }

    @Override
    public void flush(long connection) {
        throw unsupported();
    }

    @Override
    public String version() {
        throw unsupported();
    }

    @Override
    public String lastError() {
        throw unsupported();
    }

    private static SonnetDbException unsupported() {
        return new SonnetDbException("SonnetDB FFM backend requires JDK 21+ and a multi-release jar build.");
    }
}
