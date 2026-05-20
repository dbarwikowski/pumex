# Pumex

Pumex is a headless knowledge engine for plain Markdown vaults. It runs as a background daemon and exposes a CLI for reading, searching, and writing notes — no GUI, no Electron, just fast file-backed knowledge.

**Obsidian without Electron. Agent-friendly. Composable like a Unix tool.**

## Quick start

```sh
# Install and start the daemon
pumex daemon install

# Create a vault
pumex new work ~/notes/work

# Write a note
pumex create standup --content "## 2026-05-20\n- shipped v1.2"

# Search it
pumex search standup

# Read it back
pumex read standup
```

## Command reference

| Command | Description |
|---|---|
| [`pumex ping`](ping.md) | Check whether the daemon is running |
| [`pumex new`](new.md) | Create and register a new vault |
| [`pumex search`](search.md) | Full-text + tag/property search |
| [`pumex tags`](tags.md) | List tags and their counts |
| [`pumex backlinks`](backlinks.md) | List notes that link to a given note |
| [`pumex vault list`](vault.md#list) | List registered vaults |
| [`pumex vault add`](vault.md#add) | Register an existing directory as a vault |
| [`pumex vault remove`](vault.md#remove) | Unregister a vault |
| [`pumex read`](note.md#read) | Read a note, rendered or raw |
| [`pumex create`](note.md#create) | Create a new note |
| [`pumex append`](note.md#append) | Append content to an existing note |
| [`pumex delete`](note.md#delete) | Delete a note |
| [`pumex list`](note.md#list) | List all notes in a vault |
| [`pumex prop`](property.md) | List, get, or set frontmatter properties |
| [`pumex daily`](daily.md) | Read today's daily note |
| [`pumex daily append`](daily.md#append) | Append to a daily note |
| [`pumex daemon`](daemon.md) | Manage the background daemon |

## Concepts

### Vault

A vault is a directory of Markdown files registered with the daemon. Registration tells the daemon to index its contents and watch for changes. Each vault has a name used in `--vault NAME` flags.

```sh
pumex new personal ~/notes/personal   # create + register
pumex vault add work ~/notes/work      # register existing directory
pumex vault list                       # show all registered vaults
```

### Vault scope

Most commands accept scope flags to control which vault is targeted:

| Flag | Behaviour |
|---|---|
| *(none)* | Auto-discover by walking up from the current directory |
| `--vault NAME` | Use the named vault |
| `--vault-path PATH` | Use the vault at this path |
| `--all` | Run across every registered vault |

### Daily notes

The daemon tracks a daily note per vault (path determined by `dailyFolder` / `dailyFormat` in the vault config, defaulting to `daily/yyyy-MM-dd.md`). Use `pumex daily` to read today's note and `pumex daily append` to log entries to it.

### Frontmatter properties

YAML frontmatter between `---` delimiters is parsed and indexed. Use `pumex prop` to read or write individual keys without editing the file manually.

## Architecture

```
pumex (CLI)  ──named pipe──►  pumex-daemon (background)
                                    │
                               SQLite FTS5 index
                                    │
                              Markdown vault(s)
```

The CLI is a thin client — it forwards every command over a named pipe and renders the response. All indexing, search, and file I/O happen in the daemon. See [`pumex daemon`](daemon.md) for service management.

## Environment variables

| Variable | Description |
|---|---|
| `PUMEX_HOME` | Override the default data directory (`~/.pumex`). Also changes the named pipe, so a dev daemon runs fully isolated from a production install. |
