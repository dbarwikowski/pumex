---
status: active
---

Notes about the sample #data datasets.

| File | Format | Description |
|------|--------|-------------|
| [[animals.csv]] | CSV | Small reference table — id, animal, habitat |
| [[expenses.csv]] | CSV | Monthly expense log with category and amount |
| [[projects.tsv]] | TSV | Project tracker — status, owner, due date, priority |
| [[settings.json]] | JSON | App settings — top-level scalars become properties; JSONC comments allowed |
| [[events.json]] | JSON | Array-root log — use `--limit` to cap shown elements |
| [[config.yaml]] | YAML | Mapping root — top-level scalars become properties; comments preserved |
| [[roster.yaml]] | YAML | Block-sequence root — use `--limit` to cap shown elements |

Bare links like [[index]] still resolve to Markdown only.
Try: `pumex read expenses.csv` or `pumex read projects.tsv` for tables,
`pumex read settings.json` for syntax-highlighted JSON, `pumex read config.yaml`
for syntax-highlighted YAML, and
`pumex search --property theme=dark --format json` to query JSON scalars
(or `--property environment=prod --format yaml` for YAML).
