# Tasks & checkboxes

Pumex has two complementary task features:

1. **[Note checkboxes](#checkboxes)** — read and toggle GFM `- [ ]` items that
   live inside any Markdown note.
2. **[Task notes](#task-notes)** — a note that *is* a task, stored under
   `<vault>/tasks/` with `status` and timestamp frontmatter.

They're independent — use either or both.

---

## Checkboxes

Read and tick off GFM task-list items (`- [ ]` / `- [x]`) inside any note.
Only real task-list items count; bracket text inside fenced code blocks is
ignored.

### read --tasks

List a note's checkbox items.

```sh
pumex read <note> --tasks [--pending] [--vault NAME | --vault-path PATH]
```

| Flag | Description |
|---|---|
| `--tasks` | List checkbox items instead of rendering the note. |
| `--pending` | Show only unchecked items. Numbering is **not** affected. |

Each item is printed with a **stable index** — its position among *all*
checkboxes in document order. `--pending` only filters what's displayed, so an
item keeps the same number with or without it. Those numbers are what `check`
expects.

```text
$ pumex read checklist --tasks
  1 [ ] first smoke item
  2 [x] second smoke item
  3 [ ] third smoke item
```

### check

Toggle a checkbox.

```sh
pumex check <note> <n> [--vault NAME | --vault-path PATH]
```

`<n>` is the index shown by `read --tasks`. `check` flips the item's state
(`[ ]` ↔ `[x]`) and writes it back — run it again to undo. An out-of-range
number errors.

```sh
pumex check checklist 1   # tick item 1
pumex check checklist 1   # untick it again
```

---

## Task notes

A task note is an ordinary indexed Markdown note that happens to live under
`<vault>/tasks/` and carry task frontmatter. Because it's a normal note,
`search`, `prop`, and `backlinks` all see it; `type: TASK` is just a property.

Each task is its own folder so it can hold attachments:

```text
tasks/
└── task_2026-06-09_00/
    ├── write_report.md
    └── diagram.png        # added via `task attach`
```

The folder is named `task_<creation-date>_<NN>`, where `NN` is a per-day counter
(`_00`, `_01`, …). Fresh frontmatter:

```yaml
created:   2026-06-09
updated:   2026-06-09
completed:                # empty until the task is DONE
status:    NEW
type:      TASK
name:      write_report
```

Task names allow `[A-Za-z0-9_-]`; spaces become `_` and other characters are
rejected.

### task create

```sh
pumex task create <name> [--content TEXT] [--vault NAME | --vault-path PATH]
```

Scaffolds `tasks/task_<today>_NN/<name>.md`. `--content` fills the note body.

### task read

```sh
pumex task read <name> [--raw] [--limit N] [--vault NAME | --vault-path PATH]
```

Renders a task note (resolved by name among task notes, or by path).

### task list

```sh
pumex task list [--status X,Y]... [--open] [--vault NAME | --vault-path PATH]
```

Lists task notes **newest-first** with columns **Name · Status · Created ·
Completed**.

| Flag | Description |
|---|---|
| `--status X,Y` | Show only these statuses (repeatable, comma-separated). |
| `--open` | Show only tasks whose status is not `DONE`. |

### task status

```sh
pumex task status <name> [new-status] [--vault NAME | --vault-path PATH]
```

With no value, prints the current status. With a value, sets it. Status is
free-form but restricted to `[A-Za-z0-9_-]` (no spaces). Setting a status:

- bumps `updated` to today;
- setting `DONE` stamps `completed` with today's date;
- setting anything else clears `completed`.

```sh
pumex task status write_report          # → NEW
pumex task status write_report BLOCKED
pumex task status write_report DONE      # stamps completed
```

### task attach

```sh
pumex task attach <name> <file> [--vault NAME | --vault-path PATH]
```

**Moves** `<file>` into the task's folder (the source is removed) and appends a
`[[file.ext]]` link to the task note. Errors if a file of that name already
exists in the folder. Attachments can be any file type; binary files simply sit
in the folder un-indexed.

## See also

- [`pumex read`](note.md#read) — read any note
- [`pumex prop`](property.md) — task status is just a frontmatter property
- [`pumex search`](search.md) — task notes are searchable like any note
