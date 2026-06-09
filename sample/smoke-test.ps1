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

# ── text formats (CSV / TSV / JSON) ───────────────────────────────────────────
Step 'list --format csv'
Assert-Success 'list --format csv shows animals.csv' (Invoke-Pumex 'list', '--format', 'csv', '--vault', $vaultName) -contains 'animals'

Step 'list --format tsv'
Assert-Success 'list --format tsv shows projects.tsv' (Invoke-Pumex 'list', '--format', 'tsv', '--vault', $vaultName) -contains 'projects'

Step 'search full-text hits a CSV body'
# The Note column shows the extension-less name; format is its own column. Assert on
# the name (short, never wraps) rather than the path (wraps on long snippets).
Assert-Success 'search capybara finds animals' (Invoke-Pumex 'search', 'capybara', '--vault', $vaultName) -contains 'animals'

Step 'search --format json'
Assert-Success 'search capybara --format json' (Invoke-Pumex 'search', 'capybara', '--format', 'json', '--vault', $vaultName) -contains 'settings'

Step 'read data/settings.json (JSON rendering, JSONC tolerated)'
Assert-Success 'read data/settings.json' (Invoke-Pumex 'read', 'data/settings.json', '--vault', $vaultName) -contains 'capybara'

Step 'JSON top-level scalars become properties'
Assert-Success 'prop get theme from settings.json' (Invoke-Pumex 'prop', 'data/settings.json', 'theme', '--vault', $vaultName) -contains 'dark'

Step 'search by a property extracted from JSON'
Assert-Success 'search --property theme=dark --format json' (Invoke-Pumex 'search', '--property', 'theme=dark', '--format', 'json', '--vault', $vaultName) -contains 'settings'

Step 'read data/events.json --limit 2 (array-root cap)'
Assert-Success 'read events.json --limit 2' (Invoke-Pumex 'read', 'data/events.json', '--limit', '2', '--vault', $vaultName) -contains 'showing 2 of 5 elements'

# ── YAML ──────────────────────────────────────────────────────────────────────
Step 'list --format yaml'
Assert-Success 'list --format yaml shows config.yaml' (Invoke-Pumex 'list', '--format', 'yaml', '--vault', $vaultName) -contains 'config'

Step 'read data/config.yaml (YAML rendering, mapping root)'
Assert-Success 'read data/config.yaml' (Invoke-Pumex 'read', 'data/config.yaml', '--vault', $vaultName) -contains 'environment'

Step 'YAML top-level scalars become properties'
Assert-Success 'prop get environment from config.yaml' (Invoke-Pumex 'prop', 'data/config.yaml', 'environment', '--vault', $vaultName) -contains 'prod'

Step 'search by a property extracted from YAML'
Assert-Success 'search --property environment=prod --format yaml' (Invoke-Pumex 'search', '--property', 'environment=prod', '--format', 'yaml', '--vault', $vaultName) -contains 'config'

Step 'search full-text hits a YAML body'
Assert-Success 'search euwest finds config' (Invoke-Pumex 'search', 'euwest', '--vault', $vaultName) -contains 'config'

Step 'read data/roster.yaml --limit 2 (sequence-root cap)'
Assert-Success 'read roster.yaml --limit 2' (Invoke-Pumex 'read', 'data/roster.yaml', '--limit', '2', '--vault', $vaultName) -contains 'showing 2 of 5 elements'

Step 'backlinks config.yaml -> dataset-notes'
Assert-Success 'backlinks config.yaml -> dataset-notes' (Invoke-Pumex 'backlinks', 'config.yaml', '--vault', $vaultName) -contains 'dataset-notes'

Step 'read non-markdown by explicit extension (raw fallback)'
Assert-Success 'read data/animals.csv' (Invoke-Pumex 'read', 'data/animals.csv', '--vault', $vaultName) -contains 'capybara'

Step 'read data/expenses.csv (table rendering)'
Assert-Success 'read data/expenses.csv' (Invoke-Pumex 'read', 'data/expenses.csv', '--vault', $vaultName) -contains 'food'

Step 'read data/expenses.csv --limit 3'
Assert-Success 'read expenses.csv --limit 3' (Invoke-Pumex 'read', 'data/expenses.csv', '--limit', '3', '--vault', $vaultName) -contains 'food'

Step 'read data/projects.tsv (table rendering)'
Assert-Success 'read data/projects.tsv' (Invoke-Pumex 'read', 'data/projects.tsv', '--vault', $vaultName) -contains 'daemon'

Step 'read data/projects.tsv --limit 2'
Assert-Success 'read projects.tsv --limit 2' (Invoke-Pumex 'read', 'data/projects.tsv', '--limit', '2', '--vault', $vaultName) -contains 'daemon'

Step 'search full-text hits expenses.csv body'
Assert-Success 'search Copilot finds expenses' (Invoke-Pumex 'search', 'Copilot', '--vault', $vaultName) -contains 'expenses'

Step 'search full-text hits projects.tsv body'
Assert-Success 'search agentsmith finds projects' (Invoke-Pumex 'search', 'agentsmith', '--vault', $vaultName) -contains 'projects'

