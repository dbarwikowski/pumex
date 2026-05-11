$ErrorActionPreference = "Stop"

$currentVersion = git describe --tags --abbrev=0 2>$null
if (-not $currentVersion) { $currentVersion = "v0.0.0" }

if ($currentVersion -notmatch '^v(\d+)\.(\d+)\.(\d+|x)$') {
    Write-Error "Latest tag format unrecognized: $currentVersion"
    exit 1
}

$major = [int]$matches[1]
$minor = [int]$matches[2]
$patchRaw = $matches[3]
$newPatch = if ($patchRaw -eq "x") { 0 } else { [int]$patchRaw + 1 }
$newVersion = "v$major.$minor.$newPatch"

Write-Host "Current: $currentVersion  ->  New: $newVersion"

git tag $newVersion
git push origin $newVersion

Write-Host ""
Write-Host "Done. Tag $newVersion pushed."
