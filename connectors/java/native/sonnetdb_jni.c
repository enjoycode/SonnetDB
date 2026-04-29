#include <jni.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#include <windows.h>
#else
#include <dlfcn.h>
#endif

typedef void* (*sonnetdb_open_fn)(const char*);
typedef void (*sonnetdb_close_fn)(void*);
typedef void* (*sonnetdb_execute_fn)(void*, const char*);
typedef void (*sonnetdb_result_free_fn)(void*);
typedef int32_t (*sonnetdb_result_records_affected_fn)(void*);
typedef int32_t (*sonnetdb_result_column_count_fn)(void*);
typedef const char* (*sonnetdb_result_column_name_fn)(void*, int32_t);
typedef int32_t (*sonnetdb_result_next_fn)(void*);
typedef int32_t (*sonnetdb_result_value_type_fn)(void*, int32_t);
typedef int64_t (*sonnetdb_result_value_int64_fn)(void*, int32_t);
typedef double (*sonnetdb_result_value_double_fn)(void*, int32_t);
typedef int32_t (*sonnetdb_result_value_bool_fn)(void*, int32_t);
typedef const char* (*sonnetdb_result_value_text_fn)(void*, int32_t);
typedef int32_t (*sonnetdb_flush_fn)(void*);
typedef int32_t (*sonnetdb_version_fn)(char*, int32_t);
typedef int32_t (*sonnetdb_last_error_fn)(char*, int32_t);

static sonnetdb_open_fn p_sonnetdb_open;
static sonnetdb_close_fn p_sonnetdb_close;
static sonnetdb_execute_fn p_sonnetdb_execute;
static sonnetdb_result_free_fn p_sonnetdb_result_free;
static sonnetdb_result_records_affected_fn p_sonnetdb_result_records_affected;
static sonnetdb_result_column_count_fn p_sonnetdb_result_column_count;
static sonnetdb_result_column_name_fn p_sonnetdb_result_column_name;
static sonnetdb_result_next_fn p_sonnetdb_result_next;
static sonnetdb_result_value_type_fn p_sonnetdb_result_value_type;
static sonnetdb_result_value_int64_fn p_sonnetdb_result_value_int64;
static sonnetdb_result_value_double_fn p_sonnetdb_result_value_double;
static sonnetdb_result_value_bool_fn p_sonnetdb_result_value_bool;
static sonnetdb_result_value_text_fn p_sonnetdb_result_value_text;
static sonnetdb_flush_fn p_sonnetdb_flush;
static sonnetdb_version_fn p_sonnetdb_version;
static sonnetdb_last_error_fn p_sonnetdb_last_error;

#ifdef _WIN32
static HMODULE native_library;
#else
static void* native_library;
#endif

static void throw_sonnet(JNIEnv* env, const char* message)
{
    jclass ex = (*env)->FindClass(env, "com/sonnetdb/SonnetDbException");
    if (ex == NULL)
    {
        ex = (*env)->FindClass(env, "java/lang/RuntimeException");
    }
    if (ex != NULL)
    {
        (*env)->ThrowNew(env, ex, message == NULL ? "SonnetDB JNI error." : message);
    }
}

static void throw_last_error(JNIEnv* env, const char* fallback)
{
    char buffer[4096];
    buffer[0] = '\0';
    if (p_sonnetdb_last_error != NULL)
    {
        p_sonnetdb_last_error(buffer, (int32_t)sizeof(buffer));
    }
    if (buffer[0] == '\0')
    {
        throw_sonnet(env, fallback);
    }
    else
    {
        throw_sonnet(env, buffer);
    }
}

#ifdef _WIN32
static void* resolve_symbol(const char* name)
{
    return (void*)GetProcAddress(native_library, name);
}

