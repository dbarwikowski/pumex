# Distribution

## Binaries

Each GitHub release ships two self-contained single-file binaries for every
supported platform (`win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`,
`osx-arm64`):

| Binary | Purpose |
|--------|---------|
| `pumex` / `pumex.exe` | CLI — interactive terminal use, IPC client |
| `pumex-daemon` / `pumex-daemon.exe` | Background indexing service |

The install scripts place both under `~/.pumex/bin/` and add that
directory to `PATH`.

## Install

**Windows (PowerShell):**
```powershell
iwr https://raw.githubusercontent.com/dbarwikowski/pumex/master/install/install.ps1 | iex
```

**Linux / macOS:**
```sh
curl -fsSL https://raw.githubusercontent.com/dbarwikowski/pumex/master/install/install.sh | sh
```

Pin a specific version with `$env:PUMEX_VERSION = 'v0.2.0'` (PowerShell) or
`PUMEX_VERSION=v0.2.0` (sh) before running.

## Daemon registration

The installer registers `pumex-daemon` to auto-start at logon:

- **Windows** — a per-user scheduled task (`Pumex Daemon`) running at
  `LeastPrivilege`. No admin shell required.
- **Linux** — a systemd user service (`pumex.service`).
- **macOS** — a launchd user agent (`com.pumex.daemon`).

Ad-hoc lifecycle is also available without touching the service registration:

```sh
pumex daemon start    # spawn a detached daemon for this session
pumex daemon stop     # graceful shutdown via IPC
pumex daemon status   # is it running?
```

See [`pumex daemon`](../docs/daemon.md) for details.

## Data directory

Everything daemon-owned lives under `$PUMEX_HOME` (default `~/.pumex/`):

| Path | Contents |
|------|----------|
| `~/.pumex/bin/` | CLI and daemon binaries |
| `~/.pumex/index.db` | SQLite + FTS5 index |
| `~/.pumex/logs/` | Daily-rolled daemon logs, last 7 days kept |
| `~/.pumex/config.json` | Global config |

Set `PUMEX_HOME` to an alternate path to run a dev daemon alongside an
installed one — the named pipe is derived from the home path so they don't
collide.
