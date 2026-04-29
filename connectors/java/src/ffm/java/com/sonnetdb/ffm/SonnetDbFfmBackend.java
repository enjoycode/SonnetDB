package com.sonnetdb.ffm;

import com.sonnetdb.SonnetDbException;
import com.sonnetdb.SonnetDbValueType;
import com.sonnetdb.internal.NativeBackend;

import java.lang.foreign.Arena;
import java.lang.foreign.FunctionDescriptor;
import java.lang.foreign.Linker;
import java.lang.foreign.MemorySegment;
import java.lang.foreign.SymbolLookup;
import java.lang.foreign.ValueLayout;
import java.lang.invoke.MethodHandle;
import java.nio.file.Path;

/**
 * JDK 21+ Foreign Function & Memory 后端。
 */
public final class SonnetDbFfmBackend implements NativeBackend {
    private static final Linker LINKER;

    private static final MethodHandle OPEN;
    private static final MethodHandle CLOSE;
    private static final MethodHandle EXECUTE;
    private static final MethodHandle RESULT_FREE;
    private static final MethodHandle RECORDS_AFFECTED;
    private static final MethodHandle COLUMN_COUNT;
    private static final MethodHandle COLUMN_NAME;
    private static final MethodHandle NEXT;
    private static final MethodHandle VALUE_TYPE;
    private static final MethodHandle VALUE_INT64;
    private static final MethodHandle VALUE_DOUBLE;
    private static final MethodHandle VALUE_BOOL;
    private static final MethodHandle VALUE_TEXT;
    private static final MethodHandle FLUSH;
    private static final MethodHandle VERSION;
    private static final MethodHandle LAST_ERROR;

