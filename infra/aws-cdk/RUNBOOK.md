# StoryPlatform Core API — CDK Operational Runbook

Quick reference for operating the `StoryPlatformCoreStack` (account `028718096070`,
region `ap-southeast-1`, AWS CLI profile `storyplatform`). For full design rationale
see `docs/superpowers/specs/2026-09-19-aws-core-api-deployment-design.md` and
`docs/superpowers/plans/2026-09-19-aws-core-api-deployment.md` (Task 13).

## Two-phase deploy (why it exists)

**Before merging this branch to `dev`/`main`:** the two-phase `cdk deploy` sequence below must be
run manually at least once, through phase 2 (`includeEcsService=true`), before the merge lands.
`.github/workflows/deploy-core-api.yml` triggers on any push touching `infra/aws-cdk/**` but only
calls `aws ecs update-service`/`aws ecs wait services-stable` — it never runs `cdk deploy`. Until
phase 2 has been run by hand, there is no ECS cluster, no ECS service, and the CI role doesn't even
have `ecs:UpdateService` permission (that IAM grant only exists inside the `includeEcsService`
gate), so the first CI run after merge will fail with nothing to deploy to.

The ECS `FargateService` references an image tag (`storyplatform-core-api:latest`) in
the stack's own ECR repository. On a **brand-new** stack that repo is empty — no image
has ever been pushed — so the ECS Service is gated behind the `includeEcsService`
context flag (default `false` in `StoryPlatformCoreStack.cs`), the same two-phase
pattern the old App Runner setup used.

**First-time bootstrap, from `infra/aws-cdk/`:**

```bash
cdk bootstrap aws://028718096070/ap-southeast-1 --profile storyplatform

# Phase 1: VPC, RDS, ECR, Secrets, IAM, OIDC, ECS cluster/task definition — no running service yet
cdk deploy --profile storyplatform --require-approval broadcast

# Push the first image, built for ARM64 (matches the task definition's RuntimePlatform)
aws ecr get-login-password --region ap-southeast-1 --profile storyplatform | \
  docker login --username AWS --password-stdin 028718096070.dkr.ecr.ap-southeast-1.amazonaws.com
docker buildx build --platform linux/arm64 --file Dockerfile --target core-api \
  --tag 028718096070.dkr.ecr.ap-southeast-1.amazonaws.com/storyplatform-core-api:latest \
  --push .

# Phase 2: add the ECS Service now that a real image exists
cdk deploy --profile storyplatform --context includeEcsService=true --require-approval broadcast
```

After phase 2 succeeds, the flag is persisted in `cdk.json` (`"includeEcsService": true`)
so future `cdk deploy` runs (CI or manual) don't need the `--context` flag and won't
accidentally omit — and thereby delete — the running ECS Service.

## CI/CD deploy flow (as of 2026-09-22)

Every push to `dev` or `main` touching `src/Core/**`, `src/Shared/**`, `Dockerfile`, or the workflow
file itself runs `.github/workflows/deploy-core-api.yml`:

1. **`test` job** — `dotnet build`/`dotnet test` on `StoryPlatform.sln`, plus a separate
   `dotnet test` run against `infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj` (the CDK stack
   is not part of the main solution, so it needs its own step). A failing test stops the pipeline
   here; nothing is built or deployed.
2. **`build-and-push` job** (`needs: test`) — builds the image from the root `Dockerfile`
   (`--target core-api`) **for `linux/arm64`** via `docker buildx` (cross-compiled on the
   GitHub-hosted x86 runner, via `docker/setup-qemu-action`), pushes `:sha` and `:latest` to ECR,
   then explicitly calls `aws ecs update-service --force-new-deployment` (re-pulls `:latest` even
   though the task definition's image reference string doesn't change) and
   `aws ecs wait services-stable` to block until the new task is healthy or the job fails — a bad
   deploy is never silent.

ECS has no equivalent of App Runner's `AutoDeploymentsEnabled` — pushing a new `:latest` tag to
ECR does nothing by itself; the `update-service --force-new-deployment` call above is the only way
a deploy happens.

**Finding the running task's public IP** (for manual smoke-testing, since there's no ALB):

```bash
TASK_ARN=$(aws ecs list-tasks --cluster storyplatform-core-api-cluster \
  --service-name storyplatform-core-api-svc --region ap-southeast-1 --profile storyplatform \
  --query 'taskArns[0]' --output text)
ENI_ID=$(aws ecs describe-tasks --cluster storyplatform-core-api-cluster --tasks "$TASK_ARN" \
  --region ap-southeast-1 --profile storyplatform \
  --query 'tasks[0].attachments[0].details[?name==`networkInterfaceId`].value' --output text)
aws ec2 describe-network-interfaces --network-interface-ids "$ENI_ID" \
  --region ap-southeast-1 --profile storyplatform \
  --query 'NetworkInterfaces[0].Association.PublicIp' --output text
```

The public IP changes on every deployment (no static IP without an ALB or Elastic IP) — see the
spec's §14 follow-up on the SePay webhook implication of this.

