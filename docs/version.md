# pumex --version

Print the version of the CLI and the running daemon.

## Synopsis

```
pumex --version
```

## Description

Reads the CLI's own version from its assembly metadata, then asks the running daemon for its version over IPC. Prints both on separate lines and exits `0`.

If the daemon is not reachable, the CLI line is still printed and the daemon line shows `(not running)`. The IPC connect attempt is short (500 ms) so `--version` never feels slow.

When CLI and daemon versions differ (e.g., you updated the CLI but did not restart the daemon), both numbers are printed as-is — no warning, no extra line. The mismatch is visible at a glance.

## Output

```
# Both running, same version
$ pumex --version
pumex 0.1.0
pumex-daemon 0.1.0

# Daemon down
$ pumex --version
pumex 0.1.0
pumex-daemon (not running)

# Version mismatch
$ pumex --version
pumex 0.1.0
pumex-daemon 0.0.9
```

## Exit codes

Always `0`. `--version` is informational; an unreachable daemon is not an error.

## See also

- [`pumex daemon status`](daemon.md#status) — daemon running/not-running check
