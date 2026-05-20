---
tags: [wiki, search, fts5]
status: published
type: wiki
---
# Search

Pumex uses SQLite FTS5 for full-text search. Queries support `AND`, `OR`, `NOT`, phrase matching, and field targeting.

## Basic search

```powershell
# Notes mentioning "daemon"
pumex search "daemon" --vault sample

# Boolean: watcher OR indexer
pumex search "watcher OR indexer" --vault sample

# Exclude a term
pumex search "daemon NOT watcher" --vault sample

# Phrase match (single quotes around the outer command, double inside)
pumex search '"named pipe"' --vault sample

# Limit results
pumex search "pumex" --limit 3 --vault sample
```

## Tag filter

```powershell
# All notes tagged "wiki"
pumex search --tag wiki --vault sample

# Notes tagged "tasks" containing "backlog"
pumex search "backlog" --tag tasks --vault sample

# Multiple tags — AND semantics
pumex search --tag wiki --tag reference --vault sample
```

## Property filter

```powershell
# Notes where status = published
pumex search --property status=published --vault sample

# Published wiki notes — combine tag and property
pumex search --tag wiki --property status=published --vault sample

# Notes of type "tasks"
pumex search --property type=tasks --vault sample
```

## Cross-vault search

```powershell
pumex search "daemon" --all
```

## FTS5 field targeting

```powershell
# Search only in note titles
pumex search "title:daemon" --vault sample
```

## Known limitation

FTS5 snippets may be empty for complex boolean queries — this is a SQLite FTS5 behaviour, not a bug. Use bare keywords for best snippet quality.

→ See [[notes]] for reading individual results, [[tags]] for tag listing, [[properties]] for filtering by frontmatter.
