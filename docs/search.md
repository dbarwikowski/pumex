# pumex search

Full-text search across notes, with optional tag and property filters.

## Synopsis

```
pumex search [<query>] [--tag TAG]... [--property k=v]... [--format EXT]...
             [--limit N] [--vault NAME | --vault-path PATH | --all]
```

## Arguments

| Argument | Description |
|---|---|
| `query` | FTS5 full-text query. Optional when `--tag` or `--property` filters are given. |

## Flags

| Flag | Description |
|---|---|
| `--tag TAG` | Filter to notes that have this tag. Repeatable; comma-separated values accepted. |
| `--property k=v` | Filter to notes where frontmatter key `k` equals value `v`. Repeatable. Also accepts two tokens: `--property k v`. |
| `--format EXT` / `--ext EXT` | Filter to notes of a file format/extension (e.g. `md`, `csv`). Repeatable; comma-separated accepted. See [Text formats](formats.md). |
| `--limit N` | Maximum number of results to return. |
| `--vault NAME` | Search within the named vault. |
| `--vault-path PATH` | Search within the vault at this path. |
| `--all` | Search across all registered vaults. |

## Description

Performs a SQLite FTS5 full-text search. Results are displayed in a table with note name, path, and a snippet showing the matched context.

The query supports standard FTS5 syntax:
- Bare terms: `project meeting`
- Phrase: `"project meeting"`
- Boolean: `project AND meeting`, `project OR meeting`, `project NOT meeting`
- Prefix wildcard: `proj*`
- Column qualifier: `title:meeting`

At least one of `query`, `--tag`, `--property`, or `--format` must be provided.

When no vault scope is given, the vault is auto-discovered from the current working directory.

## Examples

```
# Full-text search
pumex search "meeting notes"

# Filter by tag only
pumex search --tag work

# Combine query with filters
pumex search project --tag work --property status=active

# --property also accepts two separate tokens
pumex search project --property status active

# Search across all vaults
pumex search kubernetes --all

# Limit results
pumex search rust --limit 5

# Restrict to a file format (requires the format to be enabled for the vault)
pumex search capybara --format csv
pumex search --format csv,json
```

## See also

- [`pumex tags`](tags.md) — list all tags in a vault
- [`pumex backlinks`](backlinks.md) — find notes that link to a given note
