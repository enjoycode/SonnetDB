//go:build cgo && (windows || linux)

package sonnetdb

/*
#cgo CFLAGS: -I${SRCDIR}/../c/include
#cgo windows LDFLAGS: -lSonnetDB.Native
#cgo linux LDFLAGS: -l:SonnetDB.Native.so

#include <stdint.h>
#include <stdlib.h>
#include "sonnetdb.h"

static char* sonnetdb_go_alloc(int32_t length)
{
    return (char*)malloc((size_t)length);
}

static void sonnetdb_go_free(char* value)
{
    free(value);
}
*/
import "C"

import (
	"errors"
	"fmt"
	"math"
	"runtime"
)

const nativeStringBufferSize = 4096

// ErrClosed is returned when an operation is attempted on a closed native handle.
var ErrClosed = errors.New("sonnetdb: native handle is closed")

// Error represents an error returned by the SonnetDB native ABI.
type Error struct {
	Message string
}

// Error returns the native error message.
func (e *Error) Error() string {
	return e.Message
}

// ValueType describes a value kind exposed by the SonnetDB C ABI.
type ValueType int

const (
	// ValueNull represents a NULL value.
	ValueNull ValueType = 0

	// ValueInt64 represents a signed 64-bit integer.
	ValueInt64 ValueType = 1

	// ValueDouble represents a 64-bit floating-point value.
	ValueDouble ValueType = 2

	// ValueBool represents a boolean value.
	ValueBool ValueType = 3

	// ValueText represents UTF-8 text.
	ValueText ValueType = 4
)

// String returns a stable display name for the value type.
func (t ValueType) String() string {
	switch t {
	case ValueNull:
		return "NULL"
	case ValueInt64:
		return "INT64"
	case ValueDouble:
		return "DOUBLE"
	case ValueBool:
		return "BOOL"
	case ValueText:
		return "TEXT"
	default:
		return fmt.Sprintf("UNKNOWN(%d)", int(t))
	}
}

// Value is the natural Go representation of one SonnetDB cell.
type Value any

// Connection is an embedded SonnetDB connection backed by the native C ABI.
type Connection struct {
	handle *C.sonnetdb_connection
}

// Result is a forward-only cursor over one SQL execution result.
type Result struct {
	handle *C.sonnetdb_result
}

type nativeStringKind int

const (
	nativeStringVersion nativeStringKind = iota
	nativeStringLastError
)

// Open opens an embedded SonnetDB database directory.
func Open(dataSource string) (*Connection, error) {
	if dataSource == "" {
		return nil, errors.New("sonnetdb: data source must not be empty")
	}

	cDataSource := C.CString(dataSource)
	defer C.sonnetdb_go_free(cDataSource)

	handle := C.sonnetdb_open(cDataSource)
	if handle == nil {
		return nil, lastError("sonnetdb_open failed")
	}

	connection := &Connection{handle: handle}
	runtime.SetFinalizer(connection, func(value *Connection) {
		_ = value.Close()
	})
	return connection, nil
}

// Version returns the loaded SonnetDB native library version.
func Version() (string, error) {
	return copyNativeString(nativeStringVersion)
}

// LastError returns the last native error message for the current native thread.
func LastError() string {
	value, err := copyNativeString(nativeStringLastError)
	if err != nil {
		return ""
	}
	return value
}

// Close releases the native connection handle. Calling Close more than once is safe.
func (c *Connection) Close() error {
	if c == nil || c.handle == nil {
		return nil
	}

	handle := c.handle
	c.handle = nil
	runtime.SetFinalizer(c, nil)
	C.sonnetdb_close(handle)
	if message := LastError(); message != "" {
		return &Error{Message: message}
	}
	return nil
}

// Execute executes one SQL statement and returns a cursor or non-query result.
func (c *Connection) Execute(sql string) (*Result, error) {
	handle, err := c.ensureOpen()
	if err != nil {
		return nil, err
	}
	if sql == "" {
		return nil, errors.New("sonnetdb: SQL must not be empty")
	}

	cSQL := C.CString(sql)
	defer C.sonnetdb_go_free(cSQL)

	result := C.sonnetdb_execute(handle, cSQL)
	if result == nil {
		return nil, lastError("sonnetdb_execute failed")
	}

	cursor := &Result{handle: result}
	runtime.SetFinalizer(cursor, func(value *Result) {
		_ = value.Close()
	})
	return cursor, nil
}

