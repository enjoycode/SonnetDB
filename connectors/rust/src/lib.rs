//! SonnetDB Rust connector over the native C ABI.

mod ffi;

use std::error;
use std::ffi::{c_char, c_int, CStr, CString};
use std::fmt;
use std::path::Path;
use std::ptr::NonNull;

const NATIVE_STRING_BUFFER_SIZE: usize = 4096;

/// SonnetDB Rust connector result alias.
pub type Result<T> = std::result::Result<T, Error>;

/// SonnetDB native connector error.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Error {
    message: String,
}

impl Error {
    /// Creates a connector error with the provided message.
    pub fn new(message: impl Into<String>) -> Self {
        Self {
            message: message.into(),
        }
    }

    /// Returns the error message.
    pub fn message(&self) -> &str {
        &self.message
    }
}

impl fmt::Display for Error {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(&self.message)
    }
}

impl error::Error for Error {}

/// SonnetDB C ABI value type.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ValueType {
    /// NULL value.
    Null,

    /// Signed 64-bit integer.
    Int64,

    /// 64-bit floating-point value.
    Double,

    /// Boolean value.
    Bool,

    /// UTF-8 text.
    Text,
}

impl TryFrom<c_int> for ValueType {
    type Error = Error;

    fn try_from(value: c_int) -> Result<Self> {
        match value {
            ffi::SONNETDB_TYPE_NULL => Ok(Self::Null),
            ffi::SONNETDB_TYPE_INT64 => Ok(Self::Int64),
            ffi::SONNETDB_TYPE_DOUBLE => Ok(Self::Double),
            ffi::SONNETDB_TYPE_BOOL => Ok(Self::Bool),
            ffi::SONNETDB_TYPE_TEXT => Ok(Self::Text),
            _ => Err(Error::new(format!(
                "Unknown SonnetDB value type code: {value}."
            ))),
        }
    }
}

/// Natural Rust representation of a SonnetDB cell value.
#[derive(Debug, Clone, PartialEq)]
pub enum Value {
    /// NULL value.
    Null,

    /// Signed 64-bit integer.
    Int64(i64),

    /// 64-bit floating-point value.
    Double(f64),

    /// Boolean value.
    Bool(bool),

    /// UTF-8 text.
    Text(String),
}

/// Embedded SonnetDB connection backed by the native C ABI.
pub struct Connection {
    handle: Option<NonNull<ffi::sonnetdb_connection>>,
}

impl Connection {
    /// Opens an embedded SonnetDB database directory.
    pub fn open(data_source: impl AsRef<str>) -> Result<Self> {
        let data_source = to_c_string(data_source.as_ref(), "data_source")?;
        let handle = unsafe { ffi::sonnetdb_open(data_source.as_ptr()) };
        let handle = NonNull::new(handle).ok_or_else(|| last_error_or("sonnetdb_open failed."))?;
        Ok(Self {
            handle: Some(handle),
        })
    }

    /// Opens an embedded SonnetDB database directory from a filesystem path.
    pub fn open_path(path: impl AsRef<Path>) -> Result<Self> {
        let data_source = path.as_ref().to_string_lossy();
        Self::open(data_source.as_ref())
    }

    /// Executes one SQL statement.
    pub fn execute(&self, sql: impl AsRef<str>) -> Result<ResultSet> {
        let handle = self.handle()?;
        let sql = to_c_string(sql.as_ref(), "sql")?;
        let result = unsafe { ffi::sonnetdb_execute(handle.as_ptr(), sql.as_ptr()) };
        let result = NonNull::new(result).ok_or_else(|| last_error_or("sonnetdb_execute failed."))?;
        Ok(ResultSet {
            handle: Some(result),
        })
    }

    /// Executes SQL and returns the affected row count.
    pub fn execute_non_query(&self, sql: impl AsRef<str>) -> Result<i32> {
        let result = self.execute(sql)?;
        result.records_affected()
    }

    /// Forces pending data to durable storage through the native engine.
    pub fn flush(&self) -> Result<()> {
        let handle = self.handle()?;
        let value = unsafe { ffi::sonnetdb_flush(handle.as_ptr()) };
        if value != 0 {
            return Err(last_error_or("sonnetdb_flush failed."));
        }
        Ok(())
    }

    /// Closes the native connection. Dropping the connection also closes it.
    pub fn close(mut self) -> Result<()> {
        self.close_inner()
    }

    fn handle(&self) -> Result<NonNull<ffi::sonnetdb_connection>> {
        self.handle
            .ok_or_else(|| Error::new("SonnetDB connection is closed."))
    }

