# Morita Razor Storefront Instructions

## Worktree-First Workflow

- Treat the canonical checkout as a control and baseline directory, normally kept on its stable branch; do not edit implementation files in it.
- Before any edit, inspect `git rev-parse --show-toplevel`, `git branch --show-current`, and `git status --short --branch`.
- If the current path is canonical, select or create a worktree before editing. Create new worktrees from `origin/main` on a `codex/<feature>` branch unless an existing branch is explicitly selected.
- Use `../worktrees/<feature>/<repository>/` for multi-repository work, with one worktree and branch per affected repository.
- Set the working directory to the worktree for edits, tests, builds, and feature Docker Compose. Use the canonical checkout only for the stable baseline.
- Never switch, reset, clean, stash, or remove the canonical or an existing worktree without explicit authorization.
- Existing `/home/diogo/dev/worktrees/<project>/<feature>/` checkouts are legacy worktrees; do not move or remove them during normal feature work.

## Shared Workflow

- At the beginning of every substantive task, inspect the relevant documentation under `../../wiki` before deciding how to change the system.
- Then inspect the current repository documentation and implementation. Treat repository code as the current source of truth if it conflicts with the wiki.
- Check `git status --short --branch` before editing and preserve unrelated changes.
- For cross-repository work, inspect every affected repository and trace API contracts end to end.
- Keep changes minimal and preserve the established storefront visual identity and interaction patterns.
- Run focused verification appropriate to the change. Do not run E2E tests unless explicitly requested.
- Update relevant project or wiki documentation after significant architectural or domain discoveries.

## API Calls And Tenancy

- Register every `HttpClient` that calls Morita.API with `.AddHttpMessageHandler<StorefrontApiHeadersHandler>()`. The handler alone sets `X-Morita-Client-IP`, `X-Morita-Proxy-Secret`, and `X-Morita-Storefront-Host`; do not add these headers per request.
- The multi-tenant API resolves the store from `X-Morita-Storefront-Host` (the shopper's host, lowercase, no port) and trusts it only with a matching proxy secret. Without it, and once a second store is active, every storefront call returns `404`.
- `CatalogApi:ProxySecret` is required outside Development and E2E. Each storefront host must be registered in the API's `TenantDomain` table before a second store is activated.
