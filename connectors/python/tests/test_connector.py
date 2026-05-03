from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import sonnetdb


class SonnetDbPythonConnectorTests(unittest.TestCase):
    def test_execute_and_fetch_rows(self) -> None:
        with tempfile.TemporaryDirectory(prefix="sonnetdb-python-test-") as data_dir:
            with sonnetdb.connect(data_dir) as connection:
                connection.execute_non_query(
                    "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT, active FIELD BOOL)"
                )
                inserted = connection.execute_non_query(
                    "INSERT INTO cpu (time, host, usage, active) VALUES "
                    "(1710000000000, 'edge-1', 0.42, true),"
                    "(1710000001000, 'edge-1', 0.73, false)"
                )

                self.assertEqual(2, inserted)

                with connection.execute(
                    "SELECT time, host, usage, active FROM cpu WHERE host = 'edge-1' LIMIT 10"
                ) as result:
                    self.assertEqual(["time", "host", "usage", "active"], result.columns)
                    self.assertEqual(
                        [
                            (1710000000000, "edge-1", 0.42, True),
                            (1710000001000, "edge-1", 0.73, False),
                        ],
                        result.fetchall(),
                    )

    def test_cursor_facade(self) -> None:
        with tempfile.TemporaryDirectory(prefix="sonnetdb-python-test-") as data_dir:
            with sonnetdb.connect(data_dir) as connection:
                with connection.cursor() as cursor:
                    cursor.execute("CREATE MEASUREMENT m (v FIELD INT)")
                    self.assertEqual(0, cursor.rowcount)

                    cursor.execute("INSERT INTO m (time, v) VALUES (1, 7)")
                    self.assertEqual(1, cursor.rowcount)

                    cursor.execute("SELECT time, v FROM m")
                    self.assertEqual(("time", "v"), tuple(col[0] for col in cursor.description or ()))
                    self.assertEqual((1, 7), cursor.fetchone())
                    self.assertIsNone(cursor.fetchone())

    def test_rejects_parameters_until_native_abi_supports_them(self) -> None:
        with tempfile.TemporaryDirectory(prefix="sonnetdb-python-test-") as data_dir:
            with sonnetdb.connect(data_dir) as connection:
                with connection.cursor() as cursor:
                    with self.assertRaises(sonnetdb.NotSupportedError):
                        cursor.execute("SELECT ?", [1])


if __name__ == "__main__":
    unittest.main()
