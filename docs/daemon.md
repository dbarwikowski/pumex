# pumex daemon

Manage the Pumex background daemon.

## Subcommands

- [status](#status) — check if the daemon is running
- [start](#start) — spawn the daemon as a detached background process
- [stop](#stop) — gracefully shut the daemon down via IPC
- [restart](#restart) — stop, then start
- [install](#install) — install as a system service (auto-start at logon)
- [uninstall](#uninstall) — uninstall the system service

---

## status

Check whether the daemon is running.

### Synopsis

```
pumex daemon status
```

### Description

Sends a ping to the daemon (2-second timeout). Prints a human-readable status message and exits with code `0` if running, `1` if not.

### Output

```
# Running
daemon is running

# Not running
daemon is not running
```

---

## start

Spawn the daemon as a detached background process. Idempotent.

### Synopsis

```
pumex daemon start [--daemon-path PATH]
```

### Flags

| Flag | Description |
|---|---|
| `--daemon-path PATH` | Path to the `pumex-daemon` binary. Defaults to `pumex-daemon[.exe]` next to the CLI binary. |

### Description

Launches `pumex-daemon` as a detached child process with working directory set to `$PUMEX_HOME` (or `~/.pumex`). On Windows the daemon is a Windows GUI subsystem app, so no console window appears. After spawning, polls the daemon's IPC ping for up to 5 seconds and only returns once the daemon is ready.

This is independent of the scheduled task / systemd unit installed by `daemon install` — it works whether or not the daemon has been registered as a service. Use it for one-off runs, dev work, or when you don't need auto-start at logon.

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Daemon started, or was already running |
| `1` | Failed to spawn, or did not respond to ping within 5 s |

### Output

```
# Spawned and responsive
daemon started

# Already running
daemon already running
```

---

## stop

Gracefully shut the daemon down via IPC.

### Synopsis

```
pumex daemon stop
```

### Description

Sends a `stop` command over the named pipe. The daemon finishes the file it is currently indexing, closes the database, and exits. The CLI then polls `ping` until the pipe stops responding (up to a 10-second deadline) and reports completion.

`stop` only targets the daemon currently listening on Pumex's named pipe — rogue `pumex-daemon` processes outside that pipe are not affected. The scheduled task / systemd unit is left untouched: the daemon will start again at the next logon if it was registered via `daemon install`.

If the daemon does not respond within the timeout (e.g., it has hung), use Task Manager (`taskkill /IM pumex-daemon.exe`) or `kill <pid>` to terminate it manually.

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Daemon stopped, or was not running |
| `1` | Daemon did not exit within 10 s |

### Output

```
# Successful graceful shutdown
daemon stopped

# Daemon wasn't running
daemon not running
```

---

## restart

Stop the running daemon and start it again.

### Synopsis

```
pumex daemon restart [--daemon-path PATH]
```

### Flags

| Flag | Description |
|---|---|
| `--daemon-path PATH` | Path to the `pumex-daemon` binary. Same default as `start`. |

### Description

Equivalent to `pumex daemon stop` followed by `pumex daemon start`. Useful after updating the daemon binary or changing on-disk state that requires a clean reload.

---

## install

Install the daemon as a system service that starts automatically at user logon.

### Synopsis

```
pumex daemon install [--daemon-path PATH]
```

### Flags

| Flag | Description |
|---|---|
| `--daemon-path PATH` | Path to the `pumex-daemon` binary. Defaults to `pumex-daemon[.exe]` next to the CLI binary. |

### Description

Registers the daemon as a platform-appropriate user-level service:

| Platform | Service type |
|---|---|
| Windows | Scheduled Task — `Pumex Daemon` |
| Linux | systemd user service — `pumex.service` |
| macOS | launchd user agent — `com.pumex.daemon` |

**Windows** — creates a scheduled task with an `AtLogOn` trigger registered to the current user, running at `LeastPrivilege` (no administrator required). Working directory is set to `$HOME/.pumex` so the daemon's logs and database live in the same place whether it was launched from the task or directly. Runs the task immediately so the daemon is up without requiring a re-login.

**Linux** — writes `~/.config/systemd/user/pumex.service` and runs `systemctl --user enable --now pumex`.

**macOS** — writes `~/Library/LaunchAgents/com.pumex.daemon.plist` and loads it via `launchctl`.

For dev installs where the daemon binary is in a build output directory, pass `--daemon-path`:

```
pumex daemon install --daemon-path ./src/Pumex.Daemon/bin/Release/net10.0/win-x64/pumex-daemon.exe
```

---

## uninstall

Uninstall the daemon system service.

### Synopsis

```
pumex daemon uninstall [--daemon-path PATH]
```

### Flags

| Flag | Description |
|---|---|
| `--daemon-path PATH` | Path to the `pumex-daemon` binary. Same default as `install`. |

### Description

Stops and removes the platform service. The binary and data directory (`~/.pumex/`) are not deleted.

**Windows** — runs `schtasks /end` then `schtasks /delete`.

**Linux** — runs `systemctl --user disable --now pumex` and deletes the unit file.

**macOS** — runs `launchctl unload` and deletes the plist.

---

## Running manually (no service)

For one-off runs or development, skip the service registration entirely and use `pumex daemon start` / `stop`:

```
pumex daemon start
pumex daemon stop
```

Or run the binary directly in the foreground:

```
pumex-daemon
```

Or during development:

```
dotnet run --project src/Pumex.Daemon
```

To run alongside an installed production daemon without conflicts, set `PUMEX_HOME`:

```powershell
$env:PUMEX_HOME = "$HOME\.pumex-dev"
dotnet run --project src/Pumex.Daemon
```

## Logs

The daemon writes a rolling daily log at `$PUMEX_HOME/logs/daemon-<yyyyMMdd>.log` (default `~/.pumex/logs/`). The last seven days are retained. Console logging continues to work when the daemon is run in a terminal (e.g. via `dotnet run`).

## See also

- [`pumex ping`](ping.md) — quick daemon health check
