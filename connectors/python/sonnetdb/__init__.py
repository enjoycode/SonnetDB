"""Python connector for SonnetDB over the native C ABI.

The connector is intentionally small and dependency-free. It loads the
``SonnetDB.Native`` library with :mod:`ctypes`, then exposes a Pythonic
connection/result API plus a light DB-API-style cursor facade.
"""

from __future__ import annotations

import ctypes
import os
import platform
from enum import IntEnum
from pathlib import Path
from typing import Any, Iterable, Iterator, Sequence

apilevel = "2.0"
threadsafety = 1
paramstyle = "qmark"

_NATIVE_STRING_BUFFER_SIZE = 4096
_LIBRARIES: dict[str, "_NativeLibrary"] = {}
_DLL_DIRECTORY_HANDLES: list[Any] = []


class Error(Exception):
    """Base exception for the SonnetDB Python connector."""


class InterfaceError(Error):
    """Raised when the connector cannot use a native handle or library."""


class DatabaseError(Error):
    """Raised when the native SonnetDB engine reports an execution error."""


class NotSupportedError(DatabaseError):
    """Raised for DB-API features not exposed by the current native ABI."""


class ValueType(IntEnum):
    """Value type codes exposed by the SonnetDB native C ABI."""

    NULL = 0
    INT64 = 1
    DOUBLE = 2
    BOOL = 3
    TEXT = 4


class _NativeLibrary:
    def __init__(self, library_path: str | os.PathLike[str] | None = None) -> None:
        path = _resolve_library_path(library_path)
        _add_dll_directory(path.parent)
        self.path = path
        self._dll = ctypes.CDLL(str(path))
        self._bind()

    def _bind(self) -> None:
        dll = self._dll

        dll.sonnetdb_open.argtypes = [ctypes.c_char_p]
        dll.sonnetdb_open.restype = ctypes.c_void_p

        dll.sonnetdb_close.argtypes = [ctypes.c_void_p]
        dll.sonnetdb_close.restype = None

        dll.sonnetdb_execute.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
        dll.sonnetdb_execute.restype = ctypes.c_void_p

        dll.sonnetdb_result_free.argtypes = [ctypes.c_void_p]
        dll.sonnetdb_result_free.restype = None

        dll.sonnetdb_result_records_affected.argtypes = [ctypes.c_void_p]
        dll.sonnetdb_result_records_affected.restype = ctypes.c_int32

        dll.sonnetdb_result_column_count.argtypes = [ctypes.c_void_p]
        dll.sonnetdb_result_column_count.restype = ctypes.c_int32

        dll.sonnetdb_result_column_name.argtypes = [ctypes.c_void_p, ctypes.c_int32]
        dll.sonnetdb_result_column_name.restype = ctypes.c_void_p

        dll.sonnetdb_result_next.argtypes = [ctypes.c_void_p]
        dll.sonnetdb_result_next.restype = ctypes.c_int32

        dll.sonnetdb_result_value_type.argtypes = [ctypes.c_void_p, ctypes.c_int32]
        dll.sonnetdb_result_value_type.restype = ctypes.c_int32

        dll.sonnetdb_result_value_int64.argtypes = [ctypes.c_void_p, ctypes.c_int32]
        dll.sonnetdb_result_value_int64.restype = ctypes.c_int64

        dll.sonnetdb_result_value_double.argtypes = [ctypes.c_void_p, ctypes.c_int32]
        dll.sonnetdb_result_value_double.restype = ctypes.c_double

        dll.sonnetdb_result_value_bool.argtypes = [ctypes.c_void_p, ctypes.c_int32]
        dll.sonnetdb_result_value_bool.restype = ctypes.c_int32

        dll.sonnetdb_result_value_text.argtypes = [ctypes.c_void_p, ctypes.c_int32]
        dll.sonnetdb_result_value_text.restype = ctypes.c_void_p

        dll.sonnetdb_flush.argtypes = [ctypes.c_void_p]
        dll.sonnetdb_flush.restype = ctypes.c_int32

        dll.sonnetdb_version.argtypes = [ctypes.c_void_p, ctypes.c_int32]
        dll.sonnetdb_version.restype = ctypes.c_int32

        dll.sonnetdb_last_error.argtypes = [ctypes.c_void_p, ctypes.c_int32]
        dll.sonnetdb_last_error.restype = ctypes.c_int32

    def version(self) -> str:
        return self._copy_native_string(self._dll.sonnetdb_version)

    def last_error(self) -> str:
        try:
            return self._copy_native_string(self._dll.sonnetdb_last_error)
        except Error:
            return ""

    def _copy_native_string(self, func: Any) -> str:
        buffer = ctypes.create_string_buffer(_NATIVE_STRING_BUFFER_SIZE)
        required = int(func(buffer, len(buffer)))
        if required < 0:
            raise DatabaseError(self.last_error() or "SonnetDB native string copy failed.")
        if required >= len(buffer):
            buffer = ctypes.create_string_buffer(required + 1)
            required = int(func(buffer, len(buffer)))
            if required < 0:
                raise DatabaseError(self.last_error() or "SonnetDB native string copy failed.")
        return buffer.value.decode("utf-8")


