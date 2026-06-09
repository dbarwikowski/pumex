# pumex context

Gather an agent-ready **context pack** for a query: the vault passages that best
match, ranked, each with a `pumex read` pointer for drilling in. Built for AI
agents and scripts assembling a prompt — `search` tells you *which* notes match;
`context` hands you the relevant material plus where to get more.

## Synopsis

```text
pumex context <text> [--limit N] [--budget CHARS]
              [--vault NAME | --vault-path PATH | --all]
```

## Arguments

| Argument | Description |
|---|---|
| `text` | Free text or a natural-language question. Stopwords are stripped and the remaining terms are OR-matched against the FTS5 index. |

## Flags

| Flag | Description |
|---|---|
| `--limit N` | Maximum number of source notes in the pack (default `5`). |
| `--budget CHARS` | Approximate passage-character budget for the whole pack (default `6000`, ≈1.5k tokens). Lower-ranked sources are dropped whole once it's reached; the top source is always kept. |
| `--vault NAME` | Gather context within the named vault. |
| `--vault-path PATH` | Gather context within the vault at this path. |
| `--all` | Gather context across all registered vaults. |

## Description

Retrieval is **lexical** — it reuses the SQLite FTS5 index (bm25 ranking), the
same engine as [`pumex search`](search.md). There is no semantic/embedding
layer: a query matches on shared keywords, not meaning. Natural-language
questions work because stopwords (`how`, `does`, `the`, …) are dropped and the
content words are OR-matched, so notes hitting more terms rank higher.

The output is plain Markdown, written verbatim so an agent can paste it straight
into a prompt:

```text
# Context: <query>
<N> sources · lexical

## <relative/path.md>  (score 8.4)
<the best-matching section: nearest heading + its paragraph>
→ pumex read <pointer>
...
```

- **Score** is normalised so higher = better (it's the negated bm25 value). In a
  very small vault scores can all read `0.0` — that's a property of bm25 on a
  tiny corpus, not a bug.
- **Pointer** is the bare note name when it's unique in the vault, otherwise the
  relative path — always runnable as-is with [`pumex read`](note.md#read).
- One passage per source (the single best section, capped at ~15 lines).
  Non-Markdown matches (CSV/JSON/YAML) are included when those formats are
  enabled for the vault; their passages are raw-text windows.

When nothing matches, prints `No matches for "<query>"` and exits 0.

When no vault scope is given, the vault is auto-discovered from the current
working directory.

## Examples

```text
# Natural-language question
pumex context "how does the indexer handle config changes"

# Tighten the pack for a small prompt budget
pumex context "wikilink resolution" --limit 3 --budget 2000

# Across every registered vault
pumex context "named pipe protocol" --all
```

## See also

- [`pumex search`](search.md) — ranked match list with one-line snippets
- [`pumex read`](note.md#read) — follow a pack's pointer to the full note
