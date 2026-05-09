# Pumex uninstaller — removes the service registration and binaries.
# Data in $HOME\.pumex\ is kept unless -Purge is given.
#
# Usage:    iwr https://raw.githubusercontent.com/dbarwikowski/pumex/master/uninstall.ps1 | iex
# Purge:    $env:PUMEX_PURGE = '1'; iwr ... | iex
# Dev:      .\uninstall.ps1 -Purge

param(
    [switch]$Purge
)

$ErrorActionPreference = 'Stop'

if ($env:PUMEX_PURGE -eq '1') { $Purge = $true }

$BinDir  = if ($env:PUMEX_BIN_DIR) { $env:PUMEX_BIN_DIR } else { Join-Path $HOME '.pumex\bin' }
$DataDir = Join-Path $HOME '.pumex'

# ---- 1. Windows service ----
$svcName = 'pumex'
if (Get-Service -Name $svcName -ErrorAction SilentlyContinue) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Warning "Service '$svcName' is still registered. Re-run from an elevated shell to remove it, or run: pumex daemon uninstall"
    } else {
        Write-Host "Stopping service '$svcName'..."
        sc.exe stop $svcName 2>&1 | Out-Null
        # Give the SCM a moment; sc delete marks it for deletion if still stopping.
        Start-Sleep -Seconds 2
        sc.exe delete $svcName | Out-Null
        Write-Host "  Service removed."
    }
}

# ---- 2. Binaries ----
foreach ($bin in @('pumex.exe', 'pumex-daemon.exe')) {
    $path = Join-Path $BinDir $bin
    if (Test-Path $path) {
        Remove-Item -Force $path
        Write-Host "  Removed $path"
    }
}

if ((Test-Path $BinDir) -and (Get-ChildItem $BinDir | Measure-Object).Count -eq 0) {
    Remove-Item -Force $BinDir
}

# ---- 3. Data directory ----
if ($Purge) {
    if (Test-Path $DataDir) {
        Remove-Item -Recurse -Force $DataDir
        Write-Host "  Removed $DataDir"
    }
}

Write-Host ""
Write-Host "Pumex uninstalled."

if (-not $Purge -and (Test-Path $DataDir)) {
    Write-Host ""
    Write-Host "Data directory kept at: $DataDir"
    Write-Host "Remove it manually if you no longer need the index:"
    Write-Host "  Remove-Item -Recurse -Force '$DataDir'"
}
