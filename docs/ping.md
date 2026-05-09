# pumex ping

Check whether the daemon is running and responding.

## Synopsis

```
pumex ping
```

## Description

Sends a `ping` request to the daemon and prints `ok` on success. Exits with code `0` if the daemon responds, non-zero otherwise.

Useful in scripts to gate commands on daemon availability.

## Example

```
$ pumex ping
ok
```

## See also

- [`pumex daemon status`](daemon.md#status) — same check with a more descriptive message and explicit exit code
