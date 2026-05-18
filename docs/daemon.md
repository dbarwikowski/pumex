# pumex daemon

Manage the Pumex background daemon.

## Subcommands

- [status](#status) — check if the daemon is running
- [install](#install) — install as a system service
- [uninstall](#uninstall) — uninstall the system service
- [restart](#restart) — restart the system service

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

## install

Install the daemon as a system service that starts automatically on login.

### Synopsis

```
pumex daemon install [--daemon-path PATH]
```

### Flags

| Flag | Description |
|---|---|
| `--daemon-path PATH` | Path to the `pumex-daemon` binary. Defaults to `pumex-daemon[.exe]` in the same directory as the CLI binary. |

### Description

Installs and starts the daemon as a platform-appropriate background service:

| Platform | Service type | Requires |
|---|---|---|
| Windows | Scheduled Task (`schtasks`) | Administrator |
| Linux | systemd user service | — |
| macOS | launchd user agent | — |

**Windows** — requires an elevated shell. Creates a scheduled task named "Pumex Daemon" with an ONLOGON trigger and runs it immediately.

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

**Windows** — requires an elevated shell.

**Linux** — runs `systemctl --user disable --now pumex` and deletes the unit file.

**macOS** — runs `launchctl unload` and deletes the plist.

---

## restart

Restart the daemon system service.

### Synopsis

```
pumex daemon restart [--daemon-path PATH]
```

### Flags

| Flag | Description |
|---|---|
| `--daemon-path PATH` | Path to the `pumex-daemon` binary. Same default as `install`. |

### Description

Stops then starts the platform service. Useful after updating the daemon binary.

**Windows** — requires an elevated shell.

---

## Running manually (no service)

The daemon can also be run directly as a foreground process:

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

## See also

- [`pumex ping`](ping.md) — quick daemon health check
