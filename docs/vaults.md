# pumex vaults

List all vaults registered with the daemon.

## Synopsis

```
pumex vaults
```

## Description

Displays a table of every vault the daemon is currently tracking, showing its name and root directory path.

## Output

```
 Name     │ Path
 ─────────┼──────────────────────
 work     │ C:\notes\work
 personal │ C:\notes\personal
```

## Examples

```
pumex vaults
```

## See also

- [`pumex new`](new.md) — create and register a new vault
- [`pumex vault add`](vault.md#add) — register an existing directory
- [`pumex vault remove`](vault.md#remove) — unregister a vault
