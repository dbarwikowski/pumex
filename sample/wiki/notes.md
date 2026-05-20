---
tags: [wiki, notes, crud]
status: published
type: wiki
---
# Note CRUD

Create, read, append, and delete notes through the daemon. All CRUD goes through the daemon — it writes to disk, reindexes inline, then the watcher's re-fire is a cheap mtime no-op. Safe to query immediately after create.

## List notes

```powershell
pumex list --vault sample
```

## Read a note

```powershell
# By name (case-insensitive, no .md extension)
pumex read index --vault sample
pumex read daemon --vault sample
pumex read backlog --vault sample

# By relative path (use when name is ambiguous)
pumex read wiki/search --vault sample
pumex read tasks/done --vault sample

# Raw markdown — no rendering, frontmatter included
pumex read daemon --raw --vault sample
```

## Create a note

```powershell
# Inline content
pumex create wiki/draft --content "# Draft\n\nWork in progress." --vault sample

# Nested path — folders created automatically
pumex create experiments/perf-test --content "# Perf test scratch" --vault sample

# From stdin (omit --content to read from stdin)
"# From template`n`nBody here." | pumex create wiki/from-template --vault sample
```

## Append to a note

```powershell
# New paragraph (default)
pumex append index --content "- updated 2026-05-20" --vault sample

# Same line — inline flag
pumex append index --content " (pinned)" --inline --vault sample

# From stdin
"- entry from pipe" | pumex append index --vault sample
```

## Delete a note

```powershell
# Clean up the draft created above
pumex delete wiki/draft --vault sample
pumex delete wiki/from-template --vault sample
```

## Name resolution

Bare names are exact filename matches (no `.md`, case-insensitive). Ambiguous names — same filename in two different folders — require a path:

```powershell
# Ambiguous example: if tasks/index.md and wiki/index.md both existed, this errors
pumex read index --vault sample
# Error: ambiguous name. Use a path:
pumex read wiki/index --vault sample
```

→ See [[search]] to find notes by content, [[properties]] to manage frontmatter, [[daily]] for date-stamped notes.
