param(
    [ValidateSet('nuget', 'bundles', 'installers', 'all')]
    [string[]]$Tasks = @('all'),
    [string]$Version = '0.1.0',
    [string]$Rid,
    [string]$Configuration = 'Release',
    [string]$OutputRoot,
    [switch]$BuildAdminUi
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    $OutputRoot = Join-Path $RepoRoot 'artifacts\release'
}

$ReleaseTasks = if ($Tasks -contains 'all')
{
    @('nuget', 'bundles', 'installers')
}
else
{
    $Tasks
}

if (($ReleaseTasks -contains 'bundles' -or $ReleaseTasks -contains 'installers') -and [string]::IsNullOrWhiteSpace($Rid))
{
    $Rid = Get-CurrentRid
}

$NuGetOutput = Join-Path $OutputRoot 'nuget'
$DocsSource = Join-Path $RepoRoot 'docs\releases'
$LicensePath = Join-Path $RepoRoot 'LICENSE'

function Pack-NuGetPackages
{
    Write-Section "Packing NuGet packages"
    Reset-Directory $NuGetOutput

    Invoke-DotNetPack 'src/TSLite/TSLite.csproj' $NuGetOutput
    Invoke-DotNetPack 'src/TSLite.Data/TSLite.Data.csproj' $NuGetOutput
    Invoke-DotNetPack 'src/TSLite.Cli/TSLite.Cli.csproj' $NuGetOutput
}

