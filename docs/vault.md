# pumex vault

Manage vault registrations.

## Subcommands

- [list](#list) — list registered vaults
- [add](#add) — register a directory as a vault
- [remove](#remove) — unregister a vault

---

## list

List all vaults registered with the daemon.

### Synopsis

```
pumex vault list
```

### Description

Displays a table of every vault the daemon is currently tracking, showing its name and root directory path.

### Output

```
 Name     │ Path
 ─────────┼──────────────────────
 work     │ C:\notes\work
 personal │ C:\notes\personal
```

### Examples

```
pumex vault list
```

---

## add

Register an existing directory with the daemon so it begins indexing.

### Synopsis

```
pumex vault add <name> <path>
```

### Arguments

| Argument | Description |
|---|---|
| `name` | Name to register the vault under. Used in `--vault NAME` flags. |
| `path` | Absolute or relative path to the vault's root directory. Must already exist. |

### Description

Tells the running daemon to start indexing the given directory. Indexing begins immediately — a full scan runs in the background and the watcher starts monitoring for changes.

Unlike `pumex new`, this command does not write a `.pumex/config.json` marker. Use `pumex new` when creating a vault from scratch; use `pumex vault add` to register a directory that already exists (or was created on another machine).

### Examples

```
pumex vault add work C:\notes\work
pumex vault add personal ~/notes/personal
```

---

## remove

Unregister a vault. Notes on disk are not touched.

### Synopsis

```
pumex vault remove <name>
```

### Arguments

| Argument | Description |
|---|---|
| `name` | Name of the vault to remove. |

### Description

Stops the daemon from indexing the vault and removes it from the registry. The directory and all its Markdown files are left untouched. The index rows for this vault are deleted.

To re-add the vault later, use `pumex vault add` again.

### Examples

```
pumex vault remove work
```

## See also

- [`pumex new`](new.md) — create and register a new vault
