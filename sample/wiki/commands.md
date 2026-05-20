---
tags: [wiki, reference, commands]
status: published
type: wiki
---
# Commands Reference

One-page cheatsheet for every pumex CLI command. All examples target `--vault sample`.

## Daemon

```powershell
pumex ping
pumex daemon status
pumex daemon install
pumex daemon restart
pumex daemon uninstall
```

## Vaults

```powershell
pumex vault list
pumex vault add sample C:\Repos\Pumex\sample
pumex vault remove sample
pumex new myvault C:\Notes\myvault
```

## Search

```powershell
pumex search "daemon" --vault sample
pumex search "watcher OR indexer" --vault sample
pumex search --tag wiki --vault sample
pumex search "backlog" --tag tasks --vault sample
pumex search --property status=published --vault sample
pumex search --tag wiki --property type=wiki --vault sample
pumex search "daemon" --limit 3 --vault sample
pumex search "daemon" --all
```

## Notes

```powershell
pumex list --vault sample
pumex read index --vault sample
pumex read wiki/search --vault sample
pumex read daemon --raw --vault sample
pumex create wiki/draft --content "# Draft" --vault sample
pumex append index --content "- updated entry" --vault sample
pumex delete wiki/draft --vault sample
```

## Tags

```powershell
pumex tags --vault sample
pumex tags --all
```

## Properties

`pumex prop` is a unified command: no args = list, one arg = get, two args = set.

```powershell
pumex prop index --vault sample
pumex prop index status --vault sample
pumex prop index status "draft" --vault sample
pumex prop index status "published" --vault sample
pumex prop wiki/daemon author "dbarwikowski" --vault sample
```

## Backlinks

```powershell
pumex backlinks index --vault sample
pumex backlinks daemon --vault sample
pumex backlinks backlog --vault sample
pumex backlinks index --all
```

## Daily

```powershell
pumex daily --vault sample
pumex daily --date 2026-05-19 --vault sample
pumex daily append --content "- reviewed search performance" --vault sample
pumex daily append --content "- retroactive note" --date 2026-05-18 --vault sample
```

→ See [[search]], [[notes]], [[tags]], [[properties]], [[backlinks]], [[daily]], [[vaults]], [[daemon]] for deep dives with expected output.
