param (
    [Parameter(Mandatory=$true)]
    [string]$VendorSourceDirectory,

    [Parameter(Mandatory=$false)]
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

if ($env:OS -ne "Windows_NT") {
    throw "The Windows 7 hardware package must be built on Windows with the .NET Framework 4.8 targeting pack installed."
}

$RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $RepositoryRoot "artifacts\win7-net40-real-card-kiosk"
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

$LocalVendorDirectory = Join-Path $RepositoryRoot "local-vendor\auskiosk\win-x86"
& (Join-Path $PSScriptRoot "Import-LegacyVendorFiles.ps1") `
    -SourceDirectory $VendorSourceDirectory `
    -TargetDirectory $LocalVendorDirectory

$MsBuild = Get-Command "MSBuild.exe" -ErrorAction SilentlyContinue
if ($null -eq $MsBuild) {
    $VsWhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $VsWhere) {
        $MsBuildPath = & $VsWhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($MsBuildPath)) {
            $MsBuild = Get-Item $MsBuildPath
        }
    }
}

if ($null -eq $MsBuild) {
    throw "MSBuild.exe was not found. Install Visual Studio Build Tools with the .NET Framework 4.8 targeting pack."
}

$ContractsProject = Join-Path $RepositoryRoot "src\IzbanKiosk.LegacyHardware.Contracts.Net40\IzbanKiosk.LegacyHardware.Contracts.Net40.csproj"
$BridgeProject = Join-Path $RepositoryRoot "src\IzbanKiosk.LegacyHardwareBridge\IzbanKiosk.LegacyHardwareBridge.csproj"
$PrototypeProject = Join-Path $RepositoryRoot "src\IzbanKiosk.Win7Prototype\IzbanKiosk.Win7Prototype.csproj"

foreach ($Project in @($ContractsProject, $BridgeProject, $PrototypeProject)) {
    & $MsBuild.FullName $Project /restore /t:Rebuild /m:1 /p:Configuration=Release /p:Platform=x86
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed: $Project"
    }
}

$BridgeBuildDirectory = Join-Path $RepositoryRoot "src\IzbanKiosk.LegacyHardwareBridge\bin\x86\Release\net40"
$PrototypeBuildDirectory = Join-Path $RepositoryRoot "src\IzbanKiosk.Win7Prototype\bin\x86\Release\net40"
$BridgeOutput = Join-Path $OutputDirectory "Bridge"

if (Test-Path $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $BridgeOutput -Force | Out-Null

$BridgeRuntimeFiles = @(
    "IzbanKiosk.LegacyHardwareBridge.exe",
    "IzbanKiosk.LegacyHardwareBridge.exe.config",
    "IzbanKiosk.LegacyHardware.Contracts.dll",
    "Newtonsoft.Json.dll"
)
foreach ($RuntimeFile in $BridgeRuntimeFiles) {
    Copy-Item -LiteralPath (Join-Path $BridgeBuildDirectory $RuntimeFile) -Destination $BridgeOutput -Force
}

$PrototypeRuntimeFiles = @(
    "IZBAN-Kiosk.exe",
    "IZBAN-Kiosk.exe.config",
    "IzbanKiosk.LegacyHardware.Contracts.dll",
    "Newtonsoft.Json.dll"
)
foreach ($RuntimeFile in $PrototypeRuntimeFiles) {
    Copy-Item -LiteralPath (Join-Path $PrototypeBuildDirectory $RuntimeFile) -Destination $OutputDirectory -Force
}

$HardwareSettings = Join-Path $PrototypeBuildDirectory "KioskHardware.config.json"
if (-not (Test-Path $HardwareSettings)) {
    throw "Required hardware settings file was not copied by the Win7 kiosk build: $HardwareSettings"
}
Copy-Item -LiteralPath $HardwareSettings -Destination $OutputDirectory -Force

Copy-Item -Path (Join-Path $LocalVendorDirectory "*.dll") -Destination $BridgeOutput -Force
Copy-Item -Path (Join-Path $LocalVendorDirectory "vendor-manifest.local.json") -Destination $BridgeOutput -Force

$BridgeExe = Join-Path $BridgeOutput "IzbanKiosk.LegacyHardwareBridge.exe"
$PrototypeExe = Join-Path $OutputDirectory "IZBAN-Kiosk.exe"
if (-not (Test-Path $BridgeExe) -or -not (Test-Path $PrototypeExe)) {
    throw "Expected Windows 7 executables were not produced."
}

$PackageFiles = Get-ChildItem -Path $OutputDirectory -File -Recurse | ForEach-Object {
    [ordered]@{
        path = $_.FullName.Substring($OutputDirectory.Length).TrimStart('\')
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        size = $_.Length
    }
}
$PackageManifest = [ordered]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
    purpose = "One-click .NET Framework 4.0 Client Profile Windows 7 kiosk for synchronized NFC/SAM/printer health and real İzmirim Kart identity/balance reading"
    files = @($PackageFiles)
}
$PackageManifest | ConvertTo-Json -Depth 6 | Out-File `
    -FilePath (Join-Path $OutputDirectory "package-manifest.json") `
    -Encoding utf8 `
    -Force

Write-Host "Windows 7 real-card kiosk package prepared: $OutputDirectory"
Write-Host "No card write/top-up or POS operation is enabled in this package."
