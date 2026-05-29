# Pumex

A headless, daemon-based knowledge engine for plain Markdown vaults — Obsidian without Electron, agent- and CLI-friendly, composable like a Unix tool.

Built for developers in the terminal, AI agents, servers and headless setups, and CI/CD pipelines. If you want a GUI for writing notes, you want Obsidian. If you want a fast index over your notes that you can pipe, script, and call from another process, you want Pumex.

## Status

Early v0.1. The happy path works end-to-end:

- Register vaults, get incremental indexing via a long-lived daemon
- Full-text search (FTS5), tag listing, wikilink backlinks
- Note CRUD over a named-pipe IPC layer
- Self-contained single-file binaries for Windows, Linux, macOS (x64 + arm64)
- Unit + integration tests, BenchmarkDotNet suite for the perf-critical paths

Not yet shipped: plugin SDK, IPC auth. See `pumex --help` for the current command surface.

## Install

**Linux / macOS:**

```sh
curl -fsSL https://raw.githubusercontent.com/dbarwikowski/pumex/master/install/install.sh | sh
# PATH and daemon service are configured automatically.
```

**Windows (PowerShell):**

```powershell
iwr https://raw.githubusercontent.com/dbarwikowski/pumex/master/install/install.ps1 | iex
# PATH is updated automatically. The daemon is registered as a per-user scheduled task — no admin shell required.
```

The installer downloads the right binary for your OS + arch from the latest GitHub Release and drops it into `~/.pumex/bin/`. Pin a version with `PUMEX_VERSION=v0.2.0` (or `$env:PUMEX_VERSION` on Windows).

## Uninstall

**Linux / macOS:**

```sh
curl -fsSL https://raw.githubusercontent.com/dbarwikowski/pumex/master/install/uninstall.sh | sh
# Add PUMEX_PURGE=1 to also delete ~/.pumex/ (index, config).
```

**Windows (PowerShell):**

```powershell
iwr https://raw.githubusercontent.com/dbarwikowski/pumex/master/install/uninstall.ps1 | iex
# Add $env:PUMEX_PURGE = '1' to also delete $HOME\.pumex\ (index, config).
```

The scripts stop and remove the daemon service, delete the binaries from `~/.pumex/bin/`, and leave the data directory (`~/.pumex/`) untouched unless you opt into purge. Vault markers (`.pumex/` directories inside your note folders) are never touched.

## Quick start

```sh
# Install and start the daemon
pumex daemon install

# Create a vault
pumex new work ~/notes/work

# Write a note
pumex create standup --content "## 2026-05-20\n- shipped v1.2"

# Search it
pumex search standup

# Read it back
pumex read standup
```

## Commands

| Command | Description |
|---|---|
| `pumex --version` | Print CLI and daemon versions |
| `pumex ping` | Daemon health check |
| `pumex new <name> [path]` | Create a vault marker + register with the daemon |
| `pumex search [query] [--tag TAG]... [--property k=v]... [--format EXT]...` | FTS5 full-text search with optional tag, property, and format filters |
| `pumex tags` | Tag aggregation with counts, vault-scoped by default |
| `pumex backlinks <path-or-name>` | Notes that link to the given note via `[[wikilink]]` |
| `pumex vault list` | List registered vaults |
| `pumex vault add <name> <path>` | Register an existing directory as a vault |
| `pumex vault remove <name>` | Unregister a vault (files untouched) |
| `pumex read <note> [--raw]` | Display a note — parsed frontmatter + rendered body, or raw |
| `pumex create <note> [--content TEXT]` | Create a note (pipe stdin when `--content` is omitted) |
| `pumex append <note> [--content TEXT] [--inline]` | Append to an existing note |
| `pumex delete <note>` | Delete a note |
| `pumex list [--format EXT]...` | List all notes in the vault, optionally filtered by format |
| `pumex prop <note> [key [value]]` | List, get, or set frontmatter properties |
| `pumex daily [--date YYYY-MM-DD]` | Read today's (or a given) daily note |
| `pumex daily append [--content TEXT]` | Append to a daily note |
| `pumex daemon <status\|start\|stop\|restart\|install\|uninstall>` | Manage the daemon: ad-hoc start/stop or register as a user-level service |

Full reference: [`docs/`](docs/index.md).

### Vault scope

Most commands accept these flags to control which vault is targeted:

| Flag | Description |
|---|---|
| `--vault NAME` | Select vault by registered name |
| `--vault-path PATH` | Select vault by directory path |
| `--all` | Run across all registered vaults |

When no scope flag is given, the vault is auto-discovered by walking up from the current working directory until a `.pumex/` marker directory is found.

### Text formats

Markdown is always indexed. A vault can opt into additional plain-text formats (CSV, JSON, YAML, …) via its `.pumex/config.json`:

```jsonc
{
  "formats": ["csv", "json"],            // extra extensions to index (Markdown is always on)
  "ignore": ["templates/**", "*.tmp.md"] // glob excludes, applied to every format
}
```

Editing the config is picked up live — enabling a format indexes its files, disabling one removes them. Non-Markdown files are full-text searchable and can be linked as targets from notes with an explicit extension (`[[data.csv]]`); a bare `[[data]]` still means `data.md`. Filter with `--format`/`--ext` on `search` and `list`. Full details: [`docs/formats.md`](docs/formats.md).

## How it works

```
pumex (CLI)  ──named pipe──►  pumex-daemon (background)
                                    │
                               SQLite FTS5 index
                                    │
                          Markdown (+ opt-in CSV/JSON/…) vault(s)
```

The daemon (`pumex-daemon`) owns the index and keeps it warm via per-vault `IndexingService` instances driven by a `FileSystemWatcher`. The CLI (`pumex`) is a thin client that speaks length-prefixed JSON over a named pipe — the same mechanism is the integration point for AI agents and other external tooling.

The split is deliberate: the daemon does all the heavy work once and serves cheap reads forever; the CLI starts cold every time and stays small.

## Performance

Targets for a 10 000-file vault, measured with BenchmarkDotNet (`src/Pumex.Benchmarks`):

| Operation | Budget | Measured @ 10k |
|---|---:|---:|
| Cold full scan | 2–5 s | ~5 s ¹ |
| Warm full scan (mtime-only) | ~200 ms | 138 ms |
| FTS search | <50 ms | ~20 ms |
| Incremental update (per file event) | <10 ms | 3.3 ms |

Run them yourself: `dotnet run -c Release --project src/Pumex.Benchmarks`.

¹ Reduced from 6.1 s after eliminating ~100k `SqliteCommand` re-preparations per scan via command reuse across the upsert transaction.

## Contributing

### Project layout

| Directory | Role |
|---|---|
| `src/Pumex.Daemon/` | Indexer, IPC server, command handlers, SQLite via FTS5 |
| `src/Pumex.Contracts/` | DTOs, `PumexPaths`, `IpcRequest/Response`. Zero external deps. |
| `src/Pumex.Cli/` | Thin CLI — Spectre.Console + named-pipe client. References `Pumex.Contracts` only. |
| `src/Pumex.*.Tests/` | Unit tests, integration tests, benchmarks |
| `install/` | Install / uninstall scripts for Linux, macOS, Windows |

### Build and test

```sh
dotnet build Pumex.sln
dotnet test
```

### Running a dev daemon alongside the installed one

Set `PUMEX_HOME` to an isolated directory. This changes both the pipe name and the index path so the dev daemon and the installed production service coexist without conflicts:

```sh
# Linux / macOS
export PUMEX_HOME="$HOME/.pumex-dev"
dotnet run --project src/Pumex.Daemon       # dev daemon — own pipe, own index.db
pumex search foo                            # CLI picks up PUMEX_HOME automatically
```

```powershell
# Windows
$env:PUMEX_HOME = "$HOME\.pumex-dev"
dotnet run --project src/Pumex.Daemon
pumex search foo
```

The production daemon (installed service, no env var) is untouched. The dev index is a rebuildable cache — delete `~/.pumex-dev/index.db` and restart any time.

### Releasing

Every push to `master` produces a tagged release. CI computes the next patch number from existing `vX.Y.*` tags and embeds it into the binaries via `dotnet publish -p:Version=...` — no manual tag pushes, no source-file bump.

`Directory.Build.props` carries only the major and minor in `X.Y.0-dev` form (e.g. `<Version>0.1.0-dev</Version>`). To bump the minor or major, edit that file on `master`, commit, push — the next CI run sees no `vX.Y.*` tag for the new minor and produces `vX.Y.0`. Local `dotnet build` produces `X.Y.0-dev` binaries, so `pumex --version` makes dev builds easy to spot.

PRs welcome.

## License

MIT — see [LICENSE](./LICENSE).
