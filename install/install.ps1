# Pumex installer — downloads the latest release for this platform, drops
# pumex.exe + pumex-daemon.exe into $HOME\.pumex\bin\, and adds that to PATH.
#
# Usage:    iwr https://raw.githubusercontent.com/dbarwikowski/pumex/master/install/install.ps1 | iex
# Pinning:  $env:PUMEX_VERSION = 'v0.2.0'; iwr ... | iex

$ErrorActionPreference = 'Stop'

$Repo    = if ($env:PUMEX_REPO)    { $env:PUMEX_REPO }    else { 'dbarwikowski/pumex' }
$Version = if ($env:PUMEX_VERSION) { $env:PUMEX_VERSION } else { 'latest' }
$BinDir  = if ($env:PUMEX_BIN_DIR) { $env:PUMEX_BIN_DIR } else { Join-Path $HOME '.pumex\bin' }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

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

# ---- Stop service before replacing locked binaries ----
$service = Get-Service -Name 'pumex' -ErrorAction SilentlyContinue
$serviceWasRunning = $service -and $service.Status -ne 'Stopped'
if ($serviceWasRunning) {
    if (-not $isAdmin) {
        Write-Host "error: the pumex service is running and the binaries are locked."
        Write-Host "Re-run this script from an elevated (Administrator) shell to update."
        exit 1
    }
    Write-Host "Stopping pumex service..."
    Stop-Service -Name 'pumex' -Force
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

# ---- Restart service if it was running ----
if ($serviceWasRunning) {
    Write-Host "Restarting pumex service..."
    Start-Service -Name 'pumex'
}

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

# ---- Install daemon service if running as admin and not already installed ----
if ($isAdmin -and -not $service) {
    Write-Host "Installing pumex-daemon as a Windows service..."
    & "$BinDir\pumex.exe" daemon install
} elseif (-not $isAdmin -and -not $service) {
    Write-Host ""
    Write-Host "To register the daemon as a Windows service, run in an elevated shell:"
    Write-Host "  pumex daemon install"
}