class Connection:
    """Embedded SonnetDB connection backed by the native C ABI."""

    def __init__(
        self,
        data_source: str | os.PathLike[str],
        *,
        library_path: str | os.PathLike[str] | None = None,
    ) -> None:
        text = os.fspath(data_source)
        if not text:
            raise InterfaceError("data_source must not be empty")

        self._native = _load_native_library(library_path)
        self._handle: int | None = self._native._dll.sonnetdb_open(_encode_utf8(text))
        if not self._handle:
            raise DatabaseError(self._native.last_error() or "sonnetdb_open failed.")

    def execute(self, sql: str) -> "Result":
        """Execute one SQL statement and return a forward-only result."""

        handle = self._require_handle()
        if not sql:
            raise InterfaceError("sql must not be empty")

        result = self._native._dll.sonnetdb_execute(handle, _encode_utf8(sql))
        if not result:
            raise DatabaseError(self._native.last_error() or "sonnetdb_execute failed.")
        return Result(self._native, result)

    def execute_non_query(self, sql: str) -> int:
        """Execute SQL and return the affected row count."""

        with self.execute(sql) as result:
            return result.records_affected

    def query(self, sql: str) -> list[tuple[Any, ...]]:
        """Execute SQL and return all rows as tuples."""

        with self.execute(sql) as result:
            return result.fetchall()

    def flush(self) -> None:
        """Force pending data to durable storage."""

        handle = self._require_handle()
        if self._native._dll.sonnetdb_flush(handle) != 0:
            raise DatabaseError(self._native.last_error() or "sonnetdb_flush failed.")

    def cursor(self) -> "Cursor":
        """Create a light DB-API-style cursor."""

        return Cursor(self)

    def commit(self) -> None:
        """DB-API compatibility method mapped to ``flush``."""

        self.flush()

    def rollback(self) -> None:
        """SonnetDB does not expose transactions through the native ABI."""

        raise NotSupportedError("transactions are not supported by the SonnetDB native ABI")

    def close(self) -> None:
        """Release the native connection handle. Calling close twice is safe."""

        if self._handle is None:
            return

        handle = self._handle
        self._handle = None
        self._native._dll.sonnetdb_close(handle)

    @property
    def closed(self) -> bool:
        """Whether this connection has been closed."""

        return self._handle is None

    def _require_handle(self) -> int:
        if self._handle is None:
            raise InterfaceError("SonnetDB connection is closed")
        return self._handle

    def __enter__(self) -> "Connection":
        self._require_handle()
        return self

    def __exit__(self, exc_type: Any, exc: Any, tb: Any) -> None:
        self.close()

    def __del__(self) -> None:
        try:
            self.close()
        except Exception:
            pass


