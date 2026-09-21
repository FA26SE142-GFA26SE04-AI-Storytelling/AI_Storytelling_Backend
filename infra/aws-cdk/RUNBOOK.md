# StoryPlatform Core API — CDK Operational Runbook

Quick reference for operating the `StoryPlatformCoreStack` (account `028718096070`,
region `ap-southeast-1`, AWS CLI profile `storyplatform`). For full design rationale
see `docs/superpowers/specs/2026-09-19-aws-core-api-deployment-design.md` and
`docs/superpowers/plans/2026-09-19-aws-core-api-deployment.md` (Task 13).

## Two-phase deploy (why it exists)

App Runner's `CfnService` references an image tag (`storyplatform-core-api:latest`) in
the stack's own ECR repository. On a **brand-new** stack that repo is empty — no image
has ever been pushed — so creating the App Runner service in the same deploy that
creates the ECR repo would fail to find the image and roll back the *entire* stack
(VPC, RDS, ECR, Secrets, IAM included). To avoid this, App Runner Service creation is
gated behind the `includeAppRunnerService` context flag (default `false` in
`StoryPlatformCoreStack.cs`).

**First-time bootstrap, from `infra/aws-cdk/`:**

```bash
cdk bootstrap aws://028718096070/ap-southeast-1 --profile storyplatform

# Phase 1: VPC, RDS, ECR, Secrets, IAM, OIDC — no App Runner yet
cdk deploy --profile storyplatform --require-approval broadcast

# Push the first image (repo now exists)
aws ecr get-login-password --region ap-southeast-1 --profile storyplatform | \
  docker login --username AWS --password-stdin 028718096070.dkr.ecr.ap-southeast-1.amazonaws.com
docker build --file src/Core/StoryPlatform.Api/Dockerfile --tag \
  028718096070.dkr.ecr.ap-southeast-1.amazonaws.com/storyplatform-core-api:latest .
docker push 028718096070.dkr.ecr.ap-southeast-1.amazonaws.com/storyplatform-core-api:latest

# Phase 2: add App Runner now that a real image exists
cdk deploy --profile storyplatform --context includeAppRunnerService=true --require-approval broadcast
```

After phase 2 succeeds, the flag is persisted in `cdk.json` (`"includeAppRunnerService": true`)
so future `cdk deploy` runs (CI or manual) don't need the `--context` flag and won't
accidentally omit — and thereby delete — the running App Runner service.

## CI/CD deploy flow (as of 2026-09-21)

Every push to `dev` or `main` touching `src/Core/**`, `src/Shared/**`, `Dockerfile`, or the workflow
file itself runs `.github/workflows/deploy-core-api.yml`:

1. **`test` job** — `dotnet build`/`dotnet test` on `StoryPlatform.sln`. A failing test stops the
   pipeline here; nothing is built or deployed.
2. **`build-and-push` job** (`needs: test`) — builds the image from the root `Dockerfile`
   (`--target core-api`, the same target `docker compose` uses locally), pushes `:sha` and `:latest`
   to ECR, then explicitly calls `aws apprunner start-deployment` and polls
   `aws apprunner describe-service` until the service reports `RUNNING` (success) or a
   `ROLLBACK_*`/`CREATE_FAILED` status (failure — the GitHub Actions job fails too, so a bad deploy
   is never silent).

App Runner's `AutoDeploymentsEnabled` is **disabled** — pushing a new `:latest` tag to ECR no longer
triggers anything by itself. The `start-deployment` call above is the only way a deploy happens.

**Database migrations are applied automatically** by the container itself at startup
(`Program.cs` calls `ApplicationDbContext.Database.Migrate()`), not by CI — GitHub-hosted runners
cannot reach RDS (`PRIVATE_ISOLATED` subnet), but the App Runner container already can via its VPC
Connector. If a migration is bad, the container fails its `/health` check and App Runner
automatically keeps serving the last good revision — no manual rollback needed, but also no
automatic *schema* rollback (migrations are forward-only; write a corrective migration instead of
trying to revert one already applied to the shared environment).

## Full teardown-and-redeploy caveat

`cdk destroy` deletes the stack's resources, but the 5 Secrets Manager secrets (4 app
secrets + the RDS-generated master credential) go into Secrets Manager's default
**30-day deletion recovery window** rather than being purged immediately. A subsequent
`cdk deploy` will fail with *"a secret with this name is already scheduled for
deletion"* unless they're force-deleted first:

```bash
aws secretsmanager delete-secret --secret-id storyplatform/core/db-connection-string \
  --force-delete-without-recovery --region ap-southeast-1 --profile storyplatform
aws secretsmanager delete-secret --secret-id storyplatform/core/jwt-secret-key \
  --force-delete-without-recovery --region ap-southeast-1 --profile storyplatform
aws secretsmanager delete-secret --secret-id storyplatform/core/resend-api-key \
  --force-delete-without-recovery --region ap-southeast-1 --profile storyplatform
aws secretsmanager delete-secret --secret-id storyplatform/core/sepay-api-key \
  --force-delete-without-recovery --region ap-southeast-1 --profile storyplatform

# The RDS-generated secret has a random suffix — find it first:
aws secretsmanager list-secrets --region ap-southeast-1 --profile storyplatform \
  --query "SecretList[?starts_with(Name, 'CoreDatabaseSecret')].Name"
# then, for each name returned:
aws secretsmanager delete-secret --secret-id <name-from-above> \
  --force-delete-without-recovery --region ap-southeast-1 --profile storyplatform
```

Also note: after a full `cdk destroy`, `cdk.json`'s persisted
`"includeAppRunnerService": true` means the **next** `cdk deploy` will again try to
create App Runner against an empty ECR repo (same problem as the very first deploy).
For a from-scratch redeploy after a full destroy, repeat the two-phase sequence,
overriding the persisted flag for the first deploy only:

```bash
cdk deploy --profile storyplatform --context includeAppRunnerService=false --require-approval broadcast
# push an image (see above)
cdk deploy --profile storyplatform --require-approval broadcast   # picks up the persisted true
```

## Setting the real Resend / SePay secret values post-deploy

The `resend-api-key` and `sepay-api-key` secrets deploy with a placeholder
(`REPLACE_ME_POST_DEPLOY`). Set the real values manually (never commit real keys):

```bash
aws secretsmanager put-secret-value --secret-id storyplatform/core/resend-api-key \
  --secret-string "<real-resend-key>" --region ap-southeast-1 --profile storyplatform
aws secretsmanager put-secret-value --secret-id storyplatform/core/sepay-api-key \
  --secret-string "<real-sepay-key>" --region ap-southeast-1 --profile storyplatform
```

Then restart the App Runner service so it picks up the new values:

```bash
aws apprunner start-deployment --service-arn <arn> --region ap-southeast-1 --profile storyplatform
```