function Publish-Binaries
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetRid
    )

    $publishRoot = Join-Path $OutputRoot "publish\$TargetRid"
    $cliPublishDir = Join-Path $publishRoot 'cli'
    $serverPublishDir = Join-Path $publishRoot 'server'

    Write-Section "Publishing native binaries for $TargetRid"
    Reset-Directory $publishRoot
    Ensure-Directory $cliPublishDir
    Ensure-Directory $serverPublishDir

    $null = & dotnet publish (Join-Path $RepoRoot 'src/TSLite.Cli/TSLite.Cli.csproj') `
        -c $Configuration `
        -r $TargetRid `
        -p:PublishAot=true `
        -p:Version=$Version `
        -o $cliPublishDir `
        /warnaserror
    Assert-LastExitCode "dotnet publish TSLite.Cli ($TargetRid)"

    $null = & dotnet publish (Join-Path $RepoRoot 'src/TSLite.Server/TSLite.Server.csproj') `
        -c $Configuration `
        -r $TargetRid `
        -p:PublishAot=true `
        -p:Version=$Version `
        -p:BuildAdminUi=$($BuildAdminUi.IsPresent.ToString().ToLowerInvariant()) `
        -o $serverPublishDir `
        /warnaserror
    Assert-LastExitCode "dotnet publish TSLite.Server ($TargetRid)"

    return @{
        CliPublishDir = $cliPublishDir
        ServerPublishDir = $serverPublishDir
    }
}

function New-SdkBundle
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetRid,
        [Parameter(Mandatory = $true)]
        [string]$CliPublishDir
    )

    $bundleName = "tslite-sdk-$Version-$TargetRid"
    $bundleRoot = Join-Path $OutputRoot "staging\$TargetRid\$bundleName"
    $bundleOutputDir = Join-Path $OutputRoot "bundles\$TargetRid"

    Write-Section "Building SDK bundle for $TargetRid"
    Reset-Directory $bundleRoot
    Ensure-Directory $bundleOutputDir

    $packagesDir = Join-Path $bundleRoot 'packages'
    $cliDir = Join-Path $bundleRoot 'cli'
    $docsDir = Join-Path $bundleRoot 'docs'

    Ensure-Directory $packagesDir
    Ensure-Directory $cliDir
    Ensure-Directory $docsDir

    Copy-DirectoryContent $CliPublishDir $cliDir
    Copy-Item -LiteralPath $LicensePath -Destination $bundleRoot -Force
    Copy-ReleaseDocs $docsDir
    Copy-NuGetPackages $packagesDir
    Write-SdkBundleReadme (Join-Path $bundleRoot 'README.md') $TargetRid
    Write-CliLaunchers -RootDir $bundleRoot -TargetRid $TargetRid

    Set-BundleExecutableBits -BundleRoot $bundleRoot -TargetRid $TargetRid -IncludeServer:$false

    $archive = New-BundleArchive -BundleRoot $bundleRoot -BundleOutputDir $bundleOutputDir -BundleName $bundleName -TargetRid $TargetRid
    return @{
        BundleDirectory = $bundleRoot
        ArchivePath = $archive
    }
}

function New-ServerBundle
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetRid,
        [Parameter(Mandatory = $true)]
        [string]$CliPublishDir,
        [Parameter(Mandatory = $true)]
        [string]$ServerPublishDir
    )

    $bundleName = "tslite-server-full-$Version-$TargetRid"
    $bundleRoot = Join-Path $OutputRoot "staging\$TargetRid\$bundleName"
    $bundleOutputDir = Join-Path $OutputRoot "bundles\$TargetRid"

    Write-Section "Building Server bundle for $TargetRid"
    Reset-Directory $bundleRoot
    Ensure-Directory $bundleOutputDir

    Copy-DirectoryContent $ServerPublishDir $bundleRoot

    $cliDir = Join-Path $bundleRoot 'cli'
    $packagesDir = Join-Path $bundleRoot 'packages'
    $docsDir = Join-Path $bundleRoot 'docs'
    $systemDir = Join-Path $bundleRoot 'tslite-data\.system'

    Ensure-Directory $cliDir
    Ensure-Directory $packagesDir
    Ensure-Directory $docsDir
    Ensure-Directory $systemDir

    Copy-DirectoryContent $CliPublishDir $cliDir
    Copy-Item -LiteralPath $LicensePath -Destination $bundleRoot -Force
    Copy-ReleaseDocs $docsDir
    Copy-NuGetPackages $packagesDir

    Update-ServerBundleAppSettings (Join-Path $bundleRoot 'appsettings.json')
    New-BootstrapAuthFiles -SystemDir $systemDir
    Write-ServerBundleReadme (Join-Path $bundleRoot 'README.md') $TargetRid
    Write-CliLaunchers -RootDir $bundleRoot -TargetRid $TargetRid
    Write-ServerLaunchers -RootDir $bundleRoot -TargetRid $TargetRid

    Set-BundleExecutableBits -BundleRoot $bundleRoot -TargetRid $TargetRid -IncludeServer

    $archive = New-BundleArchive -BundleRoot $bundleRoot -BundleOutputDir $bundleOutputDir -BundleName $bundleName -TargetRid $TargetRid
    return @{
        BundleDirectory = $bundleRoot
        ArchivePath = $archive
    }
}

function New-Installers
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetRid,
        [Parameter(Mandatory = $true)]
        [string]$ServerBundleDir
    )

    $installerOutput = Join-Path $OutputRoot "installers\$TargetRid"
    Ensure-Directory $installerOutput

    switch ($TargetRid)
    {
        'win-x64' { New-MsiInstaller -ServerBundleDir $ServerBundleDir -InstallerOutputDir $installerOutput }
        'linux-x64' { New-LinuxInstallers -ServerBundleDir $ServerBundleDir -InstallerOutputDir $installerOutput }
        default { throw "Unsupported RID '$TargetRid' for installer generation." }
    }
}

function Invoke-DotNetPack
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectRelativePath,
        [Parameter(Mandatory = $true)]
        [string]$PackageOutput
    )

    & dotnet pack (Join-Path $RepoRoot $ProjectRelativePath) `
        -c $Configuration `
        -o $PackageOutput `
        /p:Version=$Version
    Assert-LastExitCode "dotnet pack $ProjectRelativePath"
}

