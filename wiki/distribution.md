# Distribution

## Binaries

Each GitHub release ships three self-contained single-file binaries for every
supported platform (`win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`,
`osx-arm64`):

| Binary | Purpose |
|--------|---------|
| `pumex` / `pumex.exe` | CLI — interactive terminal use |
| `pumex-daemon` / `pumex-daemon.exe` | Background indexing service |
| `pumex-mcp` / `pumex-mcp.exe` | MCP server — wires Pumex into AI clients |

The install scripts place all three under `~/.pumex/bin/` and add that
directory to `PATH`.

## Install

**Windows (PowerShell):**
```powershell
iwr https://raw.githubusercontent.com/dbarwikowski/pumex/master/install/install.ps1 | iex
```

**Linux / macOS:**
```sh
curl -fsSL https://raw.githubusercontent.com/dbarwikowski/pumex/master/install/install.sh | sh
```

Pin a specific version with `$env:PUMEX_VERSION = 'v0.2.0'` (PowerShell) or
`PUMEX_VERSION=v0.2.0` (sh) before running.

## MCP server (`pumex-mcp`)

`pumex-mcp` implements the [Model Context Protocol](https://modelcontextprotocol.io/)
over **stdio**. MCP clients launch it as a subprocess — there is no port or
URL to configure. The binary delegates all operations to the running
`pumex-daemon`; if the daemon is not running, tools return errors. Start the
daemon separately with `pumex daemon install` (registers as a system service)
or run `pumex-daemon` directly in a terminal.

### Claude Desktop

Edit `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS)
or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

```json
{
  "mcpServers": {
    "pumex": {
      "command": "pumex-mcp",
      "args": []
    }
  }
}
```

Restart Claude Desktop after saving. The Pumex tools will appear in the
tool picker once the daemon is running.

### Cursor

Open **Cursor Settings → MCP** and add a new server:

```json
{
  "pumex": {
    "command": "pumex-mcp",
    "args": []
  }
}
```

Or edit `.cursor/mcp.json` directly in your project root for a per-project
configuration.

### Troubleshooting

- Run `pumex ping` to verify the daemon is reachable.
- If `pumex-mcp` is not on `PATH`, use the full path:
  `~/.pumex/bin/pumex-mcp` (Linux/macOS) or
  `$HOME\.pumex\bin\pumex-mcp.exe` (Windows).
- `PUMEX_HOME` is respected by `pumex-mcp` — set it to point at a dev
  daemon the same way you would for the CLI.
