# SonnetDB Connectors

This directory contains language and driver connectors built on top of SonnetDB.

The connector layout follows the same layering used by mature time-series database ecosystems:

- `c/` provides the stable native ABI and C header. Other native bindings should prefer this layer.
- `go/` provides the Go cgo connector and a `database/sql` driver over the C ABI.
- `rust/` provides hand-maintained Rust FFI bindings plus safe connection/result wrapper types over the C ABI.
- `java/` provides the Java connector over the C ABI through a Java 8-compatible JNI backend and an optional JDK 21+ FFM backend.
- `python/` provides a dependency-free `ctypes` connector plus a small DB-API-style cursor facade over the C ABI.
- `vb6/` provides source modules for Visual Basic 6 plus a local-build x86 stdcall bridge over the C ABI.
- `purebasic/` provides a PureBasic include file that dynamically loads the C ABI.
- `odbc/` is reserved for the ODBC driver.

The first implemented connector is the C connector because it defines the small native surface that higher-level connectors can wrap without depending on .NET-specific types.

## CI Policy for Legacy BASIC Connectors

The VB6 and PureBasic connectors are kept as source-level integrations and local-build examples.

- Visual Basic 6: GitHub-hosted runners do not include a licensed VB6 IDE/compiler toolchain, and VB6 is limited to 32-bit Windows applications. CI does not build VB6 projects or VB6-produced dynamic libraries.
- PureBasic: the compiler supports command-line builds, but it is proprietary and is not preinstalled on GitHub-hosted runners. CI does not build PureBasic executables or PureBasic-produced dynamic libraries.

Use a licensed local machine or self-hosted runner if you need automated binary builds for these two connectors.

## WSL Development Environment

On Ubuntu 24.04 / WSL, install the connector toolchain with:

```bash
sudo apt-get update
sudo apt-get install -y openjdk-21-jdk build-essential clang zlib1g-dev cmake
```

The Linux connector builds also require the .NET 10 SDK:

```bash
dotnet --version
```

Build and verify the Linux x64 C connector:

```bash
cmake -S connectors/c --preset linux-x64
cmake --build artifacts/connectors/c/linux-x64
./artifacts/connectors/c/linux-x64/sonnetdb_quickstart
```

Build and verify the Linux x64 Java connector:

```bash
cmake -S connectors/java --preset linux-x64
cmake --build artifacts/connectors/java/linux-x64
cmake --build artifacts/connectors/java/linux-x64 --target run_sonnetdb_java_quickstart
cmake --build artifacts/connectors/java/linux-x64 --target run_sonnetdb_java_quickstart_ffm
```

Run the Linux x64 Go connector quickstart:

```bash
cd connectors/go
native="$(realpath ../../artifacts/connectors/c/linux-x64)"
CGO_ENABLED=1 CGO_LDFLAGS="-L$native" LD_LIBRARY_PATH="$native:${LD_LIBRARY_PATH:-}" \
  go run ./examples/quickstart
```

Run the Linux x64 Rust connector quickstart:

```bash
cd connectors/rust
native="$(realpath ../../artifacts/connectors/c/linux-x64)"
SONNETDB_NATIVE_LIB_DIR="$native" LD_LIBRARY_PATH="$native:${LD_LIBRARY_PATH:-}" \
  cargo run --example quickstart
```

Run the Linux x64 Python connector quickstart:

```bash
cd connectors/python
native="$(realpath ../../artifacts/connectors/c/linux-x64)"
SONNETDB_NATIVE_LIB_DIR="$native" LD_LIBRARY_PATH="$native:${LD_LIBRARY_PATH:-}" \
  python examples/quickstart.py
```

Run the Linux x64 PureBasic connector quickstart from a machine that already has PureBasic installed:

```bash
cd connectors/purebasic
native="$(realpath ../../artifacts/connectors/c/linux-x64)"
pbcompiler examples/quickstart.pb --console --output quickstart
LD_LIBRARY_PATH="$native:${LD_LIBRARY_PATH:-}" ./quickstart
```

When working from a Windows-mounted repo (`/mnt/<drive>/...`), prefer the connector CMake presets for Linux connector work. If a full `.slnx` restore in WSL inherits Windows NuGet fallback folders, build with an explicit Linux package path:

```bash
NUGET_PACKAGES="$HOME/.nuget/packages" NUGET_FALLBACK_PACKAGES= \
  dotnet build SonnetDB.slnx --configuration Release \
  /p:RestorePackagesPath="$HOME/.nuget/packages" \
  /p:RestoreFallbackFolders= \
  /p:RestoreAdditionalProjectFallbackFolders= \
  /p:RestoreConfigFile="$HOME/.nuget/NuGet/NuGet.Config"
```
