# SonnetDB Connectors

This directory contains language and driver connectors built on top of SonnetDB.

The connector layout follows the same layering used by mature time-series database ecosystems:

- `c/` provides the stable native ABI and C header. Other native bindings should prefer this layer.
- `go/` is reserved for the Go connector.
- `rust/` is reserved for the Rust connector.
- `java/` provides the Java connector over the C ABI through a Java 8-compatible JNI backend and an optional JDK 21+ FFM backend.
- `odbc/` is reserved for the ODBC driver.

The first implemented connector is the C connector because it defines the small native surface that higher-level connectors can wrap without depending on .NET-specific types.

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

When working from a Windows-mounted repo (`/mnt/<drive>/...`), prefer the connector CMake presets for Linux connector work. If a full `.slnx` restore in WSL inherits Windows NuGet fallback folders, build with an explicit Linux package path:

```bash
NUGET_PACKAGES="$HOME/.nuget/packages" NUGET_FALLBACK_PACKAGES= \
  dotnet build SonnetDB.slnx --configuration Release \
  /p:RestorePackagesPath="$HOME/.nuget/packages" \
  /p:RestoreFallbackFolders= \
  /p:RestoreAdditionalProjectFallbackFolders= \
  /p:RestoreConfigFile="$HOME/.nuget/NuGet/NuGet.Config"
```
