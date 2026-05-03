#![allow(non_camel_case_types)]

use std::ffi::{c_char, c_double, c_int};

#[repr(C)]
pub struct sonnetdb_connection {
    _private: [u8; 0],
}

#[repr(C)]
pub struct sonnetdb_result {
    _private: [u8; 0],
}

pub type sonnetdb_value_type = c_int;

pub const SONNETDB_TYPE_NULL: sonnetdb_value_type = 0;
pub const SONNETDB_TYPE_INT64: sonnetdb_value_type = 1;
pub const SONNETDB_TYPE_DOUBLE: sonnetdb_value_type = 2;
pub const SONNETDB_TYPE_BOOL: sonnetdb_value_type = 3;
pub const SONNETDB_TYPE_TEXT: sonnetdb_value_type = 4;

extern "C" {
    pub fn sonnetdb_open(data_source: *const c_char) -> *mut sonnetdb_connection;
    pub fn sonnetdb_close(connection: *mut sonnetdb_connection);

    pub fn sonnetdb_execute(
        connection: *mut sonnetdb_connection,
        sql: *const c_char,
    ) -> *mut sonnetdb_result;
    pub fn sonnetdb_result_free(result: *mut sonnetdb_result);

    pub fn sonnetdb_result_records_affected(result: *mut sonnetdb_result) -> c_int;
    pub fn sonnetdb_result_column_count(result: *mut sonnetdb_result) -> c_int;
    pub fn sonnetdb_result_column_name(
        result: *mut sonnetdb_result,
        ordinal: c_int,
    ) -> *const c_char;
    pub fn sonnetdb_result_next(result: *mut sonnetdb_result) -> c_int;

    pub fn sonnetdb_result_value_type(
        result: *mut sonnetdb_result,
        ordinal: c_int,
    ) -> sonnetdb_value_type;
    pub fn sonnetdb_result_value_int64(
        result: *mut sonnetdb_result,
        ordinal: c_int,
    ) -> i64;
    pub fn sonnetdb_result_value_double(
        result: *mut sonnetdb_result,
        ordinal: c_int,
    ) -> c_double;
    pub fn sonnetdb_result_value_bool(result: *mut sonnetdb_result, ordinal: c_int) -> c_int;
    pub fn sonnetdb_result_value_text(
        result: *mut sonnetdb_result,
        ordinal: c_int,
    ) -> *const c_char;

    pub fn sonnetdb_flush(connection: *mut sonnetdb_connection) -> c_int;
    pub fn sonnetdb_version(buffer: *mut c_char, buffer_length: c_int) -> c_int;
    pub fn sonnetdb_last_error(buffer: *mut c_char, buffer_length: c_int) -> c_int;
}
