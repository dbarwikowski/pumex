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
| Structured properties / tags | yes | per-format (none from the framework fallback) |
| Appears in `pumex backlinks` | yes | yes — as a **target** |
| Emits `[[wikilinks]]` | yes | no (target only, never a source) |
| `pumex read` | rendered | raw passthrough (until a renderer ships) |
| `create` / `append` / `prop set` | yes | no — Markdown-only |

### Linking to non-Markdown files

A Markdown note links to a non-Markdown file with an **explicit extension**:

```markdown
See the dataset in [[data.csv]] and the config in [[settings.json]].
```

A bare `[[data]]` still resolves to `data.md` only — non-Markdown targets always
require the extension. The same rule applies to commands:

```sh
pumex read data.csv      # explicit extension → reads the CSV (raw)
pumex read data          # bare name → data.md only
pumex backlinks data.csv # who links to the CSV
```

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
