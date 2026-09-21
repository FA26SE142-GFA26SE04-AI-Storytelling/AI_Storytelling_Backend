# CI/CD Pipeline Hardening — Test Gate, Auto-Migration, Deploy Verification

**Date:** 2026-09-21
**Status:** Approved by user (design discussion), pending spec review
**Scope:** Backend Core API (`StoryPlatform.Api`) only. `StoryPlatform.AI.Api` is out of scope (per prior user instruction, this session). Builds directly on `docs/superpowers/specs/2026-09-19-aws-core-api-deployment-design.md`, closing its §14 follow-up item "Design and implement EF Core migration application strategy against RDS."

## 1. Goal

Harden the existing CI/CD pipeline (`.github/workflows/deploy-core-api.yml` + App Runner auto-deploy) so that every push to `dev` or `main`:

- Is blocked from deploying if tests fail.
- Automatically applies pending EF Core migrations to RDS — no manual `dotnet ef database update` step.
- Gives a clear pass/fail signal for the actual App Runner deployment in the GitHub Actions log, instead of relying silently on implicit ECR-triggered auto-deploy.

All within the existing **single shared environment** topology (both `dev` and `main` deploy to the same App Runner service/RDS instance — user re-confirmed this in this session's Q&A) and without automating `cdk deploy` (infra changes stay manual, per user instruction and the harness's own IaC-apply protection).

## 2. Non-goals (explicitly out of scope)

- Multi-environment split (staging/prod) — user explicitly re-confirmed single environment.
- Branch protection changing `main`'s role — user explicitly wants both `dev` and `main` to auto-deploy as today; no "main is passive backup" gating.
- Automating `cdk deploy` / infra changes in CI — stays a manual, human-run step.
- Deploying `StoryPlatform.AI.Api`.
- Manual approval gate before deploy (e.g. GitHub Environments with required reviewers) — user wants full automation, not a promotion gate.
- Notifications (Slack/Discord on deploy success/fail) — not requested; noted as an easy future addition in §11.

## 3. Context (confirmed from repo inspection)

- Current `deploy-core-api.yml`: single job (`build-and-push`), no test step, triggers on push to `dev`/`main` path-filtered to `src/Core/**`, `src/Shared/**`, the workflow file. Builds + pushes `:sha` and `:latest` to ECR. No explicit App Runner deploy call — relies entirely on `AutoDeploymentsEnabled: true`.
- Separately, `dotnet-ci.yml` runs `dotnet build`/`dotnet test` on **PR to `main` only** — does not gate direct pushes to `dev`, and does not gate the deploy workflow at all.
- **No EF Core auto-migration**: `ApplicationDbContext.Database.Migrate()` is not called anywhere in `Program.cs`. RDS was provisioned empty per the prior spec's explicit non-goal.
- **RDS is not reachable from GitHub Actions runners**: `Vpc` subnet for RDS is `PRIVATE_ISOLATED`, `PubliclyAccessible = false` (`StoryPlatformCoreStack.cs`). This rules out running `dotnet ef database update` directly from a GitHub-hosted runner — confirmed during this session's design discussion, changes the original plan from "run migration in the pipeline" to "run migration at app startup inside the VPC."
- **Health check path mismatch found**: CDK's `HealthCheckConfiguration.Path` is `/index.html` (`StoryPlatformCoreStack.cs:264`), which only returns 200 because Swashbuckle's Swagger UI middleware happens to serve its own `index.html` at the configured `RoutePrefix` (empty string here) — see `Program.cs:59-67`, gated behind `Swagger:Enabled` config in Production. `Program.cs:75` already registers a real ASP.NET Core health endpoint (`app.MapHealthChecks("/health")`) that is currently unused by App Runner. This matters directly for this task: the auto-migration design (§5.2) relies on App Runner's health-check-triggered rollback as its safety net, and that safety net is fragile while wired to an incidental Swagger route instead of the dedicated health endpoint.
- CI role (`storyplatform-core-api-ci`) currently has only `EcrRepository.GrantPullPush` — no App Runner API permissions.
- **`AutoDeploymentsEnabled: true` is already set** (`StoryPlatformCoreStack.cs:224`) — the service already redeploys itself automatically within seconds of any `:latest` push. Adding an *explicit* `apprunner start-deployment` call on top of this (§5.3) without addressing this would race the implicit trigger: whichever fires first could leave the second call rejected with an "already in progress" error, making the explicit call's pass/fail an unreliable signal. Resolved in §5.3/§5.5 by making the explicit call the sole trigger.
- Live App Runner service confirmed `RUNNING` (`arn:aws:apprunner:ap-southeast-1:028718096070:service/storyplatform-core-api/fda923da69b54ce4b6f7f37d67f9cdcf`).
- `.NET 10`, `ApplicationDbContext` in `StoryPlatform.Infrastructure/Persistence/ApplicationDbContext.cs`, registered via `AddInfrastructure()` in `Program.cs:46`.

## 4. Architecture

