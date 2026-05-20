---
tags: [tasks, in-progress]
status: active
type: tasks
---
# In Progress

## Active work items

### Property filter queries (dbarwikowski)

Implementing `--property k=v` filter on `pumex search`. Requires a schema migration to add a `properties` virtual table. Property queries are fast since the table is pre-indexed — no FTS overhead.

References: [[wiki/properties]], [[wiki/search]]

### Sample vault (dbarwikowski)

Creating a reference vault that demonstrates every CLI command with runnable examples. This vault. Each wiki page targets notes that actually exist here, so examples can be copy-pasted and run.

References: [[wiki/index]], [[wiki/commands]]

## Blocked

- Shell completions — waiting on System.CommandLine 2.0 migration to settle before adding completion providers (migration shipped in PR #39, testing in progress)

## Up next (from backlog)

- `pumex note move` — highest-value unshipped feature; requires backlink rewrite on rename
- `pumex graph` — depends on backlink table being stable

References: [[tasks/backlog]], [[tasks/done]]
