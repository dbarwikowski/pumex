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

# ---- 1. Stop daemon and remove scheduled task ----
# The daemon registers as a per-user scheduled task ("Pumex Daemon") — no admin required.
$pumexExe = Join-Path $BinDir 'pumex.exe'
if (Test-Path $pumexExe) {
    Write-Host "Stopping pumex-daemon..."
    & $pumexExe daemon stop 2>&1 | Out-Null
    Write-Host "Removing scheduled task..."
    & $pumexExe daemon uninstall 2>&1 | Out-Null
} else {
    # Binary already gone — fall back to schtasks directly so the task doesn't linger.
    if (Get-ScheduledTask -TaskName 'Pumex Daemon' -ErrorAction SilentlyContinue) {
        schtasks /end /tn 'Pumex Daemon' 2>&1 | Out-Null
        schtasks /delete /tn 'Pumex Daemon' /f 2>&1 | Out-Null
        Write-Host "  Scheduled task 'Pumex Daemon' removed."
    }
}

# Defensive: kill any leftover daemon process so the binary isn't locked.
$proc = Get-Process -Name 'pumex-daemon' -ErrorAction SilentlyContinue
if ($proc) {
    $proc | Stop-Process -Force -ErrorAction SilentlyContinue
    $proc | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
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
