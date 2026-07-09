# Environment - PowerTray

## Current State

PowerTray has no required runtime env for normal local app usage.

Outer workspace notification settings may exist in local ignored env files. Values were not copied or read.

## Infisical

- Project: `dev-secrets`
- Project ID: `cc4ee95f-8c4f-406e-832e-f65cdeb73739`
- Shared path: `/shared/common`
- Project path: `/projects/logi`
- Environment: `dev`
- Local output file: `.env.local`

## Optional Local Keys

- `TELEGRAM_BOT_TOKEN`
- `TELEGRAM_CHAT_ID`

## Rules

- `.env.local` is local-only and must stay gitignored.
- `.env.example` stores variable names only.
- `sync-all-projects` is remote-to-local only.
- Adding env keys requires updating `.env.example` and this file, then `push-project-env` dry-run before explicit apply.
