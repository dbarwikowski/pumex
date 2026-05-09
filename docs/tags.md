# pumex tags

List all tags in a vault with their occurrence counts.

## Synopsis

```
pumex tags [--vault NAME | --vault-path PATH | --all]
```

## Flags

| Flag | Description |
|---|---|
| `--vault NAME` | List tags from the named vault. |
| `--vault-path PATH` | List tags from the vault at this path. |
| `--all` | List tags across all registered vaults. |

## Description

Returns a table of every `#tag` found across notes in the selected vault(s), sorted by count descending. Tags are extracted from note bodies by the parser — inline tags of the form `#tagname` anywhere in the body.

When no vault scope is given, the vault is auto-discovered from the current working directory.

## Output

```
 Tag      │ Count
 ─────────┼───────
 #work    │ 42
 #meeting │ 17
 #project │ 8
```

## Examples

```
# Tags in the vault containing the current directory
pumex tags

# Tags in a specific vault
pumex tags --vault personal

# Tags across all vaults
pumex tags --all
```

## See also

- [`pumex search`](search.md) — search notes filtered by tag with `--tag`
