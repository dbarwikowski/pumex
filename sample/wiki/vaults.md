---
tags: [wiki, vaults, management]
status: published
type: wiki
---
# Vault Management

A vault is a registered directory of Markdown notes. The daemon indexes all `.md` files within it, maintains an FTS5 index, and tracks wikilink backlinks.

## List registered vaults

```powershell
pumex vault list
```

Should show `sample` (this vault) along with any others you have registered.

## Add an existing directory as a vault

```powershell
pumex vault add sample C:\Repos\Pumex\sample
```

## Create and register a new vault

```powershell
# Creates the directory and registers it in one step
pumex new myvault C:\Notes\myvault
```

## Remove a vault

Unregisters — does not delete files on disk:

```powershell
pumex vault remove sample
```

## Vault resolution in commands

Most commands auto-detect the vault from the current working directory. Override with flags:

```powershell
# By registered name
pumex list --vault sample
pumex search "daemon" --vault sample

# By absolute path (no registration needed)
pumex list --vault-path C:\Repos\Pumex\sample

# All registered vaults at once
pumex list --all
pumex tags --all
pumex search "daemon" --all
```

## Isolate a dev daemon from production

Use `PUMEX_HOME` to point the CLI and daemon at a separate index and pipe:

```powershell
# Terminal A — dev daemon (separate index from production)
$env:PUMEX_HOME = "$HOME\.pumex-dev"
dotnet run --project src/Pumex.Daemon

# Terminal B — CLI pointing at the dev daemon
$env:PUMEX_HOME = "$HOME\.pumex-dev"
pumex vault list
pumex vault add sample C:\Repos\Pumex\sample
pumex search "daemon" --vault sample
```

→ See [[daemon]] for daemon lifecycle, [[notes]] for working with notes inside a vault.