**Database migrations are applied automatically** by the container itself at startup
(`Program.cs` calls `ApplicationDbContext.Database.Migrate()`), not by CI — GitHub-hosted runners
cannot reach RDS (`PRIVATE_ISOLATED` subnet), but the ECS task already can via a security-group
rule from its own security group (`ApiTaskSecurityGroup` in `StoryPlatformCoreStack.cs`) — there is
no VPC Connector in this setup. If a migration is bad, the container fails its `/health` check, but
unlike App Runner, ECS does **not** automatically revert to the last-known-good task definition —
it keeps retrying the new (bad) revision. Recovering requires a manual rollback:

```bash
aws ecs update-service --cluster storyplatform-core-api-cluster --service storyplatform-core-api-svc \
  --task-definition <previous-revision-arn> --region ap-southeast-1 --profile storyplatform
```

This does **not** mean the database is rolled back too: `Database.Migrate()`
applies pending migrations one at a time, each in its own transaction, so if migration 5 of 8
fails (or the container is killed once the health check's ~50s unhealthy window expires), the
first 4 are already committed. The previous task definition revision you manually roll back to
(per the command above) then runs against a schema partway through a change it was never built
for. Two consequences:

- Write migrations to be additive and backward-compatible with the *previous* revision
  (expand/contract pattern) — never drop or rename a column in the same migration that a currently
  running revision still reads.
- After any failed deploy, check `SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY
  "MigrationId";` against the real database to see exactly how far the migration got before
  writing a corrective migration — migrations are forward-only, so recovery is always a new
  migration, never a revert of one already applied to the shared environment.

## Full teardown-and-redeploy caveat

`cdk destroy` deletes the stack's resources, but the 2 Secrets Manager secrets (1 consolidated app
secret + the RDS-generated master credential) go into Secrets Manager's default
**30-day deletion recovery window** rather than being purged immediately. A subsequent
`cdk deploy` will fail with *"a secret with this name is already scheduled for
deletion"* unless they're force-deleted first:

```bash
aws secretsmanager delete-secret --secret-id storyplatform/core/app-secrets \
  --force-delete-without-recovery --region ap-southeast-1 --profile storyplatform

# The RDS-generated secret has a random suffix — find it first:
aws secretsmanager list-secrets --region ap-southeast-1 --profile storyplatform \
  --query "SecretList[?starts_with(Name, 'CoreDatabaseSecret')].Name"
# then, for each name returned:
aws secretsmanager delete-secret --secret-id <name-from-above> \
  --force-delete-without-recovery --region ap-southeast-1 --profile storyplatform
```

Also note: after a full `cdk destroy`, `cdk.json`'s persisted
`"includeEcsService": true` means the **next** `cdk deploy` will again try to
create the ECS Service against an empty ECR repo (same problem as the very first deploy).
For a from-scratch redeploy after a full destroy, repeat the two-phase sequence,
overriding the persisted flag for the first deploy only:

```bash
cdk deploy --profile storyplatform --context includeEcsService=false --require-approval broadcast
# push an image (see above)
cdk deploy --profile storyplatform --require-approval broadcast   # picks up the persisted true
```

## Setting the real Resend / SePay / Redis secret values post-deploy

`resend-api-key`, `sepay-api-key`, and `redis-connection-string` deploy as placeholder values
(`REPLACE_ME_POST_DEPLOY`) inside the single consolidated `storyplatform/core/app-secrets` JSON
secret. Read the current JSON, edit only the keys you need, and write the whole object back
(Secrets Manager has no partial-JSON-key update — `put-secret-value` always replaces the entire
secret value):

```bash
aws secretsmanager get-secret-value --secret-id storyplatform/core/app-secrets \
  --region ap-southeast-1 --profile storyplatform --query SecretString --output text > /tmp/app-secrets.json
# edit /tmp/app-secrets.json: set ResendApiKey, SePayApiKey, and/or RedisConnectionString
aws secretsmanager put-secret-value --secret-id storyplatform/core/app-secrets \
  --secret-string "file:///tmp/app-secrets.json" --region ap-southeast-1 --profile storyplatform
rm /tmp/app-secrets.json
```

`RedisConnectionString` must be in StackExchange.Redis's native connection-string format:
`<host>:<port>,password=<token>,ssl=True,abortConnect=False` (e.g.
`cute-cat-12345.upstash.io:6379,password=sometoken,ssl=True,abortConnect=False`), using the
host/port/token issued by the external provider (Upstash or Redis Cloud, spec §5.8) — created
outside AWS, the same way the Resend/SePay keys already are. StackExchange.Redis does **not**
accept `redis://`/`rediss://` URI-scheme connection strings — the provider's dashboard may display
one, but it must be converted to the format above before pasting it into the secret, otherwise
`ConfigurationOptions.Parse()` silently produces a broken configuration (wrong host, port 0,
`Ssl=False`, no password) and the cache never works.

Then force a new deployment so the running task picks up the new values (ECS injects secrets at
task start, not live):

```bash
aws ecs update-service --cluster storyplatform-core-api-cluster --service storyplatform-core-api-svc \
  --force-new-deployment --region ap-southeast-1 --profile storyplatform
```