static int load_library(JNIEnv* env, jstring path)
{
    if (path != NULL)
    {
        const jchar* chars = (*env)->GetStringChars(env, path, NULL);
        if (chars == NULL)
        {
            return 0;
        }
        native_library = LoadLibraryW((LPCWSTR)chars);
        (*env)->ReleaseStringChars(env, path, chars);
    }
    else
    {
        native_library = LoadLibraryW(L"SonnetDB.Native.dll");
    }

    if (native_library == NULL)
    {
        throw_sonnet(env, "Cannot load SonnetDB.Native.dll. Set sonnetdb.native.path.");
        return 0;
    }
    return 1;
}
#else
static void* resolve_symbol(const char* name)
{
    return dlsym(native_library, name);
}

static int load_library(JNIEnv* env, jstring path)
{
    if (path != NULL)
    {
        const char* chars = (*env)->GetStringUTFChars(env, path, NULL);
        if (chars == NULL)
        {
            return 0;
        }
        native_library = dlopen(chars, RTLD_NOW | RTLD_LOCAL);
        (*env)->ReleaseStringUTFChars(env, path, chars);
    }
    else
    {
        native_library = dlopen("SonnetDB.Native.so", RTLD_NOW | RTLD_LOCAL);
    }

    if (native_library == NULL)
    {
        const char* error = dlerror();
        throw_sonnet(env, error == NULL ? "Cannot load SonnetDB.Native.so. Set sonnetdb.native.path." : error);
        return 0;
    }
    return 1;
}
#endif

#define RESOLVE_SYMBOL(field, type, name) \
    do { \
        field = (type)resolve_symbol(name); \
        if (field == NULL) { \
            throw_sonnet(env, "Cannot resolve SonnetDB native symbol: " name); \
            return; \
        } \
    } while (0)

JNIEXPORT void JNICALL Java_com_sonnetdb_jni_SonnetDbJni_initialize(
    JNIEnv* env,
    jclass cls,
    jstring nativeLibraryPath)
{
    (void)cls;

    if (native_library != NULL)
    {
        return;
    }

    if (!load_library(env, nativeLibraryPath))
    {
        return;
    }

    RESOLVE_SYMBOL(p_sonnetdb_open, sonnetdb_open_fn, "sonnetdb_open");
    RESOLVE_SYMBOL(p_sonnetdb_close, sonnetdb_close_fn, "sonnetdb_close");
    RESOLVE_SYMBOL(p_sonnetdb_execute, sonnetdb_execute_fn, "sonnetdb_execute");
    RESOLVE_SYMBOL(p_sonnetdb_result_free, sonnetdb_result_free_fn, "sonnetdb_result_free");
    RESOLVE_SYMBOL(p_sonnetdb_result_records_affected, sonnetdb_result_records_affected_fn, "sonnetdb_result_records_affected");
    RESOLVE_SYMBOL(p_sonnetdb_result_column_count, sonnetdb_result_column_count_fn, "sonnetdb_result_column_count");
    RESOLVE_SYMBOL(p_sonnetdb_result_column_name, sonnetdb_result_column_name_fn, "sonnetdb_result_column_name");
    RESOLVE_SYMBOL(p_sonnetdb_result_next, sonnetdb_result_next_fn, "sonnetdb_result_next");
    RESOLVE_SYMBOL(p_sonnetdb_result_value_type, sonnetdb_result_value_type_fn, "sonnetdb_result_value_type");
    RESOLVE_SYMBOL(p_sonnetdb_result_value_int64, sonnetdb_result_value_int64_fn, "sonnetdb_result_value_int64");
    RESOLVE_SYMBOL(p_sonnetdb_result_value_double, sonnetdb_result_value_double_fn, "sonnetdb_result_value_double");
    RESOLVE_SYMBOL(p_sonnetdb_result_value_bool, sonnetdb_result_value_bool_fn, "sonnetdb_result_value_bool");
    RESOLVE_SYMBOL(p_sonnetdb_result_value_text, sonnetdb_result_value_text_fn, "sonnetdb_result_value_text");
    RESOLVE_SYMBOL(p_sonnetdb_flush, sonnetdb_flush_fn, "sonnetdb_flush");
    RESOLVE_SYMBOL(p_sonnetdb_version, sonnetdb_version_fn, "sonnetdb_version");
    RESOLVE_SYMBOL(p_sonnetdb_last_error, sonnetdb_last_error_fn, "sonnetdb_last_error");
}

