# pumex backlinks

List all notes that contain a `[[wikilink]]` pointing to a given note.

## Synopsis

```
pumex backlinks <path-or-name> [--vault NAME | --vault-path PATH | --all]
```

## Arguments

| Argument | Description |
|---|---|
| `path-or-name` | The target note. Can be an absolute path, a relative path, or a bare name (e.g. `meeting-notes`). |

## Flags

| Flag | Description |
|---|---|
| `--vault NAME` | Resolve name and search within the named vault. |
| `--vault-path PATH` | Resolve name and search within the vault at this path. |
| `--all` | Search for backlinks across all registered vaults. |

## Description

Returns a list of file paths for every note that has a `[[wikilink]]` whose resolved target is the given note. Links are resolved by the daemon at index time using the `WikilinkResolver`.

A bare name is matched against the note's filename (without `.md`) in a case-insensitive exact match. If the name is ambiguous (same filename in multiple directories), use a full or relative path instead.

When no vault scope is given, the vault is auto-discovered from the current working directory.

## Output

One absolute path per line:

```
C:\notes\meeting-2026-01-10.md
C:\notes\projects\alpha.md
```

## Examples

```
# Backlinks to a note by name
pumex backlinks architecture

# Backlinks to a note by path
pumex backlinks ./docs/architecture.md

# Across all vaults
pumex backlinks architecture --all
```

## See also

- [`pumex search`](search.md) — full-text search
- [`pumex note read`](note.md#read) — read a note's content