class Result(Iterator[tuple[Any, ...]]):
    """Forward-only cursor over one SQL execution result."""

    def __init__(self, native: _NativeLibrary, handle: int) -> None:
        self._native = native
        self._handle: int | None = handle
        self._columns: list[str] | None = None

    @property
    def records_affected(self) -> int:
        """INSERT/DELETE affected rows. SELECT results return ``-1``."""

        handle = self._require_handle()
        value = int(self._native._dll.sonnetdb_result_records_affected(handle))
        if value < 0:
            message = self._native.last_error()
            if message:
                raise DatabaseError(message)
        return value

    @property
    def column_count(self) -> int:
        """Number of result columns."""

        handle = self._require_handle()
        value = int(self._native._dll.sonnetdb_result_column_count(handle))
        if value < 0:
            raise DatabaseError(
                self._native.last_error() or "sonnetdb_result_column_count failed."
            )
        return value

    @property
    def columns(self) -> list[str]:
        """Result column names."""

        if self._columns is None:
            self._columns = [self.column_name(i) for i in range(self.column_count)]
        return list(self._columns)

    def column_name(self, ordinal: int) -> str:
        """Return a result column name by zero-based ordinal."""

        ordinal = _checked_ordinal(ordinal)
        handle = self._require_handle()
        pointer = self._native._dll.sonnetdb_result_column_name(handle, ordinal)
        if not pointer:
            raise DatabaseError(
                self._native.last_error() or "sonnetdb_result_column_name failed."
            )
        return _decode_pointer(pointer)

    def next(self) -> bool:
        """Advance to the next row."""

        handle = self._require_handle()
        value = int(self._native._dll.sonnetdb_result_next(handle))
        if value < 0:
            raise DatabaseError(self._native.last_error() or "sonnetdb_result_next failed.")
        return value == 1

    def value_type(self, ordinal: int) -> ValueType:
        """Return the native value type for the current row and column."""

        ordinal = _checked_ordinal(ordinal)
        handle = self._require_handle()
        code = int(self._native._dll.sonnetdb_result_value_type(handle, ordinal))
        if code < 0:
            raise DatabaseError(
                self._native.last_error() or "sonnetdb_result_value_type failed."
            )
        try:
            return ValueType(code)
        except ValueError as ex:
            raise DatabaseError(f"unknown SonnetDB value type code: {code}") from ex

    def get_int(self, ordinal: int) -> int:
        """Read the current row value as ``int``."""

        value_type = self.value_type(ordinal)
        if value_type != ValueType.INT64:
            raise DatabaseError(f"column {ordinal} is {value_type.name}, not INT64")
        handle = self._require_handle()
        return int(self._native._dll.sonnetdb_result_value_int64(handle, ordinal))

    def get_float(self, ordinal: int) -> float:
        """Read the current row value as ``float``."""

        value_type = self.value_type(ordinal)
        if value_type not in (ValueType.DOUBLE, ValueType.INT64):
            raise DatabaseError(f"column {ordinal} is {value_type.name}, not DOUBLE")
        handle = self._require_handle()
        return float(self._native._dll.sonnetdb_result_value_double(handle, ordinal))

    def get_bool(self, ordinal: int) -> bool:
        """Read the current row value as ``bool``."""

        value_type = self.value_type(ordinal)
        if value_type != ValueType.BOOL:
            raise DatabaseError(f"column {ordinal} is {value_type.name}, not BOOL")
        handle = self._require_handle()
        value = int(self._native._dll.sonnetdb_result_value_bool(handle, ordinal))
        if value < 0:
            raise DatabaseError(
                self._native.last_error() or "sonnetdb_result_value_bool failed."
            )
        return value != 0

    def get_text(self, ordinal: int) -> str | None:
        """Read the current row value as UTF-8 text. NULL returns ``None``."""

        value_type = self.value_type(ordinal)
        if value_type == ValueType.NULL:
            return None
        handle = self._require_handle()
        pointer = self._native._dll.sonnetdb_result_value_text(
            handle, _checked_ordinal(ordinal)
        )
        if not pointer:
            raise DatabaseError(
                self._native.last_error() or "sonnetdb_result_value_text failed."
            )
        return _decode_pointer(pointer)

    def get_value(self, ordinal: int) -> Any:
        """Read the current row value using a natural Python type."""

        value_type = self.value_type(ordinal)
        if value_type == ValueType.NULL:
            return None
        if value_type == ValueType.INT64:
            return self.get_int(ordinal)
        if value_type == ValueType.DOUBLE:
            return self.get_float(ordinal)
        if value_type == ValueType.BOOL:
            return self.get_bool(ordinal)
        if value_type == ValueType.TEXT:
            return self.get_text(ordinal)
        raise DatabaseError(f"unsupported SonnetDB value type: {value_type!r}")

    def row(self) -> tuple[Any, ...]:
        """Read the current row as a tuple."""

        return tuple(self.get_value(i) for i in range(self.column_count))

    def fetchone(self) -> tuple[Any, ...] | None:
        """Fetch one row, or ``None`` when the cursor is exhausted."""

        if not self.next():
            return None
        return self.row()

    def fetchmany(self, size: int = 1) -> list[tuple[Any, ...]]:
        """Fetch up to ``size`` rows."""

        if size < 0:
            raise InterfaceError("size must be non-negative")
        rows: list[tuple[Any, ...]] = []
        for _ in range(size):
            row = self.fetchone()
            if row is None:
                break
            rows.append(row)
        return rows

    def fetchall(self) -> list[tuple[Any, ...]]:
        """Fetch all remaining rows."""

        rows: list[tuple[Any, ...]] = []
        while True:
            row = self.fetchone()
            if row is None:
                return rows
            rows.append(row)

    def close(self) -> None:
        """Release the native result handle. Calling close twice is safe."""

        if self._handle is None:
            return

        handle = self._handle
        self._handle = None
        self._native._dll.sonnetdb_result_free(handle)
        message = self._native.last_error()
        if message:
            raise DatabaseError(message)

    @property
    def closed(self) -> bool:
        """Whether this result has been closed."""

        return self._handle is None

    def _require_handle(self) -> int:
        if self._handle is None:
            raise InterfaceError("SonnetDB result is closed")
        return self._handle

    def __iter__(self) -> "Result":
        return self

    def __next__(self) -> tuple[Any, ...]:
        row = self.fetchone()
        if row is None:
            raise StopIteration
        return row

    def __enter__(self) -> "Result":
        self._require_handle()
        return self

    def __exit__(self, exc_type: Any, exc: Any, tb: Any) -> None:
        self.close()

    def __del__(self) -> None:
        try:
            self.close()
        except Exception:
            pass


