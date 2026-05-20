---
tags: [wiki, properties, frontmatter]
status: published
type: wiki
---
# Properties

Properties are YAML frontmatter fields. The daemon indexes them at write time and exposes them via `pumex prop`. All notes in this vault have frontmatter you can experiment with.

## `pumex prop` — unified list/get/set

The `pumex prop` command switches mode based on how many arguments you provide:

| Arguments | Mode |
|-----------|------|
| `pumex prop <note>` | List all properties |
| `pumex prop <note> <key>` | Get one property |
| `pumex prop <note> <key> <value>` | Set a property |

## List all properties on a note

```powershell
pumex prop index --vault sample
pumex prop wiki/daemon --vault sample
pumex prop tasks/backlog --vault sample
```

Expected output for `index`:

```
Key      Value
status   published
tags     [wiki, index, navigation]
type     wiki
```

## Get a single property

```powershell
pumex prop index status --vault sample
pumex prop wiki/search tags --vault sample
pumex prop tasks/backlog status --vault sample
```

## Set a property

```powershell
# Temporarily mark a note as draft
pumex prop index status "draft" --vault sample
pumex prop index status --vault sample
# → draft

# Restore it
pumex prop index status "published" --vault sample

# Add a custom property
pumex prop wiki/daemon author "dbarwikowski" --vault sample
pumex prop wiki/daemon --vault sample
```

## Tags are a property

Tags are a YAML list under the `tags` key. Set via `pumex prop`:

```powershell
pumex prop wiki/draft tags "[wiki, draft, wip]" --vault sample
pumex prop wiki/draft tags --vault sample
```

## Use properties as search filters

```powershell
# Find all published notes
pumex search --property status=published --vault sample

# Find all wiki-type notes
pumex search --property type=wiki --vault sample

# Combine with tag filter
pumex search --tag tasks --property status=active --vault sample
```

→ See [[tags]] for tag-specific commands, [[search]] for combining filters, [[notes]] for note CRUD.