function Copy-NuGetPackages
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    Ensure-Directory $Destination
    Get-ChildItem -LiteralPath $NuGetOutput -Filter *.nupkg | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Force
    }
}

function Copy-ReleaseDocs
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    Ensure-Directory $Destination
    Get-ChildItem -LiteralPath $DocsSource -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Force
    }
}

function Update-ServerBundleAppSettings
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$AppSettingsPath
    )

    if (-not (Test-Path $AppSettingsPath))
    {
        throw "Missing appsettings.json at '$AppSettingsPath'."
    }

    $json = Get-Content -LiteralPath $AppSettingsPath -Raw | ConvertFrom-Json
    $json.TSLiteServer.DataRoot = './tslite-data'
    $json.TSLiteServer.AutoLoadExistingDatabases = $true
    $json.TSLiteServer.AllowAnonymousProbes = $true
    $json.TSLiteServer.Tokens = [ordered]@{
        'tslite-admin-token' = 'admin'
    }

    $json | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $AppSettingsPath -Encoding utf8
}

function New-BootstrapAuthFiles
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$SystemDir
    )

    Ensure-Directory $SystemDir

    $createdAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $saltBase64 = 'ABEiM0RVZneImaq7zN3u/w=='
    $hashBase64 = 'eDh9NKddpwgeM6+ZLeY3+1vk2zlCod7Gi7bOTnsEG7U='
    $tokenHash = '63237c7cb975199c89d76b4c482edf9fb0417346975117733ad1c5e6e1d5cb18'

    $users = [ordered]@{
        version = 1
        users = @(
            [ordered]@{
                name = 'admin'
                passwordHash = $hashBase64
                salt = $saltBase64
                iterations = 100000
                isSuperuser = $true
                createdAt = $createdAt
                tokens = @(
                    [ordered]@{
                        id = 'tok_bootstrap'
                        secretHash = $tokenHash
                        createdAt = $createdAt
                        lastUsedAt = $null
                    }
                )
            }
        )
    }

    $grants = [ordered]@{
        version = 1
        grants = @()
    }

    $users | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $SystemDir 'users.json') -Encoding utf8
    $grants | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $SystemDir 'grants.json') -Encoding utf8
}

function Write-CliLaunchers
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootDir,
        [Parameter(Mandatory = $true)]
        [string]$TargetRid
    )

    if ($TargetRid -eq 'win-x64')
    {
        Set-Content -LiteralPath (Join-Path $RootDir 'tslite.cmd') -Encoding ascii -Value @'
@echo off
setlocal
"%~dp0cli\TSLite.Cli.exe" %*
'@
        return
    }

    Set-Content -LiteralPath (Join-Path $RootDir 'tslite') -Encoding utf8 -Value @'
#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$SCRIPT_DIR/cli/TSLite.Cli" "$@"
'@
}

function Write-ServerLaunchers
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootDir,
        [Parameter(Mandatory = $true)]
        [string]$TargetRid
    )

    if ($TargetRid -eq 'win-x64')
    {
        Set-Content -LiteralPath (Join-Path $RootDir 'start-tslite-server.cmd') -Encoding ascii -Value @"
@echo off
setlocal
cd /d "%~dp0"
echo TSLite.Server $Version
echo URL: http://127.0.0.1:5080/admin
echo Admin: admin / Admin123!
echo Bearer token: tslite-admin-token
".\TSLite.Server.exe"
"@
        return
    }

    $script = @'
#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"
echo "TSLite.Server __VERSION__"
echo "URL: http://127.0.0.1:5080/admin"
echo "Admin: admin / Admin123!"
echo "Bearer token: tslite-admin-token"
exec "$SCRIPT_DIR/TSLite.Server"
'@.Replace('__VERSION__', $Version)

    Set-Content -LiteralPath (Join-Path $RootDir 'start-tslite-server.sh') -Encoding utf8 -Value $script
}