class Cursor:
    """Small DB-API-style cursor wrapper over ``Connection.execute``."""

    arraysize = 1

    def __init__(self, connection: Connection) -> None:
        self.connection = connection
        self._result: Result | None = None
        self.description: tuple[tuple[Any, ...], ...] | None = None
        self.rowcount = -1

    def execute(self, sql: str, parameters: Sequence[Any] | dict[str, Any] | None = None) -> "Cursor":
        """Execute SQL.

        The current native ABI accepts a single SQL string, so non-empty
        ``parameters`` are rejected instead of interpolated.
        """

        self._ensure_parameters_empty(parameters)
        self.close_result()
        self._result = self.connection.execute(sql)
        self.rowcount = self._result.records_affected
        columns = self._result.columns
        self.description = tuple((name, None, None, None, None, None, None) for name in columns)
        return self

    def fetchone(self) -> tuple[Any, ...] | None:
        result = self._require_result()
        return result.fetchone()

    def fetchmany(self, size: int | None = None) -> list[tuple[Any, ...]]:
        result = self._require_result()
        return result.fetchmany(self.arraysize if size is None else size)

    def fetchall(self) -> list[tuple[Any, ...]]:
        result = self._require_result()
        return result.fetchall()

    def close_result(self) -> None:
        if self._result is not None:
            self._result.close()
            self._result = None

    def close(self) -> None:
        self.close_result()

    def _require_result(self) -> Result:
        if self._result is None:
            raise InterfaceError("cursor has no active result")
        return self._result

    @staticmethod
    def _ensure_parameters_empty(
        parameters: Sequence[Any] | dict[str, Any] | None,
    ) -> None:
        if parameters is None:
            return
        if isinstance(parameters, dict):
            has_parameters = len(parameters) > 0
        else:
            has_parameters = len(parameters) > 0
        if has_parameters:
            raise NotSupportedError("SQL parameters are not supported by the native ABI")

    def __enter__(self) -> "Cursor":
        return self

    def __exit__(self, exc_type: Any, exc: Any, tb: Any) -> None:
        self.close()


def connect(
    data_source: str | os.PathLike[str],
    *,
    library_path: str | os.PathLike[str] | None = None,
) -> Connection:
    """Open an embedded SonnetDB database directory."""

    return Connection(data_source, library_path=library_path)


open = connect


def version(*, library_path: str | os.PathLike[str] | None = None) -> str:
    """Return the loaded SonnetDB native library version."""

    return _load_native_library(library_path).version()


