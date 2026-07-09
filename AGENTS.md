# Repository Instructions

## Design Memory

- At the start of every new conversation for this repository, read `F:\logi\LGSTrayBattery-master\memory\powertray-design-019ead2d-ea09-7620-a6f2-7a45c88d7199.md` before making repository changes.
- After making any repository change, update `F:\logi\LGSTrayBattery-master\memory\powertray-design-019ead2d-ea09-7620-a6f2-7a45c88d7199.md` so it reflects the new implementation, design decision, release note, or workflow rule.
- The design memory lives under `memory/`, which is intentionally ignored by Git, so keep it updated locally even when the related source change is tracked.

## Communication And Release Rules

- User-facing analysis, execution notes, plans, verification summaries, and final replies should be written in Chinese by default.
- Keep code identifiers, file paths, commands, commit messages, and GitHub Release body text in English when that is the project convention.
- Keep all `RELEASE_NOTES_v*.md` files under `release-notes/`; do not add new release notes at the repository root.
- GitHub Release bodies must not repeat the release title as an H1. Use the GitHub title field for `PowerTray x.y.z`, and make the body start with `## What's New` plus 2-4 short English bullets.
- Public release notes should stay concise. Put detailed debug evidence, hardware readings, commit hashes, and validation notes in the local design document instead.
- Before pushing a release commit/tag or creating/updating a GitHub Release, show the English release notes and a Chinese translation for confirmation.

## Migration Development Model

- Canonical repo path on current Windows: `D:\dev\repos\logi\LGSTrayBattery-master`.
- Compatibility workspace path: `D:\dev\repos\logi`; old `F:\logi` is a compatibility junction only.
- Current Windows worktrees: `D:\dev\worktrees\logi\<branch-name>`.
- Windows VM102 repo/worktrees: `C:\dev\repos\logi\LGSTrayBattery-master`, `C:\dev\worktrees\logi\<branch-name>`.
- Ubuntu VM101 repo/worktrees: `~/src/repos/logi/LGSTrayBattery-master`, `~/src/worktrees/logi/<branch-name>`.
- Current Windows PC is migration control/final validation/fallback only; primary development moves to Ubuntu VM101 and Windows VM102.
- Do not develop directly on `main`; use one branch per task and one worktree per Codex session.
- Public release remote: `origin https://github.com/JumpTwiceShou/PowerTray.git`.
- Planned private development remote: `sync git@github.com:JumpTwiceShou/PowerTray-dev.git` after explicit Phase 6 approval.
- Public `origin` is release-only; default development push target after Phase 6 is private `sync`.
- Env sync uses Infisical; local output is `.env.local` and must remain ignored.
- Outer `F:\logi\.env.local` was not copied during migration and must not be committed.
- Use the Telegram notification rule from the outer workspace only through local ignored env values; never print or commit token/chat id values.

## Worktree Details
See `docs/worktree.md` for this project's exact worktree commands. Do not create a worktree or branch until the user confirms the task scope.
