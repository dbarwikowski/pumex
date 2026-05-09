# Pumex CLI reference

Pumex is a headless knowledge engine for plain Markdown vaults. All commands communicate with the background daemon over a named pipe.

## Commands

| Command | Description |
|---|---|
| [`pumex ping`](ping.md) | Check whether the daemon is running |
| [`pumex new`](new.md) | Create and register a new vault |
| [`pumex search`](search.md) | Full-text + tag/property search |
| [`pumex tags`](tags.md) | List tags and their counts |
| [`pumex backlinks`](backlinks.md) | List notes that link to a given note |
| [`pumex vaults`](vaults.md) | List registered vaults |
| [`pumex vault add`](vault.md#add) | Register an existing directory as a vault |
| [`pumex vault remove`](vault.md#remove) | Unregister a vault (files untouched) |
| [`pumex note read`](note.md#read) | Read a note, rendered or raw |
| [`pumex note create`](note.md#create) | Create a new note |
| [`pumex note append`](note.md#append) | Append content to an existing note |
| [`pumex note delete`](note.md#delete) | Delete a note |
| [`pumex note list`](note.md#list) | List all notes in a vault |
| [`pumex property list`](property.md#list) | List frontmatter properties of a note |
| [`pumex property get`](property.md#get) | Get a single property value |
| [`pumex property set`](property.md#set) | Set a property value |
| [`pumex daily`](daily.md) | Read today's daily note |
| [`pumex daily append`](daily.md#append) | Append to a daily note |
| [`pumex daemon status`](daemon.md#status) | Check daemon status |
| [`pumex daemon install`](daemon.md#install) | Install daemon as a system service |
| [`pumex daemon uninstall`](daemon.md#uninstall) | Uninstall the system service |
| [`pumex daemon restart`](daemon.md#restart) | Restart the system service |

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
