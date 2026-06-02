# Text formats

Pumex indexes Markdown by default, and can additionally index other plain-text
formats (CSV, JSON, YAML, …) per vault. This page describes the framework that
makes a vault multi-format; individual formats are enabled in the vault config
and gain richer parsing/rendering over time.

> **Framework status.** Enabling a format that does not yet have a dedicated
> parser still works — the file is indexed as **full text** (searchable) with no
> extracted properties. Dedicated parsers (structured properties, nice
> rendering) ship per format.

## Enabling formats

A vault's `.pumex/config.json` controls which extensions are indexed:

```json
{
  "name": "my-vault",
  "created": "2026-05-29T12:00:00+00:00",
  "version": 1,
  "formats": ["csv", "json", "yaml"],
  "ignore": ["templates/**", "*.tmp.md"]
}
```

- `formats` — extra extensions to index. Markdown is **always** indexed and need not be listed.
- `ignore` — glob excludes, applied to every format (including Markdown).

> The config is parsed as **strict JSON** (`System.Text.Json`): comments and
> trailing commas are not supported.

- **Markdown is always on.** `formats` only adds extras. A missing or older
  config indexes Markdown only — exactly the previous behaviour.
- **Live reload.** Editing `config.json` is picked up without restarting the
  daemon: enabling a format indexes its files, disabling one removes them from
  the index.
- **Always skipped.** `.pumex/`, `.git/`, `.obsidian/`, and any other
  dot-directory are never indexed, regardless of config.

## Ignore globs

`ignore` entries match vault-relative paths, case-insensitively:

| Pattern shape | Matches |
|---|---|
| no `/` (e.g. `*.log`) | the **basename** at any depth |
| contains `/` (e.g. `templates/**`) | the **full relative path**, anchored |
| `**` | crosses directory separators |
| `*`, `?` | a single path segment (do not cross `/`) |

Ignore rules apply to Markdown too, so you can finally exclude e.g.
`templates/**` `.md` files from the index.

## How non-Markdown files behave

| Capability | Markdown | Other formats |
|---|---|---|
| Full-text search | yes | yes |
| Structured properties / tags | yes | per-format (JSON: top-level scalars → properties; none from the framework fallback) |
| Appears in `pumex backlinks` | yes | yes — as a **target** |
| Emits `[[wikilinks]]` | yes | no (target only, never a source) |
| `pumex read` | rendered | CSV/TSV → table; JSON → syntax-highlighted; others → raw passthrough (until a renderer ships) |
| `create` / `append` / `prop set` | yes | no — Markdown-only |

### Linking to non-Markdown files

A Markdown note links to a non-Markdown file with an **explicit extension**:

```markdown
See the dataset in [[data.csv]] and the config in [[settings.json]].
```

A bare `[[data]]` still resolves to `data.md` only — non-Markdown targets always
require the extension. The same rule applies to commands:

```sh
pumex read data.csv      # explicit extension → renders the CSV as a table
pumex read data          # bare name → data.md only
pumex backlinks data.csv # who links to the CSV
```

## Rendering CSV / TSV

`pumex read` renders `.csv` and `.tsv` files as a table, using row 1 as the
column headers. The delimiter (`,` vs `\t`) is auto-detected from the first few
non-empty lines; a file that isn't recognizably tabular falls back to raw text.

Use `--limit N` to cap how many data rows are shown (default `100`); when capped,
a `showing X of Y rows` line is printed below the table. `--limit` is a display
concern only — the daemon always returns the full file, and the flag is ignored by
formats that don't paginate. Use `--raw` to print the file verbatim instead.

```sh
pumex read animals.csv            # table, up to 100 rows
pumex read animals.csv --limit 20 # cap to 20 rows
pumex read animals.csv --raw      # verbatim, no table
```

## Rendering JSON

Enable `.json` with `"formats": ["json"]`. JSON files are full-text searchable,
and the **top-level scalar keys of a root object** (string / number / boolean)
become queryable properties — so they work with `pumex search --property` and
`pumex property` just like Markdown frontmatter:

```sh
pumex search --property status=active --format json
pumex property get settings.json theme
```

Nested objects/arrays and `null` values are not properties (they stay searchable
via full text only). An array-root or bare-scalar document has no properties.

`pumex read` renders JSON with syntax highlighting:

```sh
pumex read settings.json            # syntax-highlighted JSON
pumex read records.json --limit 20  # array root: show the first 20 elements
pumex read settings.json --raw      # verbatim, no highlighting
```

- **JSONC tolerated.** `//` comments and trailing commas are accepted (handy for
  `tsconfig.json`-style files) for both indexing and rendering. A file that
  still fails to parse falls back to full-text indexing and raw rendering.
- **`--limit`** caps how many elements of an **array-root** document are shown
  (default `100`, with a `showing X of Y elements` notice); it is ignored for
  object or scalar roots. JSON is never truncated by depth.
- **`.jsonl` (newline-delimited JSON) is not supported** as a structured format.
  If enabled it is indexed as plain full text with no per-record properties.

## Filtering by format

`search` and `list` accept `--format` (alias `--ext`), repeatable and
comma-separated:

```sh
pumex list --format csv
pumex search capybara --format csv,json
```

## See also

- [`pumex search`](search.md)
- [`pumex read` / `pumex list`](note.md)
- [`pumex new`](new.md) — creates the vault marker and `config.json`
