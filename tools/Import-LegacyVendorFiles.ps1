param (
    [Parameter(Mandatory=$true)]
    [string]$SourceDirectory,

    [Parameter(Mandatory=$false)]
    [string]$TargetDirectory = ""
)

$ErrorActionPreference = "Stop"

# Whitelisted files target names
$Whitelist = @(
    "EMVRdr35Lib.dll",
    "KioskPrint.dll",
    "QAsisIzmirimKartLibW.dll",
    "QAsisIzmirimKartLibWNet.dll",
    "QSmartCardLibW.dll",
    "QSmartCardLibWNet.dll",
    "CardLibW.dll",
    "CardLibWNet.dll",
    "QT5Core.dll",
    "libeay32.dll"
)

# Output directory path
if ([string]::IsNullOrWhiteSpace($TargetDirectory)) {
    $TargetDirectory = Join-Path $PSScriptRoot "..\local-vendor\auskiosk\win-x86"
}
$TargetDirectory = [System.IO.Path]::GetFullPath($TargetDirectory)

if (-not (Test-Path $SourceDirectory)) {
    Write-Error "Source directory does not exist: $SourceDirectory"
}

# Resolve source absolute path
$SourceDirectory = [System.IO.Path]::GetFullPath($SourceDirectory)

# Ensure target directory exists
if (-not (Test-Path $TargetDirectory)) {
    New-Item -ItemType Directory -Path $TargetDirectory -Force | Out-Null
    Write-Host "Created target directory: $TargetDirectory"
}

Write-Host "Scanning source directory for files to import..."
$FilesToProcess = Get-ChildItem -LiteralPath $SourceDirectory -File

function Get-PeMachine([string]$Path) {
    $Stream = [System.IO.File]::OpenRead($Path)
    $Reader = New-Object System.IO.BinaryReader($Stream)
    try {
        if ($Reader.ReadUInt16() -ne 0x5A4D) {
            throw "Not a PE file: $Path"
        }
        $Stream.Seek(0x3C, [System.IO.SeekOrigin]::Begin) | Out-Null
        $PeOffset = $Reader.ReadInt32()
        $Stream.Seek($PeOffset, [System.IO.SeekOrigin]::Begin) | Out-Null
        if ($Reader.ReadUInt32() -ne 0x00004550) {
            throw "Invalid PE signature: $Path"
        }
        return $Reader.ReadUInt16()
    }
    finally {
        $Reader.Dispose()
        $Stream.Dispose()
    }
}

# Check if there are any non-whitelisted files in the target copy scope
# If any file is not whitelisted, reject copying.
# However, this is an import tool, we retrieve only whitelisted files.
# But "Log, veritabanı, EXE, sertifika ve PDB kopyalamayı reddetsin" means if there is a request to copy them or they are detected, it should decline copy.
# Let's check: if we copy, we only copy the whitelist. If the user tries to import an EXE or DB etc, or if we have files in the whitelist, we copy.
# Wait, "Log, veritabanı, EXE, sertifika ve PDB kopyalamayı reddetsin" translates to:
# If a file in the source directory being copied matches one of those types and we attempt to process it, we explicitly reject and fail.

$Manifest = [ordered]@{
    "generatedAtUtc" = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
    "sourceFolder"    = [System.IO.Path]::GetFileName($SourceDirectory.TrimEnd('\', '/'))
    "files"           = @()
}

$CopiedFiles = @()

foreach ($File in $FilesToProcess) {
    $Filename = $File.Name
    $Extension = $File.Extension.ToLower()

    # Reject forbidden formats explicitly if they are in the folder
    if ($Extension -in @(".exe", ".pdb", ".cer", ".mdf", ".ldf", ".sdf", ".log", ".txt", ".ini")) {
        # However, check if this file is explicitly requested or if it's there. 
        # The instruction says "Yalnızca whitelist içindeki DLL’leri kopyalasın. Log, veritabanı, EXE, sertifika ve PDB kopyalamayı reddetsin." 
        # So we skip or throw when they are found? Oh, "kopyalamayı reddetsin" - we should explicitly check and not copy them, or throw if they are in the source and we try to import them. 
        # Let's log that they are skipped/rejected:
        Write-Host "Rejected / Refused forbidden file type during import: $Filename"
        continue
    }

    if ($Filename -in $Whitelist) {
        $Machine = Get-PeMachine -Path $File.FullName
        if ($Machine -ne 0x014C) {
            throw "Vendor file is not PE32/x86 (machine=0x$($Machine.ToString('X4'))): $Filename"
        }

        $DestPath = Join-Path $TargetDirectory $Filename
        Write-Host "Importing $Filename -> $DestPath"
        Copy-Item -Path $File.FullName -Destination $DestPath -Force
        
        # Compute SHA-256
        $HashStream = Get-FileHash -Path $DestPath -Algorithm SHA256
        $Sha256 = $HashStream.Hash.ToLower()

        $FileInfo = [ordered]@{
            "filename" = $Filename
            "sha256"   = $Sha256
            "size"     = $File.Length
        }
        $Manifest.files += $FileInfo
        $CopiedFiles += $Filename
    }
}

# Every required dependency must be present in the current installation root. Do not
# recurse into Temp because that can silently overwrite current DLLs with old versions.
$Missing = @()
foreach ($Item in $Whitelist) {
    if ($Item -notin $CopiedFiles) {
        $Missing += $Item
    }
}

if ($Missing.Count -gt 0) {
    throw "Required x86 vendor DLLs are missing from the selected installation root: $($Missing -join ', ')"
}

# Write manifest JSON
$ManifestJsonPath = Join-Path $TargetDirectory "vendor-manifest.local.json"
$Manifest | ConvertTo-Json -Depth 5 | Out-File -FilePath $ManifestJsonPath -Encoding utf8 -Force
Write-Host "Manifest written to: $ManifestJsonPath"
Write-Host "Import complete! Imported $($CopiedFiles.Count) files."
