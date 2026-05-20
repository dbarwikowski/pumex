# Pumex installer — downloads the latest release for this platform, drops
# pumex.exe and pumex-daemon.exe into $HOME\.pumex\bin\,
# and adds that to PATH.
#
# Usage:    iwr https://raw.githubusercontent.com/dbarwikowski/pumex/master/install/install.ps1 | iex
# Pinning:  $env:PUMEX_VERSION = 'v0.2.0'; iwr ... | iex

$ErrorActionPreference = 'Stop'

$Repo    = if ($env:PUMEX_REPO)    { $env:PUMEX_REPO }    else { 'dbarwikowski/pumex' }
$Version = if ($env:PUMEX_VERSION) { $env:PUMEX_VERSION } else { 'latest' }
$BinDir  = if ($env:PUMEX_BIN_DIR) { $env:PUMEX_BIN_DIR } else { Join-Path $HOME '.pumex\bin' }

# ---- Detect arch ----
$arch = switch ($env:PROCESSOR_ARCHITECTURE) {
    'AMD64' { 'x64' }
    'ARM64' { 'arm64' }
    default { throw "unsupported architecture: $($env:PROCESSOR_ARCHITECTURE)" }
}

$rid   = "win-$arch"
$asset = "pumex-$rid.zip"
$url = if ($Version -eq 'latest') {
    "https://github.com/$Repo/releases/latest/download/$asset"
} else {
    "https://github.com/$Repo/releases/download/$Version/$asset"
}

# ---- Stop daemon before replacing locked binaries ----
$pumexExe = Join-Path $BinDir 'pumex.exe'
$daemonWasRunning = $false
if (Test-Path $pumexExe) {
    & $pumexExe daemon status 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        $daemonWasRunning = $true
        Write-Host "Stopping pumex-daemon..."
        & $pumexExe daemon stop | Out-Null
    }
}

# Defensive: kill any remaining process (e.g. orphaned foreground daemon)
# so the binary isn't locked when we extract over it.
$proc = Get-Process -Name 'pumex-daemon' -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "Force-stopping leftover pumex-daemon..."
    $proc | Stop-Process -Force -ErrorAction SilentlyContinue
    $proc | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

# ---- Download + extract ----
New-Item -ItemType Directory -Force -Path $BinDir | Out-Null
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$zipPath = Join-Path $tmp $asset

try {
    Write-Host "Downloading $asset from $url..."
    Invoke-WebRequest -Uri $url -OutFile $zipPath
    Expand-Archive -Path $zipPath -DestinationPath $BinDir -Force
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}

Write-Host "Installed to $BinDir"

# ---- Add to PATH (user, permanent) ----
$userPath = [Environment]::GetEnvironmentVariable('PATH', 'User') ?? ''
$entries  = $userPath -split ';' | Where-Object { $_ -ne '' }
if ($BinDir -notin $entries) {
    [Environment]::SetEnvironmentVariable('PATH', "$BinDir;$userPath", 'User')
    Write-Host "Added $BinDir to your user PATH"
} else {
    Write-Host "$BinDir already in PATH"
}
$env:PATH = "$BinDir;$env:PATH"

# ---- Install or restart the daemon ----
# Scheduled task is per-user and runs LeastPrivilege — no admin required.
$taskExists = $null -ne (Get-ScheduledTask -TaskName 'Pumex Daemon' -ErrorAction SilentlyContinue)
if (-not $taskExists) {
    Write-Host "Registering pumex-daemon as a per-user scheduled task..."
    & "$BinDir\pumex.exe" daemon install
} elseif ($daemonWasRunning) {
    Write-Host "Starting refreshed daemon..."
    & "$BinDir\pumex.exe" daemon start
}
