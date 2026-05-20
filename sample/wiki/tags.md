---
tags: [wiki, tags]
status: published
type: wiki
---
# Tags

Tags live in YAML frontmatter under the `tags` key and are indexed by the daemon at write time.

## List tags in a vault

```powershell
pumex tags --vault sample
```

Output shows each tag name with its occurrence count across all notes in the vault.

Expected output (approximate):

```
Tag           Count
backlinks     1
commands      1
crud          1
daily         7
daemon        1
done          1
frontmatter   1
fts5          1
graph         1
...
wiki          9
```

## Tags across all registered vaults

```powershell
pumex tags --all
```

## Filter notes by tag

Tags are a filter on `pumex search`:

```powershell
# All notes tagged "wiki"
pumex search --tag wiki --vault sample

# Notes tagged "tasks" mentioning "backlog"
pumex search "backlog" --tag tasks --vault sample

# Multiple tags — AND semantics
pumex search --tag wiki --tag reference --vault sample

# Tag + property filter combined
pumex search --tag wiki --property status=published --vault sample
```

## Add or update tags via `pumex prop`

Tags are a YAML list — set the full array to update:

```powershell
# Add tags to a note (replaces the existing list)
pumex prop wiki/draft tags "[wiki, draft, wip]" --vault sample

# Verify
pumex prop wiki/draft tags --vault sample
```

→ See [[properties]] for full frontmatter control, [[search]] for combining tag and property filters.
