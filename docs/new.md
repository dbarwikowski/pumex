# pumex new

Create a new vault and register it with the daemon.

## Synopsis

```
pumex new <name> [path]
```

## Arguments

| Argument | Description |
|---|---|
| `name` | Name for the vault (used in `--vault NAME` flags) |
| `path` | Directory to initialize. Defaults to the current working directory. Created if it doesn't exist. |

## Description

1. Creates the target directory if it doesn't exist.
2. Writes a `.pumex/config.json` vault marker inside it.
3. Asks the daemon to register the vault so indexing begins immediately.

If the vault marker already exists, the init step is skipped and only registration is attempted.

If the daemon is not running, the marker is written and you are told to register manually once the daemon starts:

```
pumex vault add <name> <path>
```

## Examples

```
# Initialize current directory as a vault named "work"
pumex new work

# Create and initialize a new directory
pumex new personal ~/notes/personal
```

## See also

- [`pumex vault add`](vault.md#add) — register an existing directory without writing a marker
- [`pumex vaults`](vaults.md) — list registered vaults
