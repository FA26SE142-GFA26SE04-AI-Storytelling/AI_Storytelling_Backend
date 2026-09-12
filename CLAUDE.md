# CLAUDE.md — AI Storytelling Backend

## 1. Project goal

This repository is the backend for the AI Storytelling project — an AI-assisted story generation and reading platform for children (ages ~6–12), supervised by parents/teachers and monitored by an administrator. Core domains include: profile & supervision management, an AI story-generation pipeline (outline → content → human approval → media), the child reading experience (TTS/ASR, quizzes, EXP/vocabulary tracking), distribution/O2O intervention, adaptive learning recommendations, and admin/AI governance.

Every child-facing story or content change must remain human-approved before it reaches a child, and safety/guardrail logic must never be bypassed — treat this as a hard constraint on top of ordinary scope discipline.

Work only within the scope of the current user request. Preserve the existing architecture, conventions, APIs, database schema, and behavior unless the task explicitly requires changing them.

## 2. Mandatory workflow

For every non-trivial task:

1. Understand the request and inspect the relevant existing code.
2. If the task involves a new feature, architecture, or significant behavior change, use the Superpowers workflow:
   - brainstorming
   - writing-plans
   - implementation
   - testing
   - code review
   - verification before completion
3. Before editing, identify the files you expect to create, modify, or delete.
4. Do not make unrelated refactors or cleanup.
5. Implement the smallest complete change that satisfies the request.
6. Run appropriate tests/build/validation after implementation.
7. Before reporting completion, verify the actual git diff and working tree.
8. End with a concise change summary.

## 3. File-change policy

The user wants explicit control over project changes.

- Never create a new file unless it is necessary for the requested task.
- Before creating a new file, clearly state:
  - the exact path
  - why the file is needed
  - what it will contain
- Never create temporary files, scratch files, generated documentation, logs, backups, or alternative implementations unless explicitly requested or required by the tool/framework.
- Do not modify files outside the task scope.
- Do not delete or rename files unless explicitly requested or clearly required by the approved implementation.
- Do not overwrite existing user changes.
- If an existing uncommitted change conflicts with the task, stop and explain the conflict before proceeding.
- Do not automatically commit, push, merge, reset, revert, or discard user changes unless explicitly requested.

Note: Claude Code's `Manual` permission mode is responsible for approval prompts. This file does not replace Claude Code's permission system.

## 4. Working directory and Git

Work in the currently opened project/workspace.

Do not create or switch to a Git worktree unless:
- the user explicitly requests a worktree, or
- the task genuinely requires isolated parallel development and the user has approved it.

When a worktree is warranted, use the `using-git-worktrees` skill/plugin if available rather than ad-hoc `git worktree` commands.

Before and after substantial changes, use git status/diff when appropriate to understand the working tree.

Never discard existing changes just to make tests pass.

## 5. Architecture

Respect the repository's existing architecture and dependency direction.

From the current project structure, relevant areas include:

- `src/`
- `Core/`
- `StoryPlatform.Api/`
- `StoryPlatform.Application/`
- `StoryPlatform.Domain/`
- `StoryPlatform.Infrastructure/`
- `Shared/`
- `StoryPlatform.BLL/`
- `StoryPlatform.DAL/`
- `tests/`
- `Database/`

Note: the listed folders mix two naming conventions — Clean Architecture-style (`StoryPlatform.Api/Application/Domain/Infrastructure`) and legacy-style (`StoryPlatform.BLL/DAL`). Before editing business logic or data access, confirm which convention is authoritative for the module in scope (they may represent different subsystems, or one may be a partial migration in progress) rather than assuming based on folder name alone.

Do not move code between layers or introduce a new architectural pattern without first explaining why it is necessary and getting user approval.

Prefer existing abstractions, services, repositories, DTOs, helpers, dependency-injection patterns, and conventions over creating duplicates.

## 6. Database safety

For database-related work:

- Inspect the existing schema, migrations, seed scripts, DbContext/configuration, and connection configuration first.
- Do not silently change production/destructive database behavior.
- Never drop databases, tables, data, or migrations unless explicitly requested.
- Prefer idempotent seed/migration behavior where consistent with the existing project.
- Keep secrets and passwords out of source code.
- Never print, log, or paste the contents of connection strings, API keys, or files like `appsettings.*.json`/`.env` into chat output; treat them as sensitive even when reading them is necessary to complete a task.

## 7. Code quality

- Follow the language/framework conventions already used by the repository.
- Keep methods focused and avoid unnecessary abstraction.
- Reuse existing utilities and patterns.
- Do not add dependencies unless necessary.
- If adding a dependency is necessary, explain why before adding it.
- Keep public API changes intentional and documented in the final summary.
- Handle errors consistently with existing project conventions.

## 8. Testing

Use tests appropriate to the change.

For new behavior:
- Prefer writing a focused test first when practical.
- Add or update unit/integration tests in the existing test structure.
- Run the smallest relevant test set first.
- Then run broader tests/build validation when practical.

Do not claim a task is complete if the relevant tests/build have not been run or if a known failure remains unexplained.

Default commands (adjust if the repo's actual scripts/CI differ — check for a `Makefile`, `azure-pipelines.yml`, or CI config first):
- Build: `dotnet build`
- Run a single test project: `dotnet test <path-to-test-project>`
- Run full test suite: `dotnet test`

## 9. Superpowers

When Superpowers is installed, use its skills rather than inventing a parallel workflow.

Prefer the current namespaced skills, for example:

- `superpowers:brainstorming`
- `superpowers:writing-plans`
- `superpowers:test-driven-development`
- `superpowers:systematic-debugging`
- `superpowers:subagent-driven-development`
- `superpowers:requesting-code-review`
- `superpowers:receiving-code-review`
- `superpowers:verification-before-completion`
- `superpowers:finishing-a-development-branch`

Do not rely on obsolete Superpowers slash commands such as `/brainstorm`, `/write-plan`, or `/execute-plan`.

For small, obvious changes, avoid unnecessary ceremony. For larger or risky changes, use the full workflow.

## 10. claude-mem

When claude-mem is available, use relevant project memory to avoid repeating previously established decisions.

Do not treat memory as a substitute for inspecting the current code. The current repository is the source of truth for implementation details.

## 11. Communication

Respond in the same language the user writes in for that message (Vietnamese or English); code, identifiers, and commit messages stay in English regardless.

Before implementation of a non-trivial task, provide:

### Planned changes
- CREATE: `path/to/file`
- MODIFY: `path/to/file`
- DELETE/RENAME: only if required

Then implement.

If the user has not approved a proposed new file or destructive change, ask for approval before performing that change.

At completion, always report:

### Created
- `...` or `None`

### Modified
- `...` or `None`

### Deleted/Renamed
- `...` or `None`

### Tests / Validation
- commands run
- result

### Notes
- important decisions
- remaining issues, if any

Keep the final report concise.

## 12. Scope rule

If a request can be satisfied without creating a new file, prefer modifying an appropriate existing file.

If multiple implementation approaches are possible and one requires significantly more files or architectural changes, prefer the smaller approach unless there is a clear technical reason not to.

When requirements are ambiguous and the ambiguity could change the implementation, ask a focused clarification instead of guessing.
