# Pumex CLI reference

Pumex is a headless knowledge engine for plain Markdown vaults. All commands communicate with the background daemon over a named pipe.

## Commands

| Command | Description |
|---|---|
| [`pumex --version`](version.md) | Print CLI and daemon versions |
| [`pumex ping`](ping.md) | Check whether the daemon is running |
| [`pumex new`](new.md) | Create and register a new vault |
| [`pumex search`](search.md) | Full-text + tag/property search |
| [`pumex tags`](tags.md) | List tags and their counts |
| [`pumex backlinks`](backlinks.md) | List notes that link to a given note |
| [`pumex vault list`](vault.md#list) | List registered vaults |
| [`pumex vault add`](vault.md#add) | Register an existing directory as a vault |
| [`pumex vault remove`](vault.md#remove) | Unregister a vault (files untouched) |
| [`pumex read`](note.md#read) | Read a note, rendered or raw |
| [`pumex read --tasks`](tasks.md#checkboxes) | List a note's checkbox items |
| [`pumex check`](tasks.md#check) | Toggle a checkbox in a note |
| [`pumex create`](note.md#create) | Create a new note |
| [`pumex append`](note.md#append) | Append content to an existing note |
| [`pumex delete`](note.md#delete) | Delete a note |
| [`pumex list`](note.md#list) | List all notes in a vault |
| [`pumex prop`](property.md) | List, get, or set frontmatter properties |
| [`pumex task`](tasks.md#task-notes) | Create and manage task notes under `tasks/` |
| [`pumex daily`](daily.md) | Read today's daily note |
| [`pumex daily append`](daily.md#append) | Append to a daily note |
| [`pumex daemon status`](daemon.md#status) | Check daemon status |
| [`pumex daemon start`](daemon.md#start) | Spawn the daemon as a detached process |
| [`pumex daemon stop`](daemon.md#stop) | Gracefully stop the daemon via IPC |
| [`pumex daemon restart`](daemon.md#restart) | Stop then start the daemon |
| [`pumex daemon install`](daemon.md#install) | Register daemon to auto-start at logon |
| [`pumex daemon uninstall`](daemon.md#uninstall) | Remove the auto-start registration |

## Vault scope flags

Most commands accept these flags to control which vault is used:

| Flag | Description |
|---|---|
| `--vault NAME` | Select vault by registered name |
| `--vault-path PATH` | Select vault by directory path |
| `--all` | Run across all registered vaults |

When no scope flag is given, the vault is auto-discovered by walking up from the current working directory until a `.pumex/` marker directory is found.

## Environment variables

| Variable | Description |
|---|---|
| `PUMEX_HOME` | Override the default data directory (`~/.pumex`). Also changes the named pipe, so a dev daemon runs fully isolated alongside a production install. |

## Guides

- [Text formats](formats.md) — index CSV/JSON/YAML and more alongside Markdown; vault `config.json`, ignore globs, linking to non-Markdown files.
- [Distribution](distribution.md) — binaries, install scripts, dev isolation, releases.