    fn close_inner(&mut self) -> Result<()> {
        let Some(handle) = self.handle.take() else {
            return Ok(());
        };

        unsafe { ffi::sonnetdb_close(handle.as_ptr()) };
        let message = last_error();
        if message.is_empty() {
            Ok(())
        } else {
            Err(Error::new(message))
        }
    }
}

impl Drop for Connection {
    fn drop(&mut self) {
        let _ = self.close_inner();
    }
}

/// Forward-only cursor over a SQL execution result.
pub struct ResultSet {
    handle: Option<NonNull<ffi::sonnetdb_result>>,
}

impl ResultSet {
    /// Returns INSERT/DELETE affected rows. SELECT results return -1.
    pub fn records_affected(&self) -> Result<i32> {
        let handle = self.handle()?;
        let value = unsafe { ffi::sonnetdb_result_records_affected(handle.as_ptr()) };
        if value < 0 {
            let message = last_error();
            if !message.is_empty() {
                return Err(Error::new(message));
            }
        }
        Ok(value)
    }

    /// Returns the number of result columns.
    pub fn column_count(&self) -> Result<usize> {
        let handle = self.handle()?;
        let value = unsafe { ffi::sonnetdb_result_column_count(handle.as_ptr()) };
        if value < 0 {
            return Err(last_error_or("sonnetdb_result_column_count failed."));
        }
        Ok(value as usize)
    }

    /// Returns a result column name by zero-based ordinal.
    pub fn column_name(&self, ordinal: usize) -> Result<String> {
        let handle = self.handle()?;
        let ordinal = checked_ordinal(ordinal)?;
        let value = unsafe { ffi::sonnetdb_result_column_name(handle.as_ptr(), ordinal) };
        if value.is_null() {
            return Err(last_error_or("sonnetdb_result_column_name failed."));
        }
        Ok(unsafe { CStr::from_ptr(value) }
            .to_string_lossy()
            .into_owned())
    }

    /// Returns all result column names.
    pub fn columns(&self) -> Result<Vec<String>> {
        let count = self.column_count()?;
        let mut columns = Vec::with_capacity(count);
        for ordinal in 0..count {
            columns.push(self.column_name(ordinal)?);
        }
        Ok(columns)
    }

    /// Advances the cursor to the next row.
    pub fn next(&mut self) -> Result<bool> {
        let handle = self.handle()?;
        let value = unsafe { ffi::sonnetdb_result_next(handle.as_ptr()) };
        if value < 0 {
            return Err(last_error_or("sonnetdb_result_next failed."));
        }
        Ok(value == 1)
    }

    /// Returns the native value type for the current row and column.
    pub fn value_type(&self, ordinal: usize) -> Result<ValueType> {
        let handle = self.handle()?;
        let ordinal = checked_ordinal(ordinal)?;
        let code = unsafe { ffi::sonnetdb_result_value_type(handle.as_ptr(), ordinal) };
        if code < 0 {
            return Err(last_error_or("sonnetdb_result_value_type failed."));
        }
        ValueType::try_from(code)
    }

    /// Reads the current row value as i64.
    pub fn get_i64(&self, ordinal: usize) -> Result<i64> {
        let value_type = self.value_type(ordinal)?;
        if value_type != ValueType::Int64 {
            return Err(Error::new(format!(
                "Column {ordinal} is {value_type:?}, not Int64."
            )));
        }

        let handle = self.handle()?;
        let ordinal = checked_ordinal(ordinal)?;
        Ok(unsafe { ffi::sonnetdb_result_value_int64(handle.as_ptr(), ordinal) })
    }

    /// Reads the current row value as f64. Integer values are accepted and converted by the native ABI.
    pub fn get_f64(&self, ordinal: usize) -> Result<f64> {
        let value_type = self.value_type(ordinal)?;
        if value_type != ValueType::Double && value_type != ValueType::Int64 {
            return Err(Error::new(format!(
                "Column {ordinal} is {value_type:?}, not Double."
            )));
        }

        let handle = self.handle()?;
        let ordinal = checked_ordinal(ordinal)?;
        Ok(unsafe { ffi::sonnetdb_result_value_double(handle.as_ptr(), ordinal) } as f64)
    }