function Write-SdkBundleReadme
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$TargetRid
    )

    $commandLine = if ($TargetRid -eq 'win-x64') { '.\tslite.cmd' } else { './tslite' }
    $content = @'
# TSLite SDK Bundle __VERSION__

该目录包含：

- `packages/TSLite.__VERSION__.nupkg`
- `packages/TSLite.Data.__VERSION__.nupkg`
- `packages/TSLite.Cli.__VERSION__.nupkg`
- `cli/` 原生命令行工具
- `docs/` 发布与使用说明

快速命令：

```text
__COMMAND__ version
__COMMAND__ sql --connection "Data Source=./demo-data" --command "SELECT count(*) FROM cpu"
```
'@

    $content = $content.Replace('__VERSION__', $Version).Replace('__COMMAND__', $commandLine)
    Set-Content -LiteralPath $Path -Encoding utf8 -Value $content
}

function Write-ServerBundleReadme
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$TargetRid
    )

    $startCommand = if ($TargetRid -eq 'win-x64') { '.\start-tslite-server.cmd' } else { './start-tslite-server.sh' }
    $cliCommand = if ($TargetRid -eq 'win-x64') { '.\tslite.cmd' } else { './tslite' }

    $content = @'
# TSLite Server Full Bundle __VERSION__

一键启动：

```text
__START__
```

默认信息：

- 管理后台：`http://127.0.0.1:5080/admin`
- 用户名：`admin`
- 密码：`Admin123!`
- Bearer Token：`tslite-admin-token`

CLI 示例：

```text
__CLI__ sql --connection "Data Source=tslite+http://127.0.0.1:5080/metrics;Token=tslite-admin-token" --command "SHOW DATABASES"
```
'@

    $content = $content.Replace('__VERSION__', $Version).Replace('__START__', $startCommand).Replace('__CLI__', $cliCommand)
    Set-Content -LiteralPath $Path -Encoding utf8 -Value $content
}

function Set-BundleExecutableBits
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$BundleRoot,
        [Parameter(Mandatory = $true)]
        [string]$TargetRid,
        [switch]$IncludeServer
    )

    if ($TargetRid -eq 'win-x64')
    {
        return
    }

    & chmod +x (Join-Path $BundleRoot 'cli/TSLite.Cli')
    & chmod +x (Join-Path $BundleRoot 'tslite')

    if ($IncludeServer)
    {
        & chmod +x (Join-Path $BundleRoot 'TSLite.Server')
        & chmod +x (Join-Path $BundleRoot 'start-tslite-server.sh')
    }
}

function New-BundleArchive
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$BundleRoot,
        [Parameter(Mandatory = $true)]
        [string]$BundleOutputDir,
        [Parameter(Mandatory = $true)]
        [string]$BundleName,
        [Parameter(Mandatory = $true)]
        [string]$TargetRid
    )

    Ensure-Directory $BundleOutputDir
    $archivePath = if ($TargetRid -eq 'win-x64')
    {
        Join-Path $BundleOutputDir "$BundleName.zip"
    }
    else
    {
        Join-Path $BundleOutputDir "$BundleName.tar.gz"
    }

    if (Test-Path $archivePath)
    {
        Remove-Item -LiteralPath $archivePath -Force
    }

    if ($TargetRid -eq 'win-x64')
    {
        $entries = Get-ChildItem -LiteralPath $BundleRoot -Force
        Compress-Archive -Path $entries.FullName -DestinationPath $archivePath
    }
    else
    {
        $parent = Split-Path -Parent $BundleRoot
        $name = Split-Path -Leaf $BundleRoot
        Push-Location $parent
        try
        {
            & tar -czf $archivePath $name
        }
        finally
        {
            Pop-Location
        }
    }

    Write-Sha256File $archivePath
    return $archivePath
}