```
git push (dev|main, paths: src/Core/**, src/Shared/**)
   │
   ▼
GitHub Actions: deploy-core-api.yml
   │
   ├─ job: test
   │    dotnet restore/build/test StoryPlatform.sln
   │    (fails ⇒ workflow stops here, nothing deploys)
   │
   ▼ (needs: test)
   ├─ job: build-and-push
   │    assume CI role via OIDC → ECR login → docker build → push :sha + :latest
   │    (AutoDeploymentsEnabled now FALSE — push alone does not trigger anything)
   │    → aws apprunner start-deployment (explicit, sole trigger) → poll until RUNNING / ROLLBACK
   │      (fails ⇒ job fails, Action shows red X — CI now KNOWS deploy failed)
   ▼
AWS App Runner pulls new image, starts new container
   │  (VPC Connector reachable to RDS — no network change needed)
   ▼
Program.cs: ApplicationDbContext.Database.Migrate() runs at startup
   │  (idempotent — no-op if nothing pending)
   ▼
app.MapHealthChecks("/health") ⇐ App Runner health check now points here
   │
   ├─ healthy ⇒ new revision goes live, old one retired
   └─ unhealthy (crash, failed migration, bad code) ⇒ App Runner auto-rolls back to last good revision, no downtime
```

## 5. Components

### 5.1 Test gate job (`.github/workflows/deploy-core-api.yml`)
New job `test`, reusing the same commands as `dotnet-ci.yml` (`dotnet restore/build/test StoryPlatform.sln`) for consistency. `build-and-push` gets `needs: test`. No AWS credentials needed for this job.

### 5.2 Auto-migration on startup (`src/Core/StoryPlatform.Api/Program.cs`)
After `var app = builder.Build();` and before `app.Run();`, add a startup scope that resolves `ApplicationDbContext` and calls `Database.Migrate()`. Runs inside the App Runner container, which already has VPC Connector reachability to RDS — **no new IAM, no new networking, no CI changes needed for migration itself.**

Accepted risk: if App Runner ever starts more than one instance of the *same new revision* concurrently (deploy-time race), simultaneous `Migrate()` calls could conflict. Given the service's default autoscaling (min instance count observed as 1) and this project's current traffic scale, this is judged low-probability and acceptable for now; §11 notes the upgrade path (a dedicated CI-driven migration step via CodeBuild/ECS-in-VPC) if it becomes a real problem.

### 5.3 Explicit deploy trigger + verification (`.github/workflows/deploy-core-api.yml`)
After `docker push`, add:
1. `aws apprunner start-deployment --service-arn <arn>` → capture `OperationId`.
2. Poll `aws apprunner describe-service` every 15s, up to a 10-minute timeout, until the service status leaves `OPERATION_IN_PROGRESS`.
3. If it lands on `RUNNING` ⇒ job succeeds. If it lands on `ROLLBACK_SUCCEEDED`/`ROLLBACK_FAILED` ⇒ job fails with a clear message (bad migration or bad code was caught by the health check and rolled back). If the poll exceeds the timeout ⇒ job fails with a timeout message (does not assume success).

This turns today's "fire and forget, hope App Runner notices the new tag" into a deploy step whose pass/fail is visible directly in the GitHub Actions run, and — combined with §5.5's `AutoDeploymentsEnabled: false` — makes this call the **one and only** deployment trigger (no race with an implicit ECR-triggered deploy).

### 5.4 Health check path fix (`infra/aws-cdk/src/StoryPlatformCoreStack.cs`)
Change `HealthCheckConfiguration.Path` from `"/index.html"` to `"/health"`. One-line CDK change, requires a `cdk deploy` (manual, per §2) to take effect. Directly load-bearing for §5.2/§5.3's safety guarantees — without it, the rollback safety net is coincidentally wired to Swagger UI instead of a real health check.

### 5.5 IAM + config — CI role additions and disabling implicit auto-deploy (`infra/aws-cdk/src/StoryPlatformCoreStack.cs`)
Inside the existing `if (includeAppRunnerService)` block (once `AppRunnerService` exists):
- Set `SourceConfiguration.AutoDeploymentsEnabled = false` (currently `true`). Prevents the ECR-push-triggered implicit deploy from racing the explicit `start-deployment` call in §5.3 — the explicit call becomes the sole deployment trigger, and its poll result becomes a trustworthy signal.
- Grant `CiRole`: `apprunner:StartDeployment` and `apprunner:DescribeService`, scoped to `AppRunnerService.AttrServiceArn` only (verified as the correct CDK-generated attribute name on `CfnService`).

No change to the trust policy (already fixed this session) or to ECR permissions.

### 5.6 RUNBOOK.md update (`infra/aws-cdk/RUNBOOK.md`)
Add a short section describing the new deploy flow (test gate → explicit `start-deployment` from CI → migration-on-startup) so the runbook doesn't silently go stale and keep describing the old implicit-auto-deploy behavior. The existing two-phase bootstrap section (§"Two-phase deploy") is unaffected and stays as-is — it governs `includeAppRunnerService`, unrelated to this change.

## 6. Data flow (deploy-time, supersedes §8 of the 2026-09-19 spec)

