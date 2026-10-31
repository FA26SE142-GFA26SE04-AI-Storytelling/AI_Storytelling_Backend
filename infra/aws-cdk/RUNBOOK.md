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
   though the task definition's image reference string doesn't change) and records the ID of the
   deployment it started. It then **follows that deployment ID until ECS settles it** (every 15 s, up to
   15 minutes): it must stay `PRIMARY` and reach `COMPLETED` with at least one running task; `FAILED`, a
   rollback or a replacement fails the job. Finally it checks that
   `https://api.taletale.site/health` returns 200 through the Cloudflare Tunnel. On failure it prints
   the latest ECS service events.

   `services-stable` on its own is not proof of a good deploy: it also returns after ECS has rolled a
   failed deployment back, or when the service has settled on zero running tasks, and it can return a few
   seconds before `rolloutState` becomes `COMPLETED` (the first run of the stricter check on 2026-10-31
   failed on exactly that, although the deploy was fine). Until 2026-10-31 the
   workflow stopped there, so every deploy between 2026-10-16 and 2026-10-31 reported success while
   production was down. The service's deployment circuit breaker (enabled, with rollback) stops a
   crash-looping deployment after a few failed tasks and returns to the previous task set; the
   verification step turns that rollback into a failed workflow run.

   Runs are serialized (`concurrency`), so two merges in quick succession deploy one after another.

ECS has no equivalent of App Runner's `AutoDeploymentsEnabled` — pushing a new `:latest` tag to
ECR does nothing by itself; the `update-service --force-new-deployment` call above is the only way
a deploy happens.

**Public entry point is a Cloudflare Tunnel, not a load balancer.** The task's security group has
no ingress rule, so the task's (changing) public IP cannot be reached directly; smoke-test through
the public hostname instead (`curl https://api.taletale.site/health`). See "Cloudflare Tunnel"
below for setup and troubleshooting.

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

## Alarm: no running API task

CloudWatch alarm `storyplatform-core-api-no-running-task` goes to ALARM when the service has had no
running task for 5 minutes (crash loop, failed deployment, or desired count 0 — so it also fires when
the stack is paused on purpose) and back to OK once a task runs again. It notifies the SNS topic
`storyplatform-core-api-alarms` (`ApiAlarmTopicArnOutput`) on both transitions.

E-mail subscriptions are managed outside CDK, so no address is committed and a `cdk deploy` never
drops them. To subscribe (AWS then sends a confirmation e-mail — click the link in it):

```bash
aws sns subscribe --topic-arn "$(aws cloudformation describe-stacks --stack-name StoryPlatformCoreStack   --query "Stacks[0].Outputs[?OutputKey=='ApiAlarmTopicArnOutput'].OutputValue" --output text   --region ap-southeast-1 --profile storyplatform)"   --protocol email --notification-endpoint <your-email> --region ap-southeast-1 --profile storyplatform
```

List or remove subscriptions with `aws sns list-subscriptions-by-topic` / `aws sns unsubscribe`.

## Cloudflare Tunnel (public entry point)

`api.taletale.site` reaches the API through a remotely-managed Cloudflare Tunnel: the
`CloudflaredContainer` sidecar (image pinned in `StoryPlatformCoreStack.CloudflaredImage`, pulled
from Docker Hub) dials out to Cloudflare and forwards requests to `http://localhost:8080` in the same
task. There is no ALB, listener or target group, and no inbound rule on the task. The sidecar only
starts once `ApiContainer` is healthy, and during a rolling deploy the old and new tasks both hold a
connector on the same tunnel, so a deploy causes no downtime.

The API reads the visitor IP from `CF-Connecting-IP` and only trusts it from loopback
(`ForwardedHeaders__*` env vars in the task definition), so the rate limiter counts real client IPs.

**One-time setup (before the first deploy that contains the sidecar):**

1. Cloudflare dashboard → Zero Trust → Networks → Tunnels → *Create a tunnel* → *Cloudflared*,
   name it e.g. `storyplatform-core-api`. Copy the tunnel token shown in the install command
   (the long value after `--token`); treat it as a secret.
2. In the tunnel, add a *Public hostname*: `api.taletale.site` → type `HTTP`, URL `localhost:8080`.
   If DNS still has the old `api` CNAME (pointing at the deleted ALB), delete it first so Cloudflare
   can create the tunnel's CNAME (`<tunnel-id>.cfargotunnel.com`, proxied).
3. Add the token as a new `CloudflareTunnelToken` key in `storyplatform/core/app-secrets`, using the
   same read-edit-write steps as in the next section. **Do this before `cdk deploy`**: if the key is
   missing, ECS cannot inject `TUNNEL_TOKEN` and the task fails to start (`ResourceInitializationError`).
   The key is deliberately not in the CDK-generated secret template — changing that template would
   make CloudFormation regenerate the whole secret (new `JwtSecretKey`, API keys reset to placeholders).
4. `cdk diff --profile storyplatform`, review, then `cdk deploy --profile storyplatform`.
5. Verify: the tunnel shows *Healthy* in the dashboard, `curl https://api.taletale.site/health`
   returns 200, and the `cloudflared/...` log streams in `/ecs/storyplatform-core-api` show
   "Registered tunnel connection".

**Troubleshooting:** Cloudflare error 1033 = no connector is online (task not running, or the
sidecar cannot authenticate — check the token and the `cloudflared` log stream). Error 502 = the
connector is up but `localhost:8080` did not answer (API container unhealthy or crashed).
To rotate the token, refresh it in the dashboard, update the secret key, then force a new deployment.

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
