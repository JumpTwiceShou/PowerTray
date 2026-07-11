# AGENTS.md - PowerTray

## Startup

1. Read `~/.codex/SHARED_AGENT_RULES.md`.
2. Read `memory/powertray-design-019ead2d-ea09-7620-a6f2-7a45c88d7199.md` before repository changes.
3. Read the active file under `task/` when the work requires one.

After a repository change, update the local ignored design memory with the implementation, decision, release note, or workflow change.

## Scope And Communication

This is the real PowerTray repository inside the compatibility `logi` workspace. User-facing work is written in Chinese by default; code, paths, commands, commits, and public release text follow the repository's English conventions.

## Canonical Paths

- Current Windows: `D:\dev\repos\logi\LGSTrayBattery-master`.
- Windows VM102: `C:\dev\repos\logi\LGSTrayBattery-master`.
- Ubuntu VM101: `~/src/repos/logi/LGSTrayBattery-master`.

Direct work on `main` is allowed. Run only build/test checks required by the affected PowerTray behavior.

## Remote And Env

- Private development remote: `sync` → `JumpTwiceShou/PowerTray-dev`.
- Public release remote: `origin` → `JumpTwiceShou/PowerTray`.
- Ordinary development pushes to `sync`; public releases require explicit approval.
- Env paths: `/shared/common` and `/projects/logi`; local output is ignored `.env.local`.
- Telegram notifications use local ignored env values through `$telegram-notify`; never expose token or chat ID.

## Release Rules

- Store `RELEASE_NOTES_v*.md` under `release-notes/`.
- A GitHub Release body starts with `## What's New` and 2–4 concise English bullets; do not repeat the title as H1.
- Show English release notes and a Chinese translation before an explicitly requested public release.

## Completion

Check the scoped diff and staged files, run only necessary validation, update local design memory and the task, then archive the task when complete.