`git push (dev|main)` → GitHub Actions assumes CI role via OIDC → `dotnet test` (gate) → build image → push to ECR (`latest` + SHA tags, no longer auto-triggers anything since `AutoDeploymentsEnabled=false`) → explicit `apprunner start-deployment` (sole trigger) → App Runner pulls new image, starts container → **`Database.Migrate()` runs against RDS over the VPC Connector path** → `/health` check passes → new revision goes live (or fails health check → auto-rollback, CI job reports failure via the poll in §5.3).

## 7. Error handling / rollback

- Test failure ⇒ pipeline stops before any image is built. No change from current App Runner state.
- Build/push failure ⇒ same as today, no partial deploy.
- `start-deployment` call fails immediately (e.g. bad ARN, permission issue) ⇒ job fails fast, App Runner untouched.
- New revision fails health check (bad code **or** failed migration) ⇒ App Runner automatically rolls back to the last good revision — zero manual intervention, zero downtime for end users. The GitHub Actions job also fails (via the §5.3 poll), so the failure is visible to the developer, not silent.
- A migration that succeeds but ships buggy application code is still caught by the health check the same way as any other bad deploy.
- **Known gap, explicitly accepted:** a migration that succeeds but is later found to be functionally wrong (e.g. wrong index, dropped column) is not auto-reverted — EF Core migrations are forward-only here, matching current project practice. Out of scope to build migration-rollback tooling.

## 8. Security considerations

- No new long-lived credentials. CI role permissions grow by exactly 2-3 App Runner read/deploy actions, scoped to the one service ARN.
- Migration execution happens inside the already-trusted App Runner instance role / VPC path — no new attack surface (no new network path opened, no new secret introduced).
- `/health` endpoint (via `AddHealthChecks()`/`MapHealthChecks`) returns only overall health status, no sensitive details — safe to expose without auth, consistent with typical ASP.NET Core health check usage.

## 9. Testing / validation plan

- `dotnet test StoryPlatform.sln` locally before pushing the `Program.cs` change, to confirm nothing else broke.
- After implementation: push a trivial no-op change to `dev` touching `src/Core/**`, confirm in the Actions log: test job passes → image builds/pushes → `start-deployment` call succeeds → poll reports `RUNNING`.
- Manually verify `/health` returns 200 on the live App Runner URL after the CDK health-check-path fix is deployed.
- Negative case (broken migration) is validated **locally**, not against the shared AWS environment: use the repo's existing `compose.yaml`/`Dockerfile` (containerized local dev setup, merged from `main` this session) to run the API against a local Postgres container, intentionally add a migration with a broken operation (e.g. referencing a non-existent column), and confirm `Database.Migrate()` throws, the container fails to become healthy, and the app does not serve traffic. This proves the failure path without ever risking the one real shared environment.

## 10. Decision log (from brainstorming Q&A, this session)

| Decision | Choice | Rationale |
|---|---|---|
| Environment topology | Single environment, unchanged | User re-confirmed; matches prior spec |
| `dev`/`main` deploy relationship | Both auto-deploy, no "main is passive" gating | User explicitly chose to keep current behavior and manage merge order themselves, after being shown the collision risk |
| Migration strategy | Auto-migrate on App Runner startup (`Program.cs`), not a CI pipeline step | Discovered RDS is network-isolated from GitHub-hosted runners (`PRIVATE_ISOLATED`, not publicly accessible) — a CI-driven migration step cannot reach the DB at all without new VPC-attached CI infra (rejected as over-engineering for current scale) |
| Test gate | Added before build/push | User confirmed direct pushes to `dev` currently bypass all testing; unacceptable given `dev` now auto-deploys |
| Deploy signal | Explicit `apprunner start-deployment` + poll, instead of relying silently on implicit ECR-tag-triggered auto-deploy | User wants clear pass/fail visibility in CI, not silent hope |
| `AutoDeploymentsEnabled` | Disabled (`false`), was `true` | Self-review found the explicit `start-deployment` call would race the existing implicit ECR-triggered auto-deploy if left enabled, making the poll result unreliable. Disabling it makes the explicit call the sole trigger — no functional loss, since CI is already the only thing that ever pushes `:latest` |
| Health check path | Fix `/index.html` → `/health` | Found during context-gathering for this spec; directly undermines the rollback safety net this design depends on if left as-is |
| Infra automation (`cdk deploy`) | Stays manual | User instruction this session ("hạ tầng vẫn deploy thủ công"); also independently enforced by the harness's own protected-scope IaC-apply restriction encountered earlier this session |

## 11. Follow-up work (not in this task)

- If migration risk/complexity grows, revisit: a dedicated CI-driven migration step via a short-lived CodeBuild project or ECS Fargate task inside the VPC, decoupling migration execution and its logs from app startup and eliminating the concurrent-instance race entirely.
- Slack/Discord (or similar) notification on deploy success/failure — not requested, cheap to add on top of the §5.3 poll result once desired.
- Deploy `StoryPlatform.AI.Api` (still out of scope, carried over from the prior spec).
