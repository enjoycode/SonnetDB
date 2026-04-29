package com.sonnetdb.jni;

/**
 * JNI native 方法声明。
 */
final class SonnetDbJni {
    private SonnetDbJni() {
    }

    static native void initialize(String nativeLibraryPath);

    static native long open(String dataSource);

    static native void close(long connection);

    static native long execute(long connection, String sql);

    static native void resultFree(long result);

    static native int recordsAffected(long result);

    static native int columnCount(long result);

    static native String columnName(long result, int ordinal);

    static native boolean next(long result);

    static native int valueType(long result, int ordinal);

    static native long valueInt64(long result, int ordinal);

    static native double valueDouble(long result, int ordinal);

    static native boolean valueBool(long result, int ordinal);

    static native String valueText(long result, int ordinal);

    static native void flush(long connection);

    static native String version();

    static native String lastError();
}
