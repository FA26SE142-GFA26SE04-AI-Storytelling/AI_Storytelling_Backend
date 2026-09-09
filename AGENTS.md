# AI Storytelling Backend - Project Boundary

These instructions apply to every Codex task started in this repository.

## Access boundary

- Treat `D:\Capstone_AI_Storytelling\AI_Storytelling_Backend` as the only project workspace.
- Work autonomously inside this repository when the active permission profile allows it.
- Never request, select, or emulate `danger-full-access`, `--add-dir`, or `--dangerously-bypass-approvals-and-sandbox` for repository work.
- Do not create, modify, move, or delete files outside this repository. System/runtime reads required by build tools are allowed, but do not inspect unrelated user files or source trees.
- Do not follow a symbolic link, junction, or reparse point when its resolved target is outside the repository.
- If a task requires a write outside the repository, stop and report the exact path and reason to the user.

## Destructive operations

- Before a recursive delete or move, resolve every target to an absolute path and verify that it is a descendant of the repository root.
- Never run a destructive command against the repository root itself, its parent, a drive root, a home directory, an unresolved variable, or a wildcard-expanded path.
- Preserve existing user changes. Do not use `git reset --hard`, `git clean -fd`, or `git checkout --` unless the user explicitly requests that exact operation.

## External effects

- Treat deployments, remote database changes, cloud resources, messages, and external-service writes as separate from filesystem access. Perform them only when the user explicitly includes that external target in scope.
- Never print, commit, or transmit secrets from `.env`, `appsettings*.json`, credentials, tokens, or local secret stores.

## Database migrations

- Never create, regenerate, remove, apply, roll back, or rerun a database migration unless the user explicitly requests that exact migration operation.
- Do not run `dotnet ef database update`, `dotnet ef migrations add`, `dotnet ef migrations remove`, migration bundles, startup migration helpers, database reset/seed commands, or equivalent SQL automatically as part of implementation or testing.
- Do not modify an existing migration or model snapshot merely to make a build or test pass. Report the mismatch and wait for explicit direction.
- Treat every database and migration command as an external side effect even when the database is local, containerized, or configured for development.
- When a requested code change appears to require a schema change, implement only the in-scope source changes that are safe without migration, then clearly report the required migration and its proposed name. Do not execute it without explicit approval in the user's latest request.

## Source structure and architecture

- Follow the repository's current source layout, project boundaries, naming conventions, namespaces, dependency direction, and established implementation patterns.
- Inspect adjacent features and existing abstractions before adding code. Place new code beside the equivalent current feature instead of introducing a new architecture or parallel folder structure.
- Preserve the existing layer boundaries and dependency direction. Do not move responsibilities across API, application/business, domain, infrastructure/data-access, or test projects unless the user explicitly requests an architectural refactor.
- Reuse existing base classes, interfaces, DTO conventions, dependency-injection registration patterns, result/error models, and test organization where applicable.
- Do not rename, relocate, or broadly refactor unrelated files while implementing a scoped feature or fix.
- If the current source contains competing legacy and refactored structures, determine which structure is active from solution/project references and nearby code. Do not merge or replace the structures without explicit user direction.

## Verification

- Keep generated files, build outputs, test results, and temporary project artifacts inside this repository or the sandbox-provided system temp directory.
- After changing this access policy, run `powershell -NoProfile -ExecutionPolicy Bypass -File .\.codex\Test-ProjectBoundary.ps1` from the repository root. `Bypass` applies only to that child process and does not change the machine policy.
