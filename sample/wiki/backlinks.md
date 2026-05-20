---
tags: [wiki, backlinks, graph]
status: published
type: wiki
---
# Backlinks

Backlinks show which notes link to a given note via `[[wikilinks]]`. Useful for understanding the note graph without a GUI.

## Get backlinks for a note

```powershell
# What links to the wiki index?  (should return commands, daily notes, tasks)
pumex backlinks index --vault sample

# What links to the daemon page?
pumex backlinks daemon --vault sample

# What links to the tasks/backlog note?
pumex backlinks backlog --vault sample

# What links to the search page?
pumex backlinks search --vault sample
```

## Disambiguate with a path

```powershell
# Use path when the name alone could be ambiguous
pumex backlinks wiki/index --vault sample
pumex backlinks tasks/done --vault sample
```

## Cross-vault backlinks

```powershell
pumex backlinks index --all
```

## Why backlinks matter

- Navigate the graph without opening a GUI
- Find orphaned notes — no backlinks means potential cleanup candidate
- Understand dependencies before deleting a note

```powershell
# Find all notes about "daemon", then check what links to them
pumex search "daemon" --vault sample
pumex backlinks daemon --vault sample
```

## Typical output

Running `pumex backlinks index --vault sample` should return a list of notes that contain `[[index]]` or `[[wiki/index]]`, including daily notes and task files.

→ See [[notes]] for reading linked notes, [[search]] to find notes by content, [[vaults]] for cross-vault usage.
