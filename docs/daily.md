# pumex daily

Read and write daily notes.

## Subcommands

- [read](#read) — read a daily note (default when no subcommand given)
- [append](#append) — append content to a daily note

---

## read

Read a daily note.

### Synopsis

```
pumex daily [read] [--date YYYY-MM-DD] [--vault NAME | --vault-path PATH]
```

`pumex daily` (with no subcommand) is shorthand for `pumex daily read`.

### Flags

| Flag | Description |
|---|---|
| `--date YYYY-MM-DD` | Read the daily note for this date. Defaults to today. |
| `--vault NAME` | Read from the named vault. |
| `--vault-path PATH` | Read from the vault at this path. |

### Description

Reads the daily note for the given date (or today). The file path and the note body are printed. If the note doesn't exist yet, an error is returned.

Daily note path is determined by the vault's `dailyFolder` and `dailyFormat` config fields (defaults: `daily/` folder, `yyyy-MM-dd.md` filename).

### Examples

```
# Read today's daily note
pumex daily

# Read a specific date
pumex daily --date 2026-05-01

# From a specific vault
pumex daily --vault work
```

---

## append

Append content to a daily note.

### Synopsis

```
pumex daily append [--content TEXT | --stdin] [--inline] [--date YYYY-MM-DD]
                   [--vault NAME | --vault-path PATH]
```

### Flags

| Flag | Description |
|---|---|
| `--content TEXT` | Content to append. |
| `--stdin` | Read content from standard input. |
| `--inline` | Append on the same line as existing content rather than on a new line. |
| `--date YYYY-MM-DD` | Append to the daily note for this date. Defaults to today. |
| `--vault NAME` | Append to a note in the named vault. |
| `--vault-path PATH` | Append to a note in the vault at this path. |

### Description

Appends content to the daily note for the given date. If the note doesn't exist yet it is created. The note is reindexed inline before returning.

Content must be provided via `--content` or `--stdin`, unless stdin is already redirected.

### Examples

```
# Log a quick thought to today's note
pumex daily append --content "- idea: rewrite indexer with LMDB"

# Append from stdin
echo "- [ ] follow up on PR #42" | pumex daily append --stdin

# Append to a past date
pumex daily append --content "retrospective: shipped v0.1" --date 2026-04-30
```

## See also

- [`pumex note read`](note.md#read) — read any note
- [`pumex note append`](note.md#append) — append to any note
