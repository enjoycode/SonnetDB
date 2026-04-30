param(
    [switch]$RunNativePublish,
    [switch]$RunJavaCompile,
    [switch]$RunJavaFfmQuickstart,
    [switch]$RunCMake,
    [string]$VisualStudioGenerator = "Visual Studio 18 2026"
)

$ErrorActionPreference = "Stop"

function Write-Section {
    param([string]$Name)
    Write-Host ""
    Write-Host "== $Name =="
}

function Find-Command {
    param([string]$Name)
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        Write-Host "missing: $Name"
        return $false
    }

    Write-Host "found:   $Name -> $($command.Source)"
    return $true
}

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Body
    )

    Write-Section $Name
    & $Body
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..\..\..")
Set-Location $repoRoot

Write-Section "Toolchain"
$hasDotnet = Find-Command dotnet
$hasJava = Find-Command java
$hasJavac = Find-Command javac
$hasCmake = Find-Command cmake
$hasCl = Find-Command cl
$hasGcc = Find-Command gcc
$hasClang = Find-Command clang

if ($hasJava) {
    & java -version
}

if ($hasJavac) {
    & javac -version
}

if ($hasCmake) {
    & cmake --version
}

if ($RunNativePublish) {
    if (-not $hasDotnet) {
        throw "dotnet is required for -RunNativePublish."
    }

    Invoke-Step "C NativeAOT publish fallback" {
        & dotnet publish "connectors/c/native/SonnetDB.Native/SonnetDB.Native.csproj" `
            --configuration Release `
            --runtime win-x64 `
            /p:SelfContained=true `
            --output "artifacts/connectors/c/dotnet-publish-win-x64"
    }
}

if ($RunJavaCompile) {
    if (-not ($hasJava -and $hasJavac)) {
        throw "java and javac are required for -RunJavaCompile."
    }

    Invoke-Step "Java source compile fallback" {
        $base = "artifacts/connectors/java/manual-check"
        $classes = Join-Path $base "classes"
        $ffmClasses = Join-Path $base "ffm-classes"
        $exampleClasses = Join-Path $base "example-classes"

        Remove-Item -LiteralPath $base -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path $classes, $ffmClasses, $exampleClasses | Out-Null

        $mainSources = @(Get-ChildItem -Path "connectors/java/src/main/java" -Recurse -Filter "*.java" | ForEach-Object { $_.FullName })
        & javac --release 8 -Xlint:-options -d $classes $mainSources
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        $ffmSources = @(Get-ChildItem -Path "connectors/java/src/ffm/java" -Recurse -Filter "*.java" | ForEach-Object { $_.FullName })
        & javac --release 21 --enable-preview -cp $classes -d $ffmClasses $ffmSources
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        & javac --release 8 -Xlint:-options -cp $classes -d $exampleClasses "connectors/java/examples/Quickstart.java"
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}

if ($RunJavaFfmQuickstart) {
    if (-not ($hasJava -and $hasJavac)) {
        throw "java and javac are required for -RunJavaFfmQuickstart."
    }

    $nativeDll = "artifacts/connectors/c/dotnet-publish-win-x64/SonnetDB.Native.dll"
    if (-not (Test-Path $nativeDll)) {
        throw "Missing $nativeDll. Run with -RunNativePublish first."
    }

    if (-not (Test-Path "artifacts/connectors/java/manual-check/classes")) {
        throw "Missing manual Java classes. Run with -RunJavaCompile first."
    }

    Invoke-Step "Java FFM quickstart fallback" {
        $cp = "artifacts/connectors/java/manual-check/ffm-classes;artifacts/connectors/java/manual-check/classes;artifacts/connectors/java/manual-check/example-classes"
        & java "--enable-preview" "--enable-native-access=ALL-UNNAMED" `
            "-Dsonnetdb.java.backend=ffm" `
            "-Dsonnetdb.native.path=$nativeDll" `
            -cp $cp `
            com.sonnetdb.examples.Quickstart
    }
}

if ($RunCMake) {
    if (-not $hasCmake) {
        throw "cmake is required for -RunCMake."
    }

    $isVisualStudioGenerator = $VisualStudioGenerator.StartsWith("Visual Studio", [System.StringComparison]::Ordinal)
    if (-not $isVisualStudioGenerator -and -not ($hasCl -or $hasGcc -or $hasClang)) {
        throw "A C compiler/linker is required for -RunCMake. Open a VS Developer PowerShell or install VS Build Tools C++ workload."
    }

    $suffix = if ($VisualStudioGenerator -match "18 2026") { "vs2026" } elseif ($VisualStudioGenerator -match "17 2022") { "vs2022" } else { "manual" }
    $cBuild = "artifacts/connectors/c/win-x64-$suffix"
    $javaBuild = "artifacts/connectors/java/windows-x64-$suffix"

    Invoke-Step "C connector CMake configure" {
        & cmake -S "connectors/c" `
            -B $cBuild `
            -G $VisualStudioGenerator `
            -A x64 `
            -DSONNETDB_C_RID=win-x64
    }

    Invoke-Step "C connector CMake build" {
        & cmake --build $cBuild --config Release
    }

    $nativeLibraryPath = Join-Path (Resolve-Path $cBuild) "Release/SonnetDB.Native.dll"
    if (-not (Test-Path $nativeLibraryPath)) {
        throw "CMake build did not produce $nativeLibraryPath."
    }

    Invoke-Step "Java connector CMake configure" {
        & cmake -S "connectors/java" `
            -B $javaBuild `
            -G $VisualStudioGenerator `
            -A x64 `
            -DSONNETDB_JAVA_BUILD_FFM=ON `
            "-DSONNETDB_JAVA_NATIVE_LIBRARY=$nativeLibraryPath"
    }

    Invoke-Step "Java connector CMake build" {
        & cmake --build $javaBuild --config Release
    }

    Invoke-Step "Java JNI quickstart" {
        & cmake --build $javaBuild --target run_sonnetdb_java_quickstart --config Release
    }

    Invoke-Step "Java FFM quickstart" {
        & cmake --build $javaBuild --target run_sonnetdb_java_quickstart_ffm --config Release
    }
}

Write-Section "Done"
Write-Host "Connector check completed."