JNIEXPORT jlong JNICALL Java_com_sonnetdb_jni_SonnetDbJni_open(
    JNIEnv* env,
    jclass cls,
    jstring dataSource)
{
    (void)cls;
    const char* chars = (*env)->GetStringUTFChars(env, dataSource, NULL);
    if (chars == NULL)
    {
        return 0;
    }

    void* connection = p_sonnetdb_open(chars);
    (*env)->ReleaseStringUTFChars(env, dataSource, chars);
    if (connection == NULL)
    {
        throw_last_error(env, "sonnetdb_open failed.");
        return 0;
    }
    return (jlong)(intptr_t)connection;
}

JNIEXPORT void JNICALL Java_com_sonnetdb_jni_SonnetDbJni_close(
    JNIEnv* env,
    jclass cls,
    jlong connection)
{
    (void)env;
    (void)cls;
    if (connection != 0)
    {
        p_sonnetdb_close((void*)(intptr_t)connection);
    }
}

JNIEXPORT jlong JNICALL Java_com_sonnetdb_jni_SonnetDbJni_execute(
    JNIEnv* env,
    jclass cls,
    jlong connection,
    jstring sql)
{
    (void)cls;
    const char* chars = (*env)->GetStringUTFChars(env, sql, NULL);
    if (chars == NULL)
    {
        return 0;
    }

    void* result = p_sonnetdb_execute((void*)(intptr_t)connection, chars);
    (*env)->ReleaseStringUTFChars(env, sql, chars);
    if (result == NULL)
    {
        throw_last_error(env, "sonnetdb_execute failed.");
        return 0;
    }
    return (jlong)(intptr_t)result;
}

JNIEXPORT void JNICALL Java_com_sonnetdb_jni_SonnetDbJni_resultFree(
    JNIEnv* env,
    jclass cls,
    jlong result)
{
    (void)env;
    (void)cls;
    if (result != 0)
    {
        p_sonnetdb_result_free((void*)(intptr_t)result);
    }
}

JNIEXPORT jint JNICALL Java_com_sonnetdb_jni_SonnetDbJni_recordsAffected(JNIEnv* env, jclass cls, jlong result)
{
    (void)env;
    (void)cls;
    return p_sonnetdb_result_records_affected((void*)(intptr_t)result);
}

JNIEXPORT jint JNICALL Java_com_sonnetdb_jni_SonnetDbJni_columnCount(JNIEnv* env, jclass cls, jlong result)
{
    (void)cls;
    int32_t value = p_sonnetdb_result_column_count((void*)(intptr_t)result);
    if (value < 0)
    {
        throw_last_error(env, "sonnetdb_result_column_count failed.");
    }
    return value;
}

JNIEXPORT jstring JNICALL Java_com_sonnetdb_jni_SonnetDbJni_columnName(JNIEnv* env, jclass cls, jlong result, jint ordinal)
{
    (void)cls;
    const char* value = p_sonnetdb_result_column_name((void*)(intptr_t)result, ordinal);
    if (value == NULL)
    {
        throw_last_error(env, "sonnetdb_result_column_name failed.");
        return NULL;
    }
    return (*env)->NewStringUTF(env, value);
}

JNIEXPORT jboolean JNICALL Java_com_sonnetdb_jni_SonnetDbJni_next(JNIEnv* env, jclass cls, jlong result)
{
    (void)cls;
    int32_t value = p_sonnetdb_result_next((void*)(intptr_t)result);
    if (value < 0)
    {
        throw_last_error(env, "sonnetdb_result_next failed.");
        return JNI_FALSE;
    }
    return value == 1 ? JNI_TRUE : JNI_FALSE;
}