function New-MsiInstaller
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerBundleDir,
        [Parameter(Mandatory = $true)]
        [string]$InstallerOutputDir
    )

    if (-not (Get-Command wix -ErrorAction SilentlyContinue))
    {
        throw 'The `wix` command was not found. Install the WiX .NET tool before generating MSI packages.'
    }

    Write-Section 'Building Windows MSI'

    $wixWorkDir = Join-Path $InstallerOutputDir 'wix'
    Reset-Directory $wixWorkDir

    $wxsPath = Join-Path $wixWorkDir 'TSLite.Server.wxs'
    $msiPath = Join-Path $InstallerOutputDir "tslite-server-$Version-win-x64.msi"

    New-WixSourceFile -ServerBundleDir $ServerBundleDir -WxsPath $wxsPath

    & wix build -arch x64 -o $msiPath $wxsPath
    Assert-LastExitCode 'wix build'
    Write-Sha256File $msiPath
}

function New-WixSourceFile
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerBundleDir,
        [Parameter(Mandatory = $true)]
        [string]$WxsPath
    )

    $files = Get-ChildItem -LiteralPath $ServerBundleDir -Recurse -File | Sort-Object FullName
    $directories = Get-ChildItem -LiteralPath $ServerBundleDir -Recurse -Directory | Sort-Object FullName

    $dirEntries = @{}
    foreach ($directory in $directories)
    {
        $rel = Get-RelativePathNormalized $ServerBundleDir $directory.FullName
        $parentRel = if ($directory.Parent.FullName -eq $ServerBundleDir)
        {
            ''
        }
        else
        {
            Get-RelativePathNormalized $ServerBundleDir $directory.Parent.FullName
        }

        $dirEntries[$rel] = [pscustomobject]@{
            Rel = $rel
            Parent = $parentRel
            Name = $directory.Name
            Id = 'DIR_' + (Get-SafeIdentifier $rel)
        }
    }

    $filesByDirectory = @{}
    foreach ($file in $files)
    {
        $parent = Split-Path -Parent $file.FullName
        $rel = if ($parent -eq $ServerBundleDir)
        {
            ''
        }
        else
        {
            Get-RelativePathNormalized $ServerBundleDir $parent
        }

        if (-not $filesByDirectory.ContainsKey($rel))
        {
            $filesByDirectory[$rel] = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
        }

        $null = $filesByDirectory[$rel].Add($file)
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $componentRefs = [System.Collections.Generic.List[string]]::new()
    $componentIndex = 0

    $lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
    $lines.Add("  <Package Name=""TSLite Server"" Manufacturer=""maikebing"" Version=""$Version"" UpgradeCode=""{7B5FA3D0-9660-4D0B-BB8B-1F293BF4F4A4}"" Language=""1033"" Scope=""perMachine"">")
    $lines.Add('    <MajorUpgrade DowngradeErrorMessage="A newer version of TSLite Server is already installed." />')
    $lines.Add('    <MediaTemplate EmbedCab="yes" />')
    $lines.Add('    <StandardDirectory Id="ProgramFiles64Folder">')
    $lines.Add('      <Directory Id="INSTALLFOLDER" Name="TSLite Server">')

    Add-WixFileComponents -Lines $lines -ComponentRefs $componentRefs -FilesByDirectory $filesByDirectory -DirectoryRel '' -ComponentIndex ([ref]$componentIndex)
    Add-WixDirectories -Lines $lines -ComponentRefs $componentRefs -DirEntries $dirEntries -FilesByDirectory $filesByDirectory -ParentRel '' -IndentLevel 4 -ComponentIndex ([ref]$componentIndex)

    $lines.Add('      </Directory>')
    $lines.Add('    </StandardDirectory>')
    $lines.Add('    <Feature Id="MainFeature" Title="TSLite Server" Level="1">')
    foreach ($componentRef in $componentRefs)
    {
        $lines.Add($componentRef)
    }
    $lines.Add('    </Feature>')
    $lines.Add('  </Package>')
    $lines.Add('</Wix>')

    $lines | Set-Content -LiteralPath $WxsPath -Encoding utf8
}

