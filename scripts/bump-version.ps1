$versionPath = "..\VERSION"

$ErrorActionPreference = "Stop"

if (!(Test-Path $versionPath)) {
    Write-Error "VERSION file not found."
    exit 1
}

$currentVersion = (Get-Content $versionPath -Raw).Trim()

# Match:
# v0.1.6
# v0.2.x
if ($currentVersion -notmatch '^v(\d+)\.(\d+)\.(\d+|x)$') {
    Write-Error "Invalid VERSION format. Expected: v0.1.6 or v0.2.x"
    exit 1
}

$major = [int]$matches[1]
$minor = [int]$matches[2]
$patchRaw = $matches[3]

if ($patchRaw -eq "x") {
    $newPatch = 0
}
else {
    $newPatch = [int]$patchRaw + 1
}

$newVersion = "v$major.$minor.$newPatch"

Write-Host "Current version: $currentVersion"
Write-Host "New version:     $newVersion"

# Update VERSION file
Set-Content -Path $versionPath -Value $newVersion

# Commit VERSION
git add VERSION
git commit -m "Bump version to $newVersion"

# Create tag
git tag $newVersion

# Push
git push
git push origin $newVersion

Write-Host ""
Write-Host "Done."
Write-Host "Created and pushed tag: $newVersion"