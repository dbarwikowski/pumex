#!/usr/bin/env pwsh

$processes = Get-Process pumex-daemon -ErrorAction SilentlyContinue

if ($processes) {
    Write-Host "Killing $($processes.Count) pumex-daemon instance(s)..."
    Stop-Process -InputObject $processes -Force -ErrorAction SilentlyContinue
    Write-Host "Done."
} else {
    Write-Host "No running pumex-daemon processes found."
}