function Add-WixDirectories
{
    param(
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$Lines,
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$ComponentRefs,
        [Parameter(Mandatory = $true)]
        [hashtable]$DirEntries,
        [Parameter(Mandatory = $true)]
        [hashtable]$FilesByDirectory,
        [AllowEmptyString()]
        [string]$ParentRel,
        [Parameter(Mandatory = $true)]
        [int]$IndentLevel,
        [Parameter(Mandatory = $true)]
        [ref]$ComponentIndex
    )

    $children = $DirEntries.Values |
        Where-Object { $_.Parent -eq $ParentRel } |
        Sort-Object Name

    foreach ($child in $children)
    {
        $indent = ' ' * ($IndentLevel * 2)
        $Lines.Add("$indent<Directory Id=""$($child.Id)"" Name=""$(Escape-Xml $child.Name)"">")
        Add-WixFileComponents -Lines $Lines -ComponentRefs $ComponentRefs -FilesByDirectory $FilesByDirectory -DirectoryRel $child.Rel -ComponentIndex $ComponentIndex
        Add-WixDirectories -Lines $Lines -ComponentRefs $ComponentRefs -DirEntries $DirEntries -FilesByDirectory $FilesByDirectory -ParentRel $child.Rel -IndentLevel ($IndentLevel + 1) -ComponentIndex $ComponentIndex
        $Lines.Add("$indent</Directory>")
    }
}

function Add-WixFileComponents
{
    param(
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$Lines,
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$ComponentRefs,
        [Parameter(Mandatory = $true)]
        [hashtable]$FilesByDirectory,
        [AllowEmptyString()]
        [string]$DirectoryRel,
        [Parameter(Mandatory = $true)]
        [ref]$ComponentIndex
    )

    if (-not $FilesByDirectory.ContainsKey($DirectoryRel))
    {
        return
    }

    $indent = if ([string]::IsNullOrEmpty($DirectoryRel)) { '        ' } else { '          ' + ('  ' * ($DirectoryRel.Split('/').Count - 1)) }

    foreach ($file in $FilesByDirectory[$DirectoryRel])
    {
        $ComponentIndex.Value++
        $componentId = "CMP$($ComponentIndex.Value)"
        $fileId = "FIL$($ComponentIndex.Value)"
        $ComponentRefs.Add("      <ComponentRef Id=""$componentId"" />")
        $Lines.Add("$indent<Component Id=""$componentId"" Guid=""*"">")
        $Lines.Add("$indent  <File Id=""$fileId"" Source=""$(Escape-Xml $file.FullName)"" KeyPath=""yes"" />")
        $Lines.Add("$indent</Component>")
    }
}

function New-LinuxInstallers
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerBundleDir,
        [Parameter(Mandatory = $true)]
        [string]$InstallerOutputDir
    )

    if (-not (Get-Command nfpm -ErrorAction SilentlyContinue))
    {
        throw 'The `nfpm` command was not found. Install nFPM before generating DEB/RPM packages.'
    }

    Write-Section 'Building Linux installers'

    $configPath = Join-Path $InstallerOutputDir 'nfpm.yaml'
    $bundlePath = (Resolve-Path $ServerBundleDir).Path.Replace('\', '/')
    $escapedBundlePath = ConvertTo-YamlLiteral $bundlePath

    $yaml = @"
name: tslite-server
arch: amd64
platform: linux
version: $Version
section: database
priority: optional
maintainer: maikebing
description: |
  TSLite.Server full bundle with embedded admin UI, CLI and default local bootstrap credentials.
homepage: https://github.com/maikebing/TSLite
license: MIT
contents:
  - src: '$escapedBundlePath'
    dst: /opt/tslite-server
    type: tree
  - src: /opt/tslite-server/start-tslite-server.sh
    dst: /usr/bin/tslite-server
    type: symlink
  - src: /opt/tslite-server/tslite
    dst: /usr/bin/tslite
    type: symlink
"@

    Set-Content -LiteralPath $configPath -Encoding utf8 -Value $yaml

    $debPath = Join-Path $InstallerOutputDir "tslite-server-$Version-linux-x64.deb"
    $rpmPath = Join-Path $InstallerOutputDir "tslite-server-$Version-linux-x64.rpm"

    & nfpm package --config $configPath --packager deb --target $debPath
    Assert-LastExitCode 'nfpm package deb'
    & nfpm package --config $configPath --packager rpm --target $rpmPath
    Assert-LastExitCode 'nfpm package rpm'

    Write-Sha256File $debPath
    Write-Sha256File $rpmPath
}

