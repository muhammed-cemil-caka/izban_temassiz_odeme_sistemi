param (
    [Parameter(Mandatory=$false)]
    [string]$ComPort = "COM4",

    [Parameter(Mandatory=$false)]
    [string]$PrinterName = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:IZBAN_HMAC_SECRET)) {
    throw "IZBAN_HMAC_SECRET is missing. Set a Base64-encoded random key of at least 32 bytes in this PowerShell session."
}

$BridgeExe = Join-Path $PSScriptRoot "Bridge\IzbanKiosk.LegacyHardwareBridge.exe"
if (-not (Test-Path $BridgeExe)) {
    throw "Bridge executable was not found: $BridgeExe"
}

# When -PrinterName is omitted the bridge reads ThermalPrinterName from
# KioskHardware.config.json next to itself or in the package root. Passing an empty
# name here would instead leave the printer unconfigured and every receipt would fail.
$Arguments = @("--port", $ComPort)
if (-not [string]::IsNullOrWhiteSpace($PrinterName)) {
    $Arguments += @("--printer", $PrinterName)
}

& $BridgeExe @Arguments
exit $LASTEXITCODE
