package com.sonnetdb.jni;

import com.sonnetdb.SonnetDbException;
import com.sonnetdb.SonnetDbValueType;
import com.sonnetdb.internal.NativeBackend;

import java.io.File;

/**
 * Java 8 兼容的 JNI native 后端。
 */
public final class SonnetDbJniBackend implements NativeBackend {
    static {
        loadJniLibrary();
        SonnetDbJni.initialize(resolveNativeLibraryPath());
    }

    /**
     * 构造 JNI 后端。
     */
    public SonnetDbJniBackend() {
    }

    @Override
    public long open(String dataSource) {
        return SonnetDbJni.open(dataSource);
    }

    @Override
    public void close(long connection) {
        SonnetDbJni.close(connection);
    }

    @Override
    public long execute(long connection, String sql) {
        return SonnetDbJni.execute(connection, sql);
    }

    @Override
    public void resultFree(long result) {
        SonnetDbJni.resultFree(result);
    }

    @Override
    public int recordsAffected(long result) {
        return SonnetDbJni.recordsAffected(result);
    }

    @Override
    public int columnCount(long result) {
        return SonnetDbJni.columnCount(result);
    }

    @Override
    public String columnName(long result, int ordinal) {
        return SonnetDbJni.columnName(result, ordinal);
    }

    @Override
    public boolean next(long result) {
        return SonnetDbJni.next(result);
    }

    @Override
    public SonnetDbValueType valueType(long result, int ordinal) {
        return SonnetDbValueType.fromCode(SonnetDbJni.valueType(result, ordinal));
    }

    @Override
    public long valueInt64(long result, int ordinal) {
        return SonnetDbJni.valueInt64(result, ordinal);
    }

    @Override
    public double valueDouble(long result, int ordinal) {
        return SonnetDbJni.valueDouble(result, ordinal);
    }

    @Override
    public boolean valueBool(long result, int ordinal) {
        return SonnetDbJni.valueBool(result, ordinal);
    }

    @Override
    public String valueText(long result, int ordinal) {
        return SonnetDbJni.valueText(result, ordinal);
    }

    @Override
    public void flush(long connection) {
        SonnetDbJni.flush(connection);
    }

    @Override
    public String version() {
        return SonnetDbJni.version();
    }

    @Override
    public String lastError() {
        return SonnetDbJni.lastError();
    }

    private static String resolveNativeLibraryPath() {
        String path = System.getProperty("sonnetdb.native.path");
        if (path == null || path.trim().isEmpty()) {
            path = System.getenv("SONNETDB_NATIVE_LIBRARY");
        }
        return path == null || path.trim().isEmpty()
            ? null
            : new File(path).getAbsolutePath();
    }

    private static void loadJniLibrary() {
        String path = System.getProperty("sonnetdb.jni.path");
        if (path == null || path.trim().isEmpty()) {
            path = System.getenv("SONNETDB_JNI_LIBRARY");
        }

        try {
            if (path != null && !path.trim().isEmpty()) {
                System.load(new File(path).getAbsolutePath());
            } else {
                System.loadLibrary("SonnetDB.Java.Native");
            }
        } catch (UnsatisfiedLinkError ex) {
            throw new SonnetDbException(
                "Cannot load SonnetDB JNI bridge. Set -Dsonnetdb.jni.path or SONNETDB_JNI_LIBRARY.",
                ex);
        }
    }
}