function Write-Sha256File
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$Path.sha256" -Encoding ascii -Value "$hash  $(Split-Path -Leaf $Path)"
}

function Get-RelativePathNormalized
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $baseResolved = (Resolve-Path $BasePath).Path.TrimEnd('\', '/')
    $targetResolved = (Resolve-Path $TargetPath).Path

    if ($baseResolved -eq $targetResolved)
    {
        return '.'
    }

    $baseUri = [Uri]($baseResolved.Replace('\', '/') + '/')
    $targetUri = [Uri]($targetResolved.Replace('\', '/'))
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString())
}

function Get-SafeIdentifier
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ([string]::IsNullOrEmpty($Value))
    {
        return 'ROOT'
    }

    $builder = [System.Text.StringBuilder]::new()
    foreach ($character in $Value.ToCharArray())
    {
        if ([char]::IsLetterOrDigit($character))
        {
            [void]$builder.Append($character)
        }
        else
        {
            [void]$builder.Append('_')
        }
    }

    return $builder.ToString().Trim('_')
}

function Escape-Xml
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    return [System.Security.SecurityElement]::Escape($Value)
}

function ConvertTo-YamlLiteral
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    return $Value.Replace("'", "''")
}

function Get-CurrentRid
{
    if ($IsWindows) { return 'win-x64' }
    if ($IsLinux) { return 'linux-x64' }
    throw 'Only Windows and Linux are supported by the release script.'
}

function Ensure-Directory
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path $Path))
    {
        $null = New-Item -ItemType Directory -Path $Path -Force
    }
}

function Reset-Directory
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (Test-Path $Path)
    {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    $null = New-Item -ItemType Directory -Path $Path -Force
}

function Copy-DirectoryContent
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    Ensure-Directory $Destination
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

function Write-Section
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host ''
    Write-Host "==> $Message"
}

function Assert-LastExitCode
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($LASTEXITCODE -ne 0)
    {
        throw "$Context failed with exit code $LASTEXITCODE."
    }
}

Ensure-Directory $OutputRoot

if ($ReleaseTasks -contains 'nuget')
{
    Pack-NuGetPackages
}

if ($ReleaseTasks -contains 'bundles' -or $ReleaseTasks -contains 'installers')
{
    if (-not (Test-Path $NuGetOutput))
    {
        Pack-NuGetPackages
    }

    $publishInfo = Publish-Binaries -TargetRid $Rid
    $sdkBundle = New-SdkBundle -TargetRid $Rid -CliPublishDir $publishInfo['CliPublishDir']
    $serverBundle = New-ServerBundle -TargetRid $Rid -CliPublishDir $publishInfo['CliPublishDir'] -ServerPublishDir $publishInfo['ServerPublishDir']

    if ($ReleaseTasks -contains 'installers')
    {
        New-Installers -TargetRid $Rid -ServerBundleDir $serverBundle['BundleDirectory']
    }
}

Write-Host ''
Write-Host "Release outputs are available under: $OutputRoot"
