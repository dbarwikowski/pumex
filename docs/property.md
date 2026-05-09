# pumex property

Read and write frontmatter properties on a note.

## Subcommands

- [list](#list) — list all properties
- [get](#get) — get a single property value
- [set](#set) — set a property value

---

## list

List all frontmatter properties of a note.

### Synopsis

```
pumex property list <path-or-name> [--vault NAME | --vault-path PATH]
```

### Arguments

| Argument | Description |
|---|---|
| `path-or-name` | Absolute path, relative path, or bare note name. |

### Flags

| Flag | Description |
|---|---|
| `--vault NAME` | Resolve name within the named vault. |
| `--vault-path PATH` | Resolve name within the vault at this path. |

### Description

Displays all YAML frontmatter properties for the note in a key/value table. Properties are the key-value pairs between the `---` delimiters at the top of a Markdown file.

### Output

```
 Property │ Value
 ─────────┼────────────
 status   │ active
 priority │ high
 due      │ 2026-06-01
```

### Examples

```
pumex property list architecture
pumex property list ./docs/architecture.md --vault work
```

---

## get

Get the value of a single frontmatter property.

### Synopsis

```
pumex property get <path-or-name> <key> [--vault NAME | --vault-path PATH]
```

### Arguments

| Argument | Description |
|---|---|
| `path-or-name` | Absolute path, relative path, or bare note name. |
| `key` | The property key to retrieve. |

### Flags

| Flag | Description |
|---|---|
| `--vault NAME` | Resolve name within the named vault. |
| `--vault-path PATH` | Resolve name within the vault at this path. |

### Description

Prints the value of a single property to stdout. Output is a plain string with no extra formatting — suitable for scripting.

Returns an error if the property does not exist on the note.

### Examples

```
pumex property get architecture status
# → active

# Use in a script
STATUS=$(pumex property get architecture status)
echo "Status is $STATUS"
```

---

## set

Set a frontmatter property value.

### Synopsis

```
pumex property set <path-or-name> <key> <value> [--vault NAME | --vault-path PATH]
```

### Arguments

| Argument | Description |
|---|---|
| `path-or-name` | Absolute path, relative path, or bare note name. |
| `key` | The property key to set. |
| `value` | The new value. |

### Flags

| Flag | Description |
|---|---|
| `--vault NAME` | Resolve name within the named vault. |
| `--vault-path PATH` | Resolve name within the vault at this path. |

### Description

Writes the property to the note's YAML frontmatter and reindexes the note inline before returning. If the property already exists its value is replaced; if it doesn't exist it is added.

If the note has no frontmatter block, one is created.

### Examples

```
pumex property set architecture status done
pumex property set sprint-42 due 2026-06-01
```

## See also

- [`pumex note read`](note.md#read) — read a note including its properties
- [`pumex search`](search.md) — filter search results by property with `--property k=v`
