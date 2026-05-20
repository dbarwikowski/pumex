---
tags: [tasks, backlog]
status: active
type: tasks
---
# Backlog

Feature ideas and future work for Pumex. See [[tasks/in-progress]] for what is actively being worked on.

## High priority

- [ ] `pumex note move` — rename or move a note and update all wikilinks that reference it
- [ ] `pumex graph` — output the backlink graph as JSON or DOT format for external visualisation
- [ ] Property indexing performance — benchmark `--property k=v` filter on vaults with 10k+ notes
- [ ] `pumex note list --tag X` shorthand (currently requires `pumex search --tag X`)

## Medium priority

- [ ] Shell completions — PowerShell, bash, and zsh tab completion for commands and vault names
- [ ] `pumex export` — export a vault or subset of notes to HTML or PDF
- [ ] Configurable daily note folder — currently always stored in the vault root
- [ ] FTS5 snippet quality for complex queries — snippets go empty for `AND/OR/NOT` expressions
- [ ] `pumex note list --sort modified` — sort by last-modified date

## Low priority

- [ ] Watch mode: `pumex search --watch "query"` — live-updating results as the index changes
- [ ] Vault aliases — multiple registered names for the same path
- [ ] Windows service event log integration for daemon crash diagnostics
- [ ] `pumex stats` — note count, word count, tag count, backlink density

## Links

- [[wiki/search]] — FTS5 known limitations (relevant to snippet quality backlog item)
- [[wiki/daemon]] — IPC wire format (relevant to watch mode)
- [[tasks/in-progress]] — active items pulled from this list
- [[tasks/done]] — items already shipped
