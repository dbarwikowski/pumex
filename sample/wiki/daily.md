---
tags: [wiki, daily, workflow]
status: published
type: wiki
---
# Daily Notes

Daily notes are date-stamped Markdown files. The daemon auto-creates them on first access if they don't exist yet. This vault has daily notes from 2026-05-15 through 2026-05-20.

## Read today's daily note

```powershell
pumex daily --vault sample
```

## Read a past daily note

```powershell
pumex daily --date 2026-05-19 --vault sample
pumex daily --date 2026-05-15 --vault sample
pumex daily --date 2026-05-17 --vault sample
```

## Append to today's daily

```powershell
# New paragraph (default)
pumex daily append --content "- reviewed backlinks graph" --vault sample

# Inline — appended on the same line as the last content
pumex daily append --content " ✓" --inline --vault sample
```

## Append to a past date

```powershell
pumex daily append --content "- retroactive: fixed typo in daemon docs" --date 2026-05-18 --vault sample
```

## Read a daily note by name

Daily notes are stored as `YYYY-MM-DD.md`. Read by name like any other note:

```powershell
pumex read 2026-05-20 --vault sample
pumex read 2026-05-15 --vault sample
```

## Search across daily notes

```powershell
# All daily notes (tagged "daily")
pumex search --tag daily --vault sample

# Daily notes mentioning a specific topic
pumex search "property" --tag daily --vault sample
pumex search "migration" --tag daily --vault sample
```

→ See [[notes]] for general note operations, [[search]] for querying across daily notes.