def last_error(*, library_path: str | os.PathLike[str] | None = None) -> str:
    """Return the last native error for the current thread."""

    return _load_native_library(library_path).last_error()


def _load_native_library(
    library_path: str | os.PathLike[str] | None = None,
) -> _NativeLibrary:
    path = str(_resolve_library_path(library_path))
    existing = _LIBRARIES.get(path)
    if existing is not None:
        return existing

    library = _NativeLibrary(path)
    _LIBRARIES[path] = library
    return library


def _resolve_library_path(library_path: str | os.PathLike[str] | None) -> Path:
    explicit = library_path or os.environ.get("SONNETDB_NATIVE_LIBRARY")
    if explicit:
        path = Path(explicit).expanduser().resolve()
        if path.is_dir():
            path = path / _native_library_name()
        if path.exists():
            return path
        raise InterfaceError(f"SonnetDB native library not found: {path}")

    library_dir = os.environ.get("SONNETDB_NATIVE_LIB_DIR")
    candidates: list[Path] = []
    if library_dir:
        candidates.append(Path(library_dir).expanduser() / _native_library_name())

    candidates.extend(_default_library_candidates())
    for candidate in candidates:
        resolved = candidate.resolve()
        if resolved.exists():
            return resolved

    searched = "\n".join(f"  - {candidate}" for candidate in candidates)
    raise InterfaceError(
        "SonnetDB native library was not found. Build connectors/c first or set "
        "SONNETDB_NATIVE_LIBRARY / SONNETDB_NATIVE_LIB_DIR.\nSearched:\n" + searched
    )


def _default_library_candidates() -> list[Path]:
    name = _native_library_name()
    rid = _runtime_identifier()
    package_root = Path(__file__).resolve().parents[1]
    repo_root = Path(__file__).resolve().parents[3]
    cwd = Path.cwd()

    return [
        package_root / name,
        cwd / name,
        repo_root / "artifacts" / "connectors" / "c" / rid / name,
        repo_root / "artifacts" / "connectors" / "c" / rid / "Release" / name,
        repo_root / "artifacts" / "connectors" / "c" / rid / "native" / rid / "publish" / name,
        repo_root / "artifacts" / "connectors" / "c" / "dotnet-publish-win-x64" / name,
        repo_root / "connectors" / "c" / "native" / "SonnetDB.Native" / "bin"
        / "Release" / "net10.0" / rid / "native" / name,
    ]


def _native_library_name() -> str:
    system = platform.system().lower()
    if system == "windows":
        return "SonnetDB.Native.dll"
    if system == "linux":
        return "SonnetDB.Native.so"
    raise InterfaceError(f"unsupported platform for SonnetDB native library: {platform.system()}")


def _runtime_identifier() -> str:
    system = platform.system().lower()
    machine = platform.machine().lower()
    if machine in ("amd64", "x86_64"):
        arch = "x64"
    elif machine in ("arm64", "aarch64"):
        arch = "arm64"
    elif machine in ("x86", "i386", "i686"):
        arch = "x86"
    else:
        arch = machine

    if system == "windows":
        return f"win-{arch}"
    if system == "linux":
        return f"linux-{arch}"
    return f"{system}-{arch}"


def _add_dll_directory(directory: Path) -> None:
    if hasattr(os, "add_dll_directory") and platform.system().lower() == "windows":
        _DLL_DIRECTORY_HANDLES.append(os.add_dll_directory(str(directory)))


def _encode_utf8(value: str) -> bytes:
    if "\x00" in value:
        raise InterfaceError("strings passed to the native ABI must not contain NUL bytes")
    return value.encode("utf-8")


def _decode_pointer(pointer: int) -> str:
    return ctypes.cast(pointer, ctypes.c_char_p).value.decode("utf-8")


def _checked_ordinal(ordinal: int) -> int:
    if ordinal < 0 or ordinal > 2_147_483_647:
        raise InterfaceError(f"column ordinal {ordinal} is out of range")
    return int(ordinal)


__all__ = [
    "Connection",
    "Cursor",
    "DatabaseError",
    "Error",
    "InterfaceError",
    "NotSupportedError",
    "Result",
    "ValueType",
    "apilevel",
    "connect",
    "last_error",
    "open",
    "paramstyle",
    "threadsafety",
    "version",
]
