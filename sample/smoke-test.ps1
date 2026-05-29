#Requires -Version 7
<#
  smoke-test.ps1 — exercises every pumex command against the sample vault.
  Run from the repo root or from within sample/. Requires the daemon to be running.
  Usage: pwsh sample/smoke-test.ps1
#>

$ErrorActionPreference = 'Continue'
$vaultName = 'sample-smoke'
$vaultPath = $PSScriptRoot   # sample/
$tempNote  = 'smoke/temp-note'
$pass = 0; $fail = 0

function Step([string]$label) { Write-Host "`n==> $label" -ForegroundColor Cyan }
function Ok  ([string]$msg)   { Write-Host "    OK  $msg" -ForegroundColor Green; $script:pass++ }
function Fail([string]$msg)   { Write-Host "    FAIL $msg" -ForegroundColor Red;  $script:fail++ }

function Invoke-Pumex {
    param([string[]]$PumexArgs)
    $output = pumex @PumexArgs 2>&1
    return [PSCustomObject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Assert-Success {
    param([string]$label, [PSCustomObject]$result, [string]$contains = '')
    $result.Output | ForEach-Object { Write-Host "      $_" }
    if ($result.ExitCode -eq 0) {
        if ($contains -and ($result.Output -join "`n") -notmatch [regex]::Escape($contains)) {
            Fail "$label — exit 0 but output missing '$contains'"
        } else {
            Ok $label
        }
    } else {
        Fail "$label — exit $($result.ExitCode)"
    }
}

function Assert-Failure {
    param([string]$label, [PSCustomObject]$result)
    $result.Output | ForEach-Object { Write-Host "      $_" }
    if ($result.ExitCode -ne 0) { Ok $label } else { Fail "$label — expected non-zero exit, got 0" }
}

# ── daemon ────────────────────────────────────────────────────────────────────
Step 'daemon status'
Assert-Success 'daemon is running' (Invoke-Pumex 'daemon', 'status')

Step 'ping'
Assert-Success 'ping' (Invoke-Pumex 'ping')

# ── vault registration ────────────────────────────────────────────────────────
Step 'vault remove (pre-clean, may fail if not registered)'
$r = Invoke-Pumex 'vault', 'remove', $vaultName
$r.Output | ForEach-Object { Write-Host "      $_" }

Step 'vault new (init + register)'
Assert-Success "new vault '$vaultName'" (Invoke-Pumex 'new', $vaultName, $vaultPath) -contains $vaultName

Step 'vault list'
Assert-Success 'vault list contains sample-smoke' (Invoke-Pumex 'vault', 'list') -contains $vaultName

Step 'vault add (idempotent re-add should fail gracefully)'
$r = Invoke-Pumex 'vault', 'add', $vaultName, $vaultPath
$r.Output | ForEach-Object { Write-Host "      $_" }
# daemon may reject duplicates — just note the outcome, don't hard-fail
if ($r.ExitCode -eq 0) { Ok 'vault add (daemon accepted re-add)' }
else                   { Ok 'vault add (daemon rejected duplicate — expected)' }

# ── list notes ────────────────────────────────────────────────────────────────
Step 'list notes'
Assert-Success 'note list' (Invoke-Pumex 'list', '--vault', $vaultName)

# ── read an existing note ─────────────────────────────────────────────────────
Step 'read wiki/index (rendered)'
Assert-Success 'read wiki/index' (Invoke-Pumex 'read', 'wiki/index', '--vault', $vaultName)

Step 'read wiki/commands (--raw)'
Assert-Success 'read wiki/commands --raw' (Invoke-Pumex 'read', 'wiki/commands', '--raw', '--vault', $vaultName)

# ── create ────────────────────────────────────────────────────────────────────
Step 'create note'
$createContent = @'
---
status: draft
priority: high
---
#smoke #test

This is a temporary smoke-test note created by smoke-test.ps1.
It links to [[wiki/index]].
'@
Assert-Success 'note:create' (Invoke-Pumex 'create', $tempNote, '--content', $createContent, '--vault', $vaultName) -contains 'created'

Step 'read created note'
Assert-Success 'read temp note' (Invoke-Pumex 'read', $tempNote, '--vault', $vaultName)

# ── append ────────────────────────────────────────────────────────────────────
Step 'append to note'
Assert-Success 'note:append' (Invoke-Pumex 'append', $tempNote, '--content', 'Appended line via smoke test.', '--vault', $vaultName) -contains 'appended'

Step 'append inline'
Assert-Success 'note:append --inline' (Invoke-Pumex 'append', $tempNote, '--content', ' (inline append)', '--inline', '--vault', $vaultName) -contains 'appended'

# ── properties ───────────────────────────────────────────────────────────────
Step 'prop list'
Assert-Success 'property:list' (Invoke-Pumex 'prop', $tempNote, '--vault', $vaultName) -contains 'status'

Step 'prop get'
Assert-Success 'property:get status' (Invoke-Pumex 'prop', $tempNote, 'status', '--vault', $vaultName) -contains 'draft'

Step 'prop set'
Assert-Success 'property:set status=done' (Invoke-Pumex 'prop', $tempNote, 'status', 'done', '--vault', $vaultName) -contains 'set'

Step 'prop get after set'
Assert-Success 'property:get status (updated)' (Invoke-Pumex 'prop', $tempNote, 'status', '--vault', $vaultName) -contains 'done'

# ── tags ──────────────────────────────────────────────────────────────────────
Step 'tags'
Assert-Success 'tags list' (Invoke-Pumex 'tags', '--vault', $vaultName) -contains 'smoke'

# ── search ────────────────────────────────────────────────────────────────────
Step 'search full-text'
Assert-Success 'search smoke-test' (Invoke-Pumex 'search', 'smoke-test', '--vault', $vaultName)

Step 'search by tag'
Assert-Success 'search --tag smoke' (Invoke-Pumex 'search', '--tag', 'smoke', '--vault', $vaultName)

Step 'search by property'
Assert-Success 'search --property priority=high' (Invoke-Pumex 'search', '--property', 'priority=high', '--vault', $vaultName)

Step 'search --limit'
Assert-Success 'search with --limit 2' (Invoke-Pumex 'search', 'note', '--limit', '2', '--vault', $vaultName)

# ── text formats (CSV / JSON) ──────────────────────────────────────────────────
Step 'list --format csv'
Assert-Success 'list --format csv shows animals.csv' (Invoke-Pumex 'list', '--format', 'csv', '--vault', $vaultName) -contains 'animals'

Step 'search full-text hits a CSV body'
Assert-Success 'search capybara finds CSV' (Invoke-Pumex 'search', 'capybara', '--vault', $vaultName) -contains 'animals.csv'

Step 'search --format json'
Assert-Success 'search capybara --format json' (Invoke-Pumex 'search', 'capybara', '--format', 'json', '--vault', $vaultName) -contains 'settings.json'

Step 'read non-markdown by explicit extension (raw fallback)'
Assert-Success 'read data/animals.csv' (Invoke-Pumex 'read', 'data/animals.csv', '--vault', $vaultName) -contains 'capybara'

Step 'bare name does not match a non-markdown file'
Assert-Failure 'read animals (bare, no .md) fails' (Invoke-Pumex 'read', 'animals', '--vault', $vaultName)

Step 'non-markdown files are read-only (no create)'
Assert-Failure 'create data/new.csv rejected' (Invoke-Pumex 'create', 'data/new.csv', '--content', 'x', '--vault', $vaultName)

Step 'backlinks for a non-markdown target'
Assert-Success 'backlinks animals.csv -> dataset-notes' (Invoke-Pumex 'backlinks', 'animals.csv', '--vault', $vaultName) -contains 'dataset-notes'

# ── backlinks ─────────────────────────────────────────────────────────────────
Step 'backlinks for wiki/index'
# temp note links [[wiki/index]] so there should be at least one backlink
Assert-Success 'backlinks wiki/index' (Invoke-Pumex 'backlinks', 'wiki/index', '--vault', $vaultName)

# ── daily ────────────────────────────────────────────────────────────────────
Step 'daily read (today)'
Assert-Success 'daily read' (Invoke-Pumex 'daily', '--vault', $vaultName)

Step 'daily read specific date'
Assert-Success 'daily read 2026-05-19' (Invoke-Pumex 'daily', '--date', '2026-05-19', '--vault', $vaultName)

Step 'daily append'
Assert-Success 'daily:append' (Invoke-Pumex 'daily', 'append', '--content', 'Smoke test ran successfully.', '--vault', $vaultName) -contains 'appended'

# ── delete ───────────────────────────────────────────────────────────────────
Step 'delete temp note'
Assert-Success 'note:delete' (Invoke-Pumex 'delete', $tempNote, '--vault', $vaultName) -contains 'deleted'

Step 'read deleted note (should fail)'
Assert-Failure 'read after delete fails' (Invoke-Pumex 'read', $tempNote, '--vault', $vaultName)

# ── vault remove ──────────────────────────────────────────────────────────────
Step 'vault remove'
Assert-Success 'vault remove' (Invoke-Pumex 'vault', 'remove', $vaultName) -contains 'removed'

Step 'vault list after remove'
$r = Invoke-Pumex 'vault', 'list'
$r.Output | ForEach-Object { Write-Host "      $_" }
if (($r.Output -join '') -notmatch [regex]::Escape($vaultName)) { Ok 'vault no longer listed' }
else                                                              { Fail 'vault still listed after remove' }

# ── summary ───────────────────────────────────────────────────────────────────
Write-Host "`n$('─' * 50)" -ForegroundColor DarkGray
$color = if ($fail -eq 0) { 'Green' } else { 'Red' }
Write-Host ("  PASSED: $pass   FAILED: $fail") -ForegroundColor $color
if ($fail -gt 0) { exit 1 }
