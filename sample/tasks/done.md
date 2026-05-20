---
tags: [tasks, done]
status: closed
type: tasks
---
# Done

## Completed

- [x] FTS5 full-text search — `pumex search` with AND/OR/NOT boolean operators
- [x] YAML frontmatter property indexing — `pumex property list/get/set`
- [x] Backlink graph — `pumex backlinks` resolves wikilinks across the vault
- [x] Daily notes — `pumex daily read/append` with `--date` override
- [x] Note CRUD — `pumex note create/read/append/delete` going through daemon
- [x] Daemon IPC over named pipe — 4-byte length prefix + UTF-8 JSON
- [x] FileSystemWatcher per vault for live re-indexing on disk changes
- [x] `InvariantGlobalization=false` — Polish and other non-ASCII characters in tags and note names
- [x] Fix frontmatter rendering — merged PR #37 (2026-05-13)
- [x] System.CommandLine 2.0.8 migration — merged PR #39 (2026-05-14)
- [x] Property filter on `pumex search` — `--property k=v` filter (2026-05-19)
- [x] Sample vault with runnable examples for every command (2026-05-20)

## References

- [[wiki/commands]] — current command surface
- [[wiki/daemon]] — IPC wire format and FTS5 constraints
- [[tasks/in-progress]] — what is next
- [[tasks/backlog]] — ideas not yet started
