# pumex prop

List, get, or set frontmatter properties on a note. The subcommand is determined by how many arguments are provided.

## Synopsis

```
pumex prop <note> [<key> [<value>]] [--vault NAME | --vault-path PATH]
```

## Arguments

| Argument | Description |
|---|---|
| `note` | Absolute path, relative path, or bare note name. |
| `key` | Property key. When omitted, all properties are listed. |
| `value` | New property value. When provided alongside `key`, sets the property. |

## Flags

| Flag | Description |
|---|---|
| `--vault NAME` | Resolve name within the named vault. |
| `--vault-path PATH` | Resolve name within the vault at this path. |

## Modes

### List all properties

Omit both `key` and `value`:

```
pumex prop <note>
```

Displays all YAML frontmatter properties in a key/value table. Properties are the key-value pairs between the `---` delimiters at the top of a Markdown file.

**Output:**

```
 Property │ Value
 ─────────┼────────────
 status   │ active
 priority │ high
 due      │ 2026-06-01
```

### Get a single property

Provide `key` but not `value`:

```
pumex prop <note> <key>
```

Prints the value of a single property to stdout — a plain string with no extra formatting, suitable for scripting. Returns an error if the property does not exist.

### Set a property

Provide both `key` and `value`:

```
pumex prop <note> <key> <value>
```

Writes the property to the note's YAML frontmatter and reindexes the note inline before returning. If the property already exists its value is replaced; if it doesn't exist it is added. If the note has no frontmatter block, one is created.

## Examples

```
# List all properties
pumex prop architecture

# Get a single property
pumex prop architecture status
# → active

# Use in a script
STATUS=$(pumex prop architecture status)
echo "Status is $STATUS"

# Set a property
pumex prop architecture status done
pumex prop sprint-42 due 2026-06-01

# With explicit vault
pumex prop ./docs/architecture.md status active --vault work
```

## See also

- [`pumex read`](note.md#read) — read a note including its properties
- [`pumex search`](search.md) — filter search results by property with `--property k=v`
