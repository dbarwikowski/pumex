# pumex note

Read and manage notes.

## Subcommands

- [read](#read) — display a note's content
- [create](#create) — create a new note
- [append](#append) — append content to a note
- [delete](#delete) — delete a note
- [list](#list) — list all notes in a vault

---

## read

Display a note's content.

### Synopsis

```
pumex note read <path-or-name> [--raw] [--vault NAME | --vault-path PATH]
```

### Arguments

| Argument | Description |
|---|---|
| `path-or-name` | Absolute path, relative path, or bare note name. |

### Flags

| Flag | Description |
|---|---|
| `--raw` | Print the raw file contents without any rendering or formatting. |
| `--vault NAME` | Resolve name within the named vault. |
| `--vault-path PATH` | Resolve name within the vault at this path. |

### Description

Reads and displays the note. Without `--raw`:

- Frontmatter properties are shown in a table.
- Inline tags are shown as `#tag` labels.
- The body is rendered as Markdown: headings, tables, code blocks, bold/italic, lists, blockquotes, and thematic breaks.

With `--raw`, the file is printed exactly as stored on disk, including frontmatter delimiters.

### Examples

```
# Read a note by name (vault auto-discovered from CWD)
pumex note read architecture

# Read raw file contents (useful for piping)
pumex note read architecture --raw

# Read a note by path
pumex note read ./docs/architecture.md
```

---

## create

Create a new note.

### Synopsis

```
pumex note create <path-or-name> [--content TEXT | --stdin] [--vault NAME | --vault-path PATH]
```

### Arguments

| Argument | Description |
|---|---|
| `path-or-name` | Name or path for the new note. A bare name creates `<vault-root>/<name>.md`. |

### Flags

| Flag | Description |
|---|---|
| `--content TEXT` | Initial content for the note. |
| `--stdin` | Read content from standard input. |
| `--vault NAME` | Create in the named vault. |
| `--vault-path PATH` | Create in the vault at this path. |

### Description

Creates the note on disk and reindexes it inline before returning. A subsequent `pumex search` for the note will succeed immediately without waiting for the file watcher.

Content must be provided via `--content` or `--stdin`, unless stdin is already redirected (e.g. a pipe).

### Examples

```
# Create an empty note
pumex note create ideas --content ""

# Create a note with content
pumex note create "2026-05-10" --content "# Meeting notes"

# Create from a file
cat template.md | pumex note create sprint-42 --stdin

# Create in a specific vault
pumex note create todo --content "- [ ] review PR" --vault work
```

---

## append

Append content to an existing note.

### Synopsis

```
pumex note append <path-or-name> [--content TEXT | --stdin] [--inline] [--vault NAME | --vault-path PATH]
```

### Arguments

| Argument | Description |
|---|---|
| `path-or-name` | Name or path of the note to append to. |

### Flags

| Flag | Description |
|---|---|
| `--content TEXT` | Content to append. |
| `--stdin` | Read content from standard input. |
| `--inline` | Append on the same line as existing content rather than on a new line. |
| `--vault NAME` | Resolve name within the named vault. |
| `--vault-path PATH` | Resolve name within the vault at this path. |

### Description

Appends content to the end of the note and reindexes it inline before returning. The note must already exist.

By default, content is appended with a preceding newline. With `--inline`, it is appended directly to the last line.

### Examples

```
# Append a bullet point
pumex note append todo --content "- [ ] write tests"

# Append from stdin
echo "- [ ] deploy" | pumex note append todo --stdin

# Append inline (no leading newline)
pumex note append log --content " [done]" --inline
```

---

## delete

Delete a note permanently.

### Synopsis

```
pumex note delete <path-or-name> [--vault NAME | --vault-path PATH]
```

### Arguments

| Argument | Description |
|---|---|
| `path-or-name` | Name or path of the note to delete. |

### Flags

| Flag | Description |
|---|---|
| `--vault NAME` | Resolve name within the named vault. |
| `--vault-path PATH` | Resolve name within the vault at this path. |

### Description

Deletes the note from disk and removes it from the index immediately. There is no confirmation prompt and no undo. The file watcher's later re-fire is a no-op (file no longer exists).

### Examples

```
pumex note delete draft
pumex note delete ./archive/old-note.md
```

---

## list

List all notes in a vault.

### Synopsis

```
pumex note list [--vault NAME | --vault-path PATH | --all]
```

### Flags

| Flag | Description |
|---|---|
| `--vault NAME` | List notes from the named vault. |
| `--vault-path PATH` | List notes from the vault at this path. |
| `--all` | List notes across all registered vaults. |

### Description

Displays a table of all indexed notes with their name, path, and last-modified time.

### Output

```
 Name             │ Path                          │       Modified
 ─────────────────┼───────────────────────────────┼────────────────────
 architecture     │ C:\notes\work\architecture.md │ 2026-05-09 14:22
 meeting-2026-05  │ C:\notes\work\meeting-...md   │ 2026-05-09 11:05
```

### Examples

```
pumex note list
pumex note list --vault work
pumex note list --all
```

## See also

- [`pumex search`](search.md) — search notes by content, tag, or property
- [`pumex property`](property.md) — read and write frontmatter properties
- [`pumex daily`](daily.md) — daily notes
