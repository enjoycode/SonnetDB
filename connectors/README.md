# SonnetDB Connectors

This directory contains language and driver connectors built on top of SonnetDB.

The connector layout follows the same layering used by mature time-series database ecosystems:

- `c/` provides the stable native ABI and C header. Other native bindings should prefer this layer.
- `go/` is reserved for the Go connector.
- `rust/` is reserved for the Rust connector.
- `java/` provides the Java connector over the C ABI through the JDK Foreign Function & Memory API.
- `odbc/` is reserved for the ODBC driver.

The first implemented connector is the C connector because it defines the small native surface that higher-level connectors can wrap without depending on .NET-specific types.