    /// Reads the current row value as bool.
    pub fn get_bool(&self, ordinal: usize) -> Result<bool> {
        let value_type = self.value_type(ordinal)?;
        if value_type != ValueType::Bool {
            return Err(Error::new(format!(
                "Column {ordinal} is {value_type:?}, not Bool."
            )));
        }

        let handle = self.handle()?;
        let ordinal = checked_ordinal(ordinal)?;
        let value = unsafe { ffi::sonnetdb_result_value_bool(handle.as_ptr(), ordinal) };
        if value < 0 {
            return Err(last_error_or("sonnetdb_result_value_bool failed."));
        }
        Ok(value != 0)
    }

    /// Reads the current row value as UTF-8 text. NULL returns `None`.
    pub fn get_text(&self, ordinal: usize) -> Result<Option<String>> {
        if self.value_type(ordinal)? == ValueType::Null {
            return Ok(None);
        }

        let handle = self.handle()?;
        let ordinal = checked_ordinal(ordinal)?;
        let value = unsafe { ffi::sonnetdb_result_value_text(handle.as_ptr(), ordinal) };
        if value.is_null() {
            return Err(last_error_or("sonnetdb_result_value_text failed."));
        }

        Ok(Some(
            unsafe { CStr::from_ptr(value) }
                .to_string_lossy()
                .into_owned(),
        ))
    }

    /// Reads the current row value using a natural Rust enum representation.
    pub fn get_value(&self, ordinal: usize) -> Result<Value> {
        match self.value_type(ordinal)? {
            ValueType::Null => Ok(Value::Null),
            ValueType::Int64 => Ok(Value::Int64(self.get_i64(ordinal)?)),
            ValueType::Double => Ok(Value::Double(self.get_f64(ordinal)?)),
            ValueType::Bool => Ok(Value::Bool(self.get_bool(ordinal)?)),
            ValueType::Text => Ok(Value::Text(self.get_text(ordinal)?.unwrap_or_default())),
        }
    }

    /// Frees the native result handle. Dropping the result also frees it.
    pub fn close(mut self) -> Result<()> {
        self.close_inner()
    }

    fn handle(&self) -> Result<NonNull<ffi::sonnetdb_result>> {
        self.handle
            .ok_or_else(|| Error::new("SonnetDB result is closed."))
    }

    fn close_inner(&mut self) -> Result<()> {
        let Some(handle) = self.handle.take() else {
            return Ok(());
        };

        unsafe { ffi::sonnetdb_result_free(handle.as_ptr()) };
        let message = last_error();
        if message.is_empty() {
            Ok(())
        } else {
            Err(Error::new(message))
        }
    }
}

impl Drop for ResultSet {
    fn drop(&mut self) {
        let _ = self.close_inner();
    }
}

/// Returns the loaded SonnetDB native library version.
pub fn version() -> Result<String> {
    copy_utf8(ffi::sonnetdb_version, "sonnetdb_version failed.")
}

/// Returns the last native error message for the current native thread.
pub fn last_error() -> String {
    copy_utf8_raw(ffi::sonnetdb_last_error).unwrap_or_default()
}

fn to_c_string(value: &str, name: &str) -> Result<CString> {
    CString::new(value).map_err(|_| Error::new(format!("{name} must not contain NUL bytes.")))
}

fn checked_ordinal(ordinal: usize) -> Result<c_int> {
    c_int::try_from(ordinal)
        .map_err(|_| Error::new(format!("Column ordinal {ordinal} is out of range.")))
}

fn last_error_or(fallback: &str) -> Error {
    let message = last_error();
    if message.is_empty() {
        Error::new(fallback)
    } else {
        Error::new(message)
    }
}

fn copy_utf8(
    func: unsafe extern "C" fn(*mut c_char, c_int) -> c_int,
    fallback: &str,
) -> Result<String> {
    copy_utf8_raw(func).map_err(|_| last_error_or(fallback))
}

fn copy_utf8_raw(
    func: unsafe extern "C" fn(*mut c_char, c_int) -> c_int,
) -> std::result::Result<String, ()> {
    let mut buffer = vec![0 as c_char; NATIVE_STRING_BUFFER_SIZE];
    let required = unsafe { func(buffer.as_mut_ptr(), buffer.len() as c_int) };
    if required < 0 {
        return Err(());
    }

    if required as usize >= buffer.len() {
        buffer = vec![0 as c_char; required as usize + 1];
        let second = unsafe { func(buffer.as_mut_ptr(), buffer.len() as c_int) };
        if second < 0 {
            return Err(());
        }
    }

    Ok(unsafe { CStr::from_ptr(buffer.as_ptr()) }
        .to_string_lossy()
        .into_owned())
}
