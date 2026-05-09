# Pumex installer — downloads the latest release for this platform and drops
# pumex.exe + pumex-daemon.exe into $HOME\.pumex\bin\.
#
# Usage:    iwr https://raw.githubusercontent.com/dbarwikowski/pumex/main/install.ps1 | iex
# Pinning:  $env:PUMEX_VERSION = 'v0.2.0'; iwr ... | iex

$ErrorActionPreference = 'Stop'

$Repo    = if ($env:PUMEX_REPO)    { $env:PUMEX_REPO }    else { 'dbarwikowski/pumex' }
$Version = if ($env:PUMEX_VERSION) { $env:PUMEX_VERSION } else { 'latest' }
$BinDir  = if ($env:PUMEX_BIN_DIR) { $env:PUMEX_BIN_DIR } else { Join-Path $HOME '.pumex/bin' }

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

# ---- Hints ----
Write-Host ""
Write-Host "Installed:"
Write-Host "  $BinDir\pumex.exe"
Write-Host "  $BinDir\pumex-daemon.exe"
Write-Host ""
Write-Host "Add to PATH (current session):"
Write-Host "  `$env:PATH = `"$BinDir;`$env:PATH`""
Write-Host ""
Write-Host "Then install the daemon as a Windows service (requires admin):"
Write-Host "  pumex daemon install"
