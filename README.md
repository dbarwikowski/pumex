# Pumex

A headless, daemon-based knowledge engine for plain Markdown vaults — Obsidian without Electron, agent- and CLI-friendly, composable like a Unix tool.

Built for developers in the terminal, AI agents, servers and headless setups, and CI/CD pipelines. If you want a GUI for writing notes, you want Obsidian. If you want a fast index over your notes that you can pipe, script, and call from another process, you want Pumex.

## Status

Early v0.1. The happy path works end-to-end:

- Register vaults, get incremental indexing via a long-lived daemon
- Full-text search (FTS5), tag listing, wikilink backlinks
- Note CRUD over a named-pipe IPC layer
- Self-contained single-file binaries for Windows, Linux, macOS (x64 + arm64)
- 112 unit + integration tests, BenchmarkDotNet suite for the perf-critical paths

Not yet shipped: plugin SDK, IPC auth. See `pumex --help` for the current command surface.

## Install

**Linux / macOS:**

```sh
curl -fsSL https://raw.githubusercontent.com/dbarwikowski/pumex/main/install.sh | sh
export PATH="$HOME/.pumex/bin:$PATH"
pumex daemon install      # registers the daemon as a systemd user service / launchd agent
```

**Windows (PowerShell):**

```powershell
iwr https://raw.githubusercontent.com/dbarwikowski/pumex/main/install.ps1 | iex
$env:PATH = "$HOME\.pumex\bin;$env:PATH"
# In an elevated shell:
pumex daemon install      # registers the daemon as a Windows service
```

The installer downloads the right binary for your OS + arch from the latest GitHub Release and drops it into `~/.pumex/bin/`. Pin a version with `PUMEX_VERSION=v0.2.0` (or `$env:PUMEX_VERSION` on Windows).

## 30-second demo

```sh
mkdir -p ~/notes && cd ~/notes
pumex new mynotes .                        # creates .pumex/ marker, registers with the daemon

cat > welcome.md <<'EOF'
---
tags: [intro, demo]
---
# Welcome
First note. See [[ideas]] for what's next.
EOF

cat > ideas.md <<'EOF'
# Ideas
Things to explore.
EOF

pumex search Welcome                       # FTS5 search over the vault
pumex tags                                 # tag aggregation (auto-scoped to current vault)
pumex backlinks ideas.md                   # finds welcome.md (the wikilink resolved)
pumex note read welcome.md                 # frontmatter + body, structured
```

Every read command auto-discovers the vault you're in by walking up from the current directory until it finds a `.pumex/` marker. Override with `--vault NAME`, `--vault-path PATH`, or `--all` for cross-vault queries.

## Commands

| Command | Purpose |
|---|---|
| `pumex ping` | Daemon health check |
| `pumex new <name> [path]` | Create a vault marker + register with the daemon |
| `pumex search <query> [--limit N]` | FTS5 search |
| `pumex tags` | Tag aggregation, vault-scoped by default |
| `pumex backlinks <path>` | Notes that link to the given note |
| `pumex vaults` / `pumex vault add <name> <path>` | List / register vaults |
| `pumex note read <path> [--raw]` | Read note (parsed frontmatter + body, or raw) |
| `pumex note create <path> [--content TEXT \| --stdin]` | Write a new note |
| `pumex note append <path> [--content TEXT \| --stdin] [--inline]` | Append to an existing note |
| `pumex note delete <path>` | Delete a note |
| `pumex note list` | List all notes in the vault |
| `pumex property list <path>` | List all frontmatter properties on a note |
| `pumex property get <path> <key>` | Read a single frontmatter property |
| `pumex property set <path> <key> <value>` | Write a frontmatter property |
| `pumex daily [read] [--date YYYY-MM-DD]` | Read today's (or a given) daily note |
| `pumex daily append [--content TEXT \| --stdin] [--date YYYY-MM-DD]` | Append to a daily note |
| `pumex vault remove <name>` | Unregister a vault |
| `pumex daemon <status\|install\|uninstall\|restart>` | Manage the platform-native daemon service |

All commands accept `--vault NAME`, `--vault-path PATH`, or `--all` to override auto-discovery.

## How it works

```
Disk → NoteParser → IndexDb (SQLite + FTS5) → CLI / agent
        ↑              ↑
        FileSystemWatcher (debounced)
        VaultIndexingOrchestrator (one IndexingService per vault)

CLI ⇄ NamedPipe IPC ⇄ Command handlers ⇄ IndexDb
```

The daemon (`pumex-daemon`) owns the index and keeps it warm via per-vault `IndexingService` instances driven by a `FileSystemWatcher`. The CLI (`pumex`) is a thin client that speaks length-prefixed JSON over a named pipe — the same mechanism is the integration point for AI agents and other external tooling.

The split is deliberate: the daemon does all the heavy work once and serves cheap reads forever; the CLI starts cold every time and stays small.

## Performance

Targets for a 10 000-file vault, measured with BenchmarkDotNet (`tests/Pumex.Benchmarks`):

| Operation | Budget | Measured @ 10k |
|---|---:|---:|
| Cold full scan | 2–5 s | ~5 s ¹ |
| Warm full scan (mtime-only) | ~200 ms | 138 ms |
| FTS search | <50 ms | ~20 ms |
| Incremental update (per file event) | <10 ms | 3.3 ms |

Run them yourself: `dotnet run -c Release --project tests/Pumex.Benchmarks`.

¹ Reduced from 6.1 s after eliminating ~100k `SqliteCommand` re-preparations per scan via command reuse across the upsert transaction.

## Contributing

Build:

```sh
dotnet build Pumex.sln
dotnet test
dotnet run --project Pumex.Daemon       # foreground daemon for development
```

`Pumex.Cli` (thin client, references `Pumex.Contracts` only), `Pumex.Daemon` (the indexer + IPC server), and `Pumex.Contracts` (DTOs, paths). Tests under `tests/`.

PRs welcome. The design log is in `context.md` (Polish).

## License

MIT — see [LICENSE](./LICENSE).
