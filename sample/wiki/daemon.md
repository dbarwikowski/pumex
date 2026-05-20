---
tags: [wiki, daemon, lifecycle]
status: published
type: wiki
---
# Daemon

The Pumex daemon is a background process that indexes vaults, maintains the FTS5 and backlink databases, and serves IPC requests from the CLI over a named pipe.

## Check daemon is alive

```powershell
pumex ping
# → Pong  (daemon is up)

pumex daemon status
```

## Install as a system service

Requires Administrator on Windows. Registers the daemon to start automatically:

```powershell
pumex daemon install

# Point at a custom binary
pumex daemon install --daemon-path C:\Tools\pumex-daemon.exe
```

## Uninstall the service

```powershell
pumex daemon uninstall
```

## Restart

```powershell
pumex daemon restart
```

## Run the dev daemon (isolated from production)

```powershell
# Set PUMEX_HOME so the dev daemon uses a separate pipe and index
$env:PUMEX_HOME = "$HOME\.pumex-dev"
dotnet run --project src/Pumex.Daemon
```

The CLI auto-detects `PUMEX_HOME` and connects to the right pipe. See [[vaults]] for the full dev isolation workflow.

## IPC wire format

The CLI communicates with the daemon over a named pipe:

- 4-byte little-endian length prefix
- UTF-8 JSON payload
- Maximum message size: 10 MB

Changing the wire format requires updating all clients in lockstep.

## FileSystemWatcher

The daemon registers a `FileSystemWatcher` per vault. File changes on disk trigger re-indexing. Note CRUD through the CLI goes through the daemon — the watcher's re-fire for those changes is a cheap mtime no-op. It is safe to query immediately after a create or append.

## FTS5: no UPDATE

The daemon's `IndexDb` deletes then re-inserts for every note change. SQLite FTS5 does not support UPDATE — this is intentional and must not be "optimised" away.

→ See [[vaults]] for vault registration, [[search]] for querying the index, [[notes]] for note CRUD.