    static {
        loadNativeLibrary();
        LINKER = Linker.nativeLinker();
        OPEN = downcall("sonnetdb_open", FunctionDescriptor.of(ValueLayout.ADDRESS, ValueLayout.ADDRESS));
        CLOSE = downcall("sonnetdb_close", FunctionDescriptor.ofVoid(ValueLayout.ADDRESS));
        EXECUTE = downcall("sonnetdb_execute", FunctionDescriptor.of(ValueLayout.ADDRESS, ValueLayout.ADDRESS, ValueLayout.ADDRESS));
        RESULT_FREE = downcall("sonnetdb_result_free", FunctionDescriptor.ofVoid(ValueLayout.ADDRESS));
        RECORDS_AFFECTED = downcall("sonnetdb_result_records_affected", FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.ADDRESS));
        COLUMN_COUNT = downcall("sonnetdb_result_column_count", FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.ADDRESS));
        COLUMN_NAME = downcall("sonnetdb_result_column_name", FunctionDescriptor.of(ValueLayout.ADDRESS, ValueLayout.ADDRESS, ValueLayout.JAVA_INT));
        NEXT = downcall("sonnetdb_result_next", FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.ADDRESS));
        VALUE_TYPE = downcall("sonnetdb_result_value_type", FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.ADDRESS, ValueLayout.JAVA_INT));
        VALUE_INT64 = downcall("sonnetdb_result_value_int64", FunctionDescriptor.of(ValueLayout.JAVA_LONG, ValueLayout.ADDRESS, ValueLayout.JAVA_INT));
        VALUE_DOUBLE = downcall("sonnetdb_result_value_double", FunctionDescriptor.of(ValueLayout.JAVA_DOUBLE, ValueLayout.ADDRESS, ValueLayout.JAVA_INT));
        VALUE_BOOL = downcall("sonnetdb_result_value_bool", FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.ADDRESS, ValueLayout.JAVA_INT));
        VALUE_TEXT = downcall("sonnetdb_result_value_text", FunctionDescriptor.of(ValueLayout.ADDRESS, ValueLayout.ADDRESS, ValueLayout.JAVA_INT));
        FLUSH = downcall("sonnetdb_flush", FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.ADDRESS));
        VERSION = downcall("sonnetdb_version", FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.ADDRESS, ValueLayout.JAVA_INT));
        LAST_ERROR = downcall("sonnetdb_last_error", FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.ADDRESS, ValueLayout.JAVA_INT));
    }

    /**
     * 构造 FFM 后端。
     */
    public SonnetDbFfmBackend() {
    }

    @Override
    public long open(String dataSource) {
        try (Arena arena = Arena.ofConfined()) {
            MemorySegment dataSourceAddress = arena.allocateUtf8String(dataSource);
            MemorySegment connection = (MemorySegment) OPEN.invoke(dataSourceAddress);
            if (isNull(connection)) {
                throw failure("sonnetdb_open");
            }
            return connection.address();
        } catch (SonnetDbException ex) {
            throw ex;
        } catch (Throwable ex) {
            throw new SonnetDbException("Failed to call sonnetdb_open.", ex);
        }
    }

    @Override
    public void close(long connection) {
        if (connection == 0L) {
            return;
        }
        try {
            CLOSE.invoke(MemorySegment.ofAddress(connection));
        } catch (Throwable ex) {
            throw new SonnetDbException("Failed to call sonnetdb_close.", ex);
        }
    }

    @Override
    public long execute(long connection, String sql) {
        try (Arena arena = Arena.ofConfined()) {
            MemorySegment sqlAddress = arena.allocateUtf8String(sql);
            MemorySegment result = (MemorySegment) EXECUTE.invoke(MemorySegment.ofAddress(connection), sqlAddress);
            if (isNull(result)) {
                throw failure("sonnetdb_execute");
            }
            return result.address();
        } catch (SonnetDbException ex) {
            throw ex;
        } catch (Throwable ex) {
            throw new SonnetDbException("Failed to call sonnetdb_execute.", ex);
        }
    }

    @Override
    public void resultFree(long result) {
        if (result == 0L) {
            return;
        }
        try {
            RESULT_FREE.invoke(MemorySegment.ofAddress(result));
        } catch (Throwable ex) {
            throw new SonnetDbException("Failed to call sonnetdb_result_free.", ex);
        }
    }

    @Override
    public int recordsAffected(long result) {
        return invokeInt(RECORDS_AFFECTED, result, "sonnetdb_result_records_affected");
    }

    @Override
    public int columnCount(long result) {
        return invokeInt(COLUMN_COUNT, result, "sonnetdb_result_column_count");
    }

    @Override
    public String columnName(long result, int ordinal) {
        try {
            MemorySegment address = (MemorySegment) COLUMN_NAME.invoke(MemorySegment.ofAddress(result), ordinal);
            if (isNull(address)) {
                throw failure("sonnetdb_result_column_name");
            }
            return readUtf8(address);
        } catch (SonnetDbException ex) {
            throw ex;
        } catch (Throwable ex) {
            throw new SonnetDbException("Failed to call sonnetdb_result_column_name.", ex);
        }
    }

    @Override
    public boolean next(long result) {
        int value = invokeInt(NEXT, result, "sonnetdb_result_next");
        if (value < 0) {
            throw failure("sonnetdb_result_next");
        }
        return value == 1;
    }

    @Override
    public SonnetDbValueType valueType(long result, int ordinal) {
        try {
            int code = (int) VALUE_TYPE.invoke(MemorySegment.ofAddress(result), ordinal);
            if (code < 0) {
                throw failure("sonnetdb_result_value_type");
            }
            return SonnetDbValueType.fromCode(code);
        } catch (SonnetDbException ex) {
            throw ex;
        } catch (Throwable ex) {
            throw new SonnetDbException("Failed to call sonnetdb_result_value_type.", ex);
        }
    }

    @Override
    public long valueInt64(long result, int ordinal) {
        try {
            return (long) VALUE_INT64.invoke(MemorySegment.ofAddress(result), ordinal);
        } catch (Throwable ex) {
            throw new SonnetDbException("Failed to call sonnetdb_result_value_int64: " + lastError(), ex);
        }
    }

    @Override
    public double valueDouble(long result, int ordinal) {
        try {
            return (double) VALUE_DOUBLE.invoke(MemorySegment.ofAddress(result), ordinal);
        } catch (Throwable ex) {
            throw new SonnetDbException("Failed to call sonnetdb_result_value_double: " + lastError(), ex);
        }
    }

    @Override
    public boolean valueBool(long result, int ordinal) {
        try {
            int value = (int) VALUE_BOOL.invoke(MemorySegment.ofAddress(result), ordinal);
            if (value < 0) {
                throw failure("sonnetdb_result_value_bool");
            }
            return value != 0;
        } catch (SonnetDbException ex) {
            throw ex;
        } catch (Throwable ex) {
            throw new SonnetDbException("Failed to call sonnetdb_result_value_bool.", ex);
        }
    }

    @Override
    public String valueText(long result, int ordinal) {
        try {
            MemorySegment address = (MemorySegment) VALUE_TEXT.invoke(MemorySegment.ofAddress(result), ordinal);
            return isNull(address) ? null : readUtf8(address);
        } catch (Throwable ex) {
            throw new SonnetDbException("Failed to call sonnetdb_result_value_text: " + lastError(), ex);
        }
    }

    @Override
    public void flush(long connection) {
        try {
            int code = (int) FLUSH.invoke(MemorySegment.ofAddress(connection));
            if (code != 0) {
                throw failure("sonnetdb_flush");
            }
        } catch (SonnetDbException ex) {
            throw ex;
        } catch (Throwable ex) {
            throw new SonnetDbException("Failed to call sonnetdb_flush.", ex);
        }
    }

    @Override
    public String version() {
        return copyString(VERSION, "sonnetdb_version");
    }

    @Override
    public String lastError() {
        return copyString(LAST_ERROR, "sonnetdb_last_error");
    }

    private static MethodHandle downcall(String symbol, FunctionDescriptor descriptor) {
        MemorySegment address = SymbolLookup.loaderLookup()
            .find(symbol)
            .orElseThrow(() -> new SonnetDbException("Native symbol not found: " + symbol));
        return LINKER.downcallHandle(address, descriptor);
    }

    private static int invokeInt(MethodHandle handle, long argument, String functionName) {
        try {
            int value = (int) handle.invoke(MemorySegment.ofAddress(argument));
            if (value < 0) {
                throw failure(functionName);
            }
            return value;
        } catch (SonnetDbException ex) {
            throw ex;
        } catch (Throwable ex) {
            throw new SonnetDbException("Failed to call " + functionName + ".", ex);
        }
    }

    private static String copyString(MethodHandle handle, String functionName) {
        try (Arena arena = Arena.ofConfined()) {
            MemorySegment first = arena.allocate(4096);
            int required = (int) handle.invoke(first, 4096);
            if (required < 0) {
                throw new SonnetDbException("Failed to call " + functionName + ".");
            }
            if (required < 4096) {
                return first.getUtf8String(0);
            }

            MemorySegment exact = arena.allocate((long) required + 1);
            int second = (int) handle.invoke(exact, required + 1);
            if (second < 0) {
                throw new SonnetDbException("Failed to call " + functionName + ".");
            }
            return exact.getUtf8String(0);
        } catch (SonnetDbException ex) {
            throw ex;
        } catch (Throwable ex) {
            throw new SonnetDbException("Failed to call " + functionName + ".", ex);
        }
    }

    private static boolean isNull(MemorySegment address) {
        return address == null || MemorySegment.NULL.equals(address);
    }

    private static String readUtf8(MemorySegment address) {
        return address.reinterpret(Long.MAX_VALUE).getUtf8String(0);
    }

    private static SonnetDbException failure(String functionName) {
        String message = copyString(LAST_ERROR, "sonnetdb_last_error");
        if (message == null || message.isBlank()) {
            message = functionName + " failed.";
        }
        return new SonnetDbException(message);
    }

    private static void loadNativeLibrary() {
        String path = System.getProperty("sonnetdb.native.path");
        if (path == null || path.isBlank()) {
            path = System.getenv("SONNETDB_NATIVE_LIBRARY");
        }

        if (path != null && !path.isBlank()) {
            System.load(Path.of(path).toAbsolutePath().toString());
            return;
        }

        try {
            System.loadLibrary("SonnetDB.Native");
        } catch (UnsatisfiedLinkError ex) {
            throw new SonnetDbException(
                "Cannot load SonnetDB native library. Set -Dsonnetdb.native.path or SONNETDB_NATIVE_LIBRARY.",
                ex);
        }
    }
}