// ExecuteNonQuery executes SQL and returns the affected row count.
func (c *Connection) ExecuteNonQuery(sql string) (int, error) {
	result, err := c.Execute(sql)
	if err != nil {
		return 0, err
	}
	defer result.Close()

	return result.RecordsAffected()
}

// Flush forces pending data to durable storage through the native engine.
func (c *Connection) Flush() error {
	handle, err := c.ensureOpen()
	if err != nil {
		return err
	}
	if C.sonnetdb_flush(handle) != 0 {
		return lastError("sonnetdb_flush failed")
	}
	return nil
}

// Close releases the native result handle. Calling Close more than once is safe.
func (r *Result) Close() error {
	if r == nil || r.handle == nil {
		return nil
	}

	handle := r.handle
	r.handle = nil
	runtime.SetFinalizer(r, nil)
	C.sonnetdb_result_free(handle)
	if message := LastError(); message != "" {
		return &Error{Message: message}
	}
	return nil
}

// RecordsAffected returns INSERT/DELETE affected rows. SELECT results return -1.
func (r *Result) RecordsAffected() (int, error) {
	handle, err := r.ensureOpen()
	if err != nil {
		return 0, err
	}

	value := int(C.sonnetdb_result_records_affected(handle))
	if value < 0 {
		if message := LastError(); message != "" {
			return 0, &Error{Message: message}
		}
	}
	return value, nil
}

// ColumnCount returns the number of columns in the result.
func (r *Result) ColumnCount() (int, error) {
	handle, err := r.ensureOpen()
	if err != nil {
		return 0, err
	}

	value := int(C.sonnetdb_result_column_count(handle))
	if value < 0 {
		return 0, lastError("sonnetdb_result_column_count failed")
	}
	return value, nil
}

// ColumnName returns a result column name by zero-based ordinal.
func (r *Result) ColumnName(ordinal int) (string, error) {
	handle, err := r.ensureOpen()
	if err != nil {
		return "", err
	}
	if err := validateOrdinal(ordinal); err != nil {
		return "", err
	}

	value := C.sonnetdb_result_column_name(handle, C.int32_t(ordinal))
	if value == nil {
		return "", lastError("sonnetdb_result_column_name failed")
	}
	return C.GoString(value), nil
}

// Columns returns all result column names.
func (r *Result) Columns() ([]string, error) {
	count, err := r.ColumnCount()
	if err != nil {
		return nil, err
	}

	columns := make([]string, count)
	for i := 0; i < count; i++ {
		columns[i], err = r.ColumnName(i)
		if err != nil {
			return nil, err
		}
	}
	return columns, nil
}

// Next advances the cursor to the next row.
func (r *Result) Next() (bool, error) {
	handle, err := r.ensureOpen()
	if err != nil {
		return false, err
	}

	value := int(C.sonnetdb_result_next(handle))
	if value < 0 {
		return false, lastError("sonnetdb_result_next failed")
	}
	return value == 1, nil
}

// ValueType returns the native type of the current row value.
func (r *Result) ValueType(ordinal int) (ValueType, error) {
	handle, err := r.ensureOpen()
	if err != nil {
		return ValueNull, err
	}
	if err := validateOrdinal(ordinal); err != nil {
		return ValueNull, err
	}

	code := int(C.sonnetdb_result_value_type(handle, C.int32_t(ordinal)))
	if code < 0 {
		return ValueNull, lastError("sonnetdb_result_value_type failed")
	}
	return valueTypeFromCode(code)
}

// Int64 reads the current row value as int64.
func (r *Result) Int64(ordinal int) (int64, error) {
	valueType, err := r.ValueType(ordinal)
	if err != nil {
		return 0, err
	}
	if valueType != ValueInt64 {
		return 0, fmt.Errorf("sonnetdb: column %d is %s, not INT64", ordinal, valueType)
	}

	handle, err := r.ensureOpen()
	if err != nil {
		return 0, err
	}
	return int64(C.sonnetdb_result_value_int64(handle, C.int32_t(ordinal))), nil
}

// Double reads the current row value as float64.
func (r *Result) Double(ordinal int) (float64, error) {
	valueType, err := r.ValueType(ordinal)
	if err != nil {
		return 0, err
	}
	if valueType != ValueDouble && valueType != ValueInt64 {
		return 0, fmt.Errorf("sonnetdb: column %d is %s, not DOUBLE", ordinal, valueType)
	}

	handle, err := r.ensureOpen()
	if err != nil {
		return 0, err
	}
	return float64(C.sonnetdb_result_value_double(handle, C.int32_t(ordinal))), nil
}