Step 'bare name does not match a non-markdown file'
Assert-Failure 'read animals (bare, no .md) fails' (Invoke-Pumex 'read', 'animals', '--vault', $vaultName)

Step 'non-markdown files are read-only (no create)'
Assert-Failure 'create data/new.csv rejected' (Invoke-Pumex 'create', 'data/new.csv', '--content', 'x', '--vault', $vaultName)

Step 'backlinks for a non-markdown target'
Assert-Success 'backlinks animals.csv -> dataset-notes' (Invoke-Pumex 'backlinks', 'animals.csv', '--vault', $vaultName) -contains 'dataset-notes'

Step 'backlinks expenses.csv -> dataset-notes'
Assert-Success 'backlinks expenses.csv -> dataset-notes' (Invoke-Pumex 'backlinks', 'expenses.csv', '--vault', $vaultName) -contains 'dataset-notes'

Step 'backlinks projects.tsv -> dataset-notes'
Assert-Success 'backlinks projects.tsv -> dataset-notes' (Invoke-Pumex 'backlinks', 'projects.tsv', '--vault', $vaultName) -contains 'dataset-notes'

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

# ── note checkboxes (read --tasks / check) ────────────────────────────────────
Step 'read --tasks lists checkbox items'
Assert-Success 'read checklist --tasks' (Invoke-Pumex 'read', 'checklist', '--tasks', '--vault', $vaultName) -contains 'quokkacheck'

Step 'read --tasks ignores fenced pseudo-checkboxes'
$tasksOut = Invoke-Pumex 'read', 'checklist', '--tasks', '--vault', $vaultName
if (($tasksOut.Output -join "`n") -notmatch 'fenced item') { Ok 'fenced checkbox ignored' } else { Fail 'fenced checkbox leaked into task list' }

Step 'read --tasks --pending hides checked items'
$pendingOut = Invoke-Pumex 'read', 'checklist', '--tasks', '--pending', '--vault', $vaultName
Assert-Success 'read checklist --tasks --pending' $pendingOut -contains 'quokkacheck'
if (($pendingOut.Output -join "`n") -notmatch 'already done') { Ok '--pending hides checked item' } else { Fail '--pending still shows checked item' }

Step 'check toggles a checkbox on'
Assert-Success 'check checklist 1' (Invoke-Pumex 'check', 'checklist', '1', '--vault', $vaultName) -contains 'checked'

Step 'check again toggles it back (restore fixture)'
Assert-Success 'check checklist 1 (undo)' (Invoke-Pumex 'check', 'checklist', '1', '--vault', $vaultName) -contains 'unchecked'

# ── task notes (create / read / list / status / attach) ───────────────────────
Step 'task list shows the committed sample task'
Assert-Success 'task list' (Invoke-Pumex 'task', 'list', '--vault', $vaultName) -contains 'sample_task'

Step 'task read sample_task'
Assert-Success 'task read sample_task' (Invoke-Pumex 'task', 'read', 'sample_task', '--vault', $vaultName) -contains 'wombattask'

Step 'task status get'
Assert-Success 'task status sample_task' (Invoke-Pumex 'task', 'status', 'sample_task', '--vault', $vaultName) -contains 'NEW'

Step 'task status set DONE (stamps completed)'
Assert-Success 'task status sample_task DONE' (Invoke-Pumex 'task', 'status', 'sample_task', 'DONE', '--vault', $vaultName) -contains 'DONE'

Step 'task list --status DONE'
Assert-Success 'task list --status DONE' (Invoke-Pumex 'task', 'list', '--status', 'DONE', '--vault', $vaultName) -contains 'sample_task'

Step 'task list --open hides DONE tasks'
$openOut = Invoke-Pumex 'task', 'list', '--open', '--vault', $vaultName
if (($openOut.Output -join "`n") -notmatch 'sample_task') { Ok '--open hides DONE task' } else { Fail '--open still shows DONE task' }

Step 'task status restore NEW (restore fixture)'
Assert-Success 'task status sample_task NEW' (Invoke-Pumex 'task', 'status', 'sample_task', 'NEW', '--vault', $vaultName) -contains 'NEW'

Step 'task create (new dated folder)'
Assert-Success 'task create' (Invoke-Pumex 'task', 'create', 'smoke generated task', '--content', 'Created by smoke test.', '--vault', $vaultName) -contains 'created'

Step 'task attach (move a file into the task folder)'
$attachSrc = Join-Path $vaultPath 'smoke-attach.txt'
Set-Content -Path $attachSrc -Value 'attachment body'
Assert-Success 'task attach' (Invoke-Pumex 'task', 'attach', 'smoke_generated_task', $attachSrc, '--vault', $vaultName) -contains 'attached'

Step 'cleanup: remove generated task folder(s) and any stray attach source'
$today = (Get-Date).ToString('yyyy-MM-dd')
Get-ChildItem -Path (Join-Path $vaultPath 'tasks') -Directory -Filter "task_${today}_*" -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $attachSrc) { Remove-Item $attachSrc -Force }
Ok 'generated task artifacts cleaned up'

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