JNIEXPORT jint JNICALL Java_com_sonnetdb_jni_SonnetDbJni_valueType(JNIEnv* env, jclass cls, jlong result, jint ordinal)
{
    (void)cls;
    int32_t value = p_sonnetdb_result_value_type((void*)(intptr_t)result, ordinal);
    if (value < 0)
    {
        throw_last_error(env, "sonnetdb_result_value_type failed.");
    }
    return value;
}

JNIEXPORT jlong JNICALL Java_com_sonnetdb_jni_SonnetDbJni_valueInt64(JNIEnv* env, jclass cls, jlong result, jint ordinal)
{
    (void)env;
    (void)cls;
    return (jlong)p_sonnetdb_result_value_int64((void*)(intptr_t)result, ordinal);
}

JNIEXPORT jdouble JNICALL Java_com_sonnetdb_jni_SonnetDbJni_valueDouble(JNIEnv* env, jclass cls, jlong result, jint ordinal)
{
    (void)env;
    (void)cls;
    return (jdouble)p_sonnetdb_result_value_double((void*)(intptr_t)result, ordinal);
}

JNIEXPORT jboolean JNICALL Java_com_sonnetdb_jni_SonnetDbJni_valueBool(JNIEnv* env, jclass cls, jlong result, jint ordinal)
{
    (void)cls;
    int32_t value = p_sonnetdb_result_value_bool((void*)(intptr_t)result, ordinal);
    if (value < 0)
    {
        throw_last_error(env, "sonnetdb_result_value_bool failed.");
        return JNI_FALSE;
    }
    return value == 0 ? JNI_FALSE : JNI_TRUE;
}

JNIEXPORT jstring JNICALL Java_com_sonnetdb_jni_SonnetDbJni_valueText(JNIEnv* env, jclass cls, jlong result, jint ordinal)
{
    (void)cls;
    const char* value = p_sonnetdb_result_value_text((void*)(intptr_t)result, ordinal);
    if (value == NULL)
    {
        return NULL;
    }
    return (*env)->NewStringUTF(env, value);
}

JNIEXPORT void JNICALL Java_com_sonnetdb_jni_SonnetDbJni_flush(JNIEnv* env, jclass cls, jlong connection)
{
    (void)cls;
    int32_t value = p_sonnetdb_flush((void*)(intptr_t)connection);
    if (value != 0)
    {
        throw_last_error(env, "sonnetdb_flush failed.");
    }
}

static jstring copy_string(JNIEnv* env, int32_t (*fn)(char*, int32_t))
{
    char stack_buffer[4096];
    int32_t required = fn(stack_buffer, (int32_t)sizeof(stack_buffer));
    if (required < 0)
    {
        return NULL;
    }
    if (required < (int32_t)sizeof(stack_buffer))
    {
        return (*env)->NewStringUTF(env, stack_buffer);
    }

    char* heap_buffer = (char*)malloc((size_t)required + 1);
    if (heap_buffer == NULL)
    {
        throw_sonnet(env, "Out of memory.");
        return NULL;
    }
    required = fn(heap_buffer, required + 1);
    if (required < 0)
    {
        free(heap_buffer);
        return NULL;
    }
    jstring result = (*env)->NewStringUTF(env, heap_buffer);
    free(heap_buffer);
    return result;
}

JNIEXPORT jstring JNICALL Java_com_sonnetdb_jni_SonnetDbJni_version(JNIEnv* env, jclass cls)
{
    (void)cls;
    return copy_string(env, p_sonnetdb_version);
}

JNIEXPORT jstring JNICALL Java_com_sonnetdb_jni_SonnetDbJni_lastError(JNIEnv* env, jclass cls)
{
    (void)cls;
    return copy_string(env, p_sonnetdb_last_error);
}
