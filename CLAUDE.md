# Morita Worktree Instructions

## Worktree-First Workflow

- Canonical checkouts are control and baseline directories, normally kept on `main`; do not edit implementation files in them.
- Before any edit, inspect `git rev-parse --show-toplevel`, `git branch --show-current`, and `git status --short --branch`.
- If the current path is canonical, select or create a worktree before editing. Create new worktrees from `origin/main` on a `<feature>` branch unless an existing branch is explicitly selected.
- Multi-repository projects use `projects/<project>/worktrees/<feature>/<repository>/`.
- Single-repository projects use `projects/<project>/worktrees/<feature>/`.
- Keep one harness, one worktree, and one branch per repository checkout.
- Do not have multiple harnesses edit the same worktree or the canonical checkout concurrently.
- Start each harness with its working directory set to its assigned worktree.
- Run tests, builds, and feature Docker Compose from the worktree. Use canonical Docker Compose only for the stable `main` baseline.
- Never switch, reset, clean, stash, or remove a canonical or existing worktree without explicit authorization.
- Check `git status --short --branch` before starting work.
- Do not reset, clean, stash, commit, or remove another harness's worktree without explicit permission.
- Existing global worktrees under `dev/worktrees/<project>/<feature>/` are legacy worktrees; do not move or remove them as part of normal feature work.

For Morita, create all affected repository worktrees under `projects/morita/worktrees/<feature>/<repository>/` before editing. Use the same feature name across repositories.