// Bool reads the current row value as bool.
func (r *Result) Bool(ordinal int) (bool, error) {
	valueType, err := r.ValueType(ordinal)
	if err != nil {
		return false, err
	}
	if valueType != ValueBool {
		return false, fmt.Errorf("sonnetdb: column %d is %s, not BOOL", ordinal, valueType)
	}

	handle, err := r.ensureOpen()
	if err != nil {
		return false, err
	}
	value := int(C.sonnetdb_result_value_bool(handle, C.int32_t(ordinal)))
	if value < 0 {
		return false, lastError("sonnetdb_result_value_bool failed")
	}
	return value != 0, nil
}

// Text reads the current row value as UTF-8 text. The second return value is false for NULL.
func (r *Result) Text(ordinal int) (string, bool, error) {
	valueType, err := r.ValueType(ordinal)
	if err != nil {
		return "", false, err
	}
	if valueType == ValueNull {
		return "", false, nil
	}

	handle, err := r.ensureOpen()
	if err != nil {
		return "", false, err
	}
	value := C.sonnetdb_result_value_text(handle, C.int32_t(ordinal))
	if value == nil {
		return "", false, lastError("sonnetdb_result_value_text failed")
	}
	return C.GoString(value), true, nil
}

// Value reads the current row value using the natural Go type.
func (r *Result) Value(ordinal int) (Value, error) {
	valueType, err := r.ValueType(ordinal)
	if err != nil {
		return nil, err
	}

	switch valueType {
	case ValueNull:
		return nil, nil
	case ValueInt64:
		return r.Int64(ordinal)
	case ValueDouble:
		return r.Double(ordinal)
	case ValueBool:
		return r.Bool(ordinal)
	case ValueText:
		value, _, err := r.Text(ordinal)
		return value, err
	default:
		return nil, fmt.Errorf("sonnetdb: unsupported value type %s", valueType)
	}
}

func (c *Connection) ensureOpen() (*C.sonnetdb_connection, error) {
	if c == nil || c.handle == nil {
		return nil, ErrClosed
	}
	return c.handle, nil
}

func (r *Result) ensureOpen() (*C.sonnetdb_result, error) {
	if r == nil || r.handle == nil {
		return nil, ErrClosed
	}
	return r.handle, nil
}

func validateOrdinal(ordinal int) error {
	if ordinal < 0 || ordinal > math.MaxInt32 {
		return fmt.Errorf("sonnetdb: column ordinal %d is out of range", ordinal)
	}
	return nil
}

func valueTypeFromCode(code int) (ValueType, error) {
	switch ValueType(code) {
	case ValueNull, ValueInt64, ValueDouble, ValueBool, ValueText:
		return ValueType(code), nil
	default:
		return ValueNull, fmt.Errorf("sonnetdb: unknown value type code %d", code)
	}
}

func lastError(fallback string) error {
	message := LastError()
	if message == "" {
		message = fallback
	}
	return &Error{Message: message}
}

func copyNativeString(kind nativeStringKind) (string, error) {
	value, required, err := readNativeString(kind, nativeStringBufferSize)
	if err != nil {
		return "", err
	}
	if required < nativeStringBufferSize {
		return value, nil
	}
	value, _, err = readNativeString(kind, required+1)
	return value, err
}

func readNativeString(kind nativeStringKind, length int) (string, int, error) {
	if length <= 0 || length > math.MaxInt32 {
		return "", 0, errors.New("sonnetdb: native string buffer length is out of range")
	}

	buffer := C.sonnetdb_go_alloc(C.int32_t(length))
	if buffer == nil {
		return "", 0, errors.New("sonnetdb: cannot allocate native string buffer")
	}
	defer C.sonnetdb_go_free(buffer)

	required := int(callNativeString(kind, buffer, C.int32_t(length)))
	if required < 0 {
		return "", required, lastError("sonnetdb native string copy failed")
	}
	return C.GoString(buffer), required, nil
}

func callNativeString(kind nativeStringKind, buffer *C.char, length C.int32_t) C.int32_t {
	switch kind {
	case nativeStringVersion:
		return C.sonnetdb_version(buffer, length)
	case nativeStringLastError:
		return C.sonnetdb_last_error(buffer, length)
	default:
		return -1
	}
}
