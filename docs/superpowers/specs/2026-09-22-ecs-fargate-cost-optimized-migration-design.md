# Migrate Core API off App Runner to Cost-Optimized ECS Fargate (+ Redis Cache)

**Date:** 2026-09-22
**Status:** Approved by user, pending implementation plan
**Scope:** Replace the App Runner compute layer for `StoryPlatform.Api` (Core backend) with ECS Fargate (ARM64, cost-optimized), eliminate the VPC's NAT Gateway, consolidate Secrets Manager to 1 secret, and add Redis caching via an **external managed provider** (e.g. Upstash or Redis Cloud free tier — not self-hosted on AWS) for general data caching. RDS and ECR are structurally unchanged. `StoryPlatform.AI.Api` is out of scope.

## 1. Goal

Reduce the Core API's AWS spend to fit a **$190 / 5-month** budget (AWS Free Plan credit), while:

- Staying off the compute path (App Runner + VPC Connector) that structurally forces a NAT Gateway, the single largest cost driver (~57% of current spend).
- Keeping RDS PostgreSQL private (no public endpoint) — same security posture as today.
- Preserving all existing external integrations (Vertex AI/Gemini, Resend, SePay, Supabase) which require outbound internet access from the container.
- Making the change reversible / low-risk given there are **no real users yet** on the current App Runner service.

This is a cost-optimization migration, not a response to an AWS-imposed deadline: App Runner stopped accepting *new* customers on 2026-04-30, but this account's existing service (`storyplatform-core-api-v2`) is unaffected and keeps working indefinitely. The migration is driven entirely by the budget finding below.

A general-purpose Redis cache is added as part of this task (not a separate follow-up) because introducing it later, after the compute layer has already moved once, would mean touching the same CDK stack and networking a second time for no reason — it belongs in the same change while the ECS networking model is already being designed from scratch.

**Redis is hosted externally (not self-hosted on AWS)**, reached over plain outbound TLS from the API task — the same integration pattern already used for Supabase, Resend, and SePay (§3). This was a correction made during design review (§13): a self-hosted Redis task in a NAT-less private subnet turned out to have no way to pull its own container image or reach CloudWatch/Service Discovery, and the cheapest fix (moving it to the public subnet with its own public IP) would have pushed the 5-month total to ~$216.5, over budget. Using an external free-tier provider avoids the problem entirely and costs nothing at this scale.

## 2. Non-goals (explicitly out of scope)

- Deciding *what* the app caches or designing per-endpoint cache-invalidation strategy — this task delivers the `IDistributedCache` wiring in `Program.cs` and the external Redis connection only. Which queries/endpoints actually use it is a separate, follow-on application change.
- Redis persistence, backups, or capacity planning beyond the chosen provider's free tier — these are the external provider's concern, not this stack's. Treated the same way Supabase/Resend/SePay's own reliability and limits are already out of this project's control.
- Provisioning any AWS-hosted Redis (ElastiCache, or a self-hosted container) — evaluated and rejected during design review (§13); not part of this task.
- Using Redis for anything beyond general caching (no session store, no rate limiting, no SignalR backplane in this task) — those were considered (see decision log, §13) and deferred as separate future decisions if needed.
- Deploying `StoryPlatform.AI.Api` — unchanged, not part of this task.
- Adding HTTPS / a custom domain — the new design serves plain HTTP over the Fargate task's public IP. Deferred until either real users arrive or a stable endpoint is needed for the SePay webhook (§14).
- Solving the SePay webhook's dynamic-IP problem (Route 53 auto-update, Elastic IP, or ALB) — flagged as a known gap, deferred (§14) since there is no live payment traffic yet.
- Auto-scaling / high availability — single Fargate task, matching the current App Runner default-minimum footprint. Not a regression: the current service also runs effectively as a single instance under this traffic level.
- Changing RDS instance class, Multi-AZ, or storage — unchanged from the existing stack.
- Changing ECR's repository/lifecycle rule, or the app's existing (non-Redis) `Program.cs`/config binding logic — unchanged. (Secrets Manager's *layout* does change — 4 secrets consolidated to 1, §5.4 — but the set of values and how the app reads them via env vars does not.)

## 3. Context (confirmed from repo inspection + real billing data)

- Current stack: `infra/aws-cdk/src/StoryPlatformCoreStack.cs` — VPC (NAT Gateway ×1) → App Runner (`CoreApiServiceV2`, VPC Connector, 1 vCPU/2GB) → RDS `db.t4g.micro` (private isolated subnet) + 4 Secrets Manager secrets + ECR.
- CI/CD: `.github/workflows/deploy-core-api.yml` — builds/pushes image to ECR, then explicitly calls `aws apprunner start-deployment` and polls `list-operations` until `SUCCEEDED`/`FAILED`.
- Real cost data pulled via `aws ce get-cost-and-usage` (profile `storyplatform`, account `028718096070`, region `ap-southeast-1`) for the 3 full days since the stack went live (2026-09-19 to 2026-09-21):

  | Service | Avg $/day | Projected $/month |
  |---|---|---|
  | NAT Gateway (hourly, `EC2 - Other`) | $1.15 | ~$34.5 |
  | NAT Gateway (data processing, `VPC`) | $0.10 | ~$2.9 |
  | RDS PostgreSQL | $0.55 | ~$16.4 |
  | App Runner (compute) | $0.36 | ~$10.8 |
  | Secrets Manager | $0.05 | ~$1.6 |
  | ECR / S3 (negligible) | ~$0 | ~$0 |
  | **Total** | **~$2.21** | **~$66** |

  All of this "Usage" cost is currently offset dollar-for-dollar by "Credit" records (confirmed via `RECORD_TYPE` grouping) — the AWS Free Plan credit is absorbing it, so the account is not yet being charged out of pocket. But at a ~$66/month burn rate, a $190–200 credit pool lasts **~2.9 months**, not the user's target of 5 months.
- NAT Gateway is the dominant cost (~57%) and is **structurally required** by the current design: App Runner's VPC Connector routes 100% of the container's egress through the VPC, and App Runner tasks have no "assign a public IP directly" option the way Fargate `awsvpc` networking does. There is no way to keep App Runner + a private RDS + outbound internet access without a NAT Gateway (or an equivalent NAT instance, not evaluated further — see decision log §13).
- ECS Fargate tasks, by contrast, can run in a **public** subnet with `assignPublicIp: true`, giving each task its own public IP for outbound (and inbound, since there's no ALB) traffic — while still reaching RDS over the VPC's internal routing via security groups. This removes the NAT Gateway entirely.
- Verified real AWS pricing for `ap-southeast-1` (via aws.amazon.com pricing pages, region selector set to Asia Pacific (Singapore)):
  - Fargate Linux/X86: $0.05056 / vCPU-hour, $0.00553 / GB-hour.
  - NAT Gateway: $0.059/hour + $0.059/GB processed.
  - ALB: $0.0252/hour + $0.008/LCU-hour (relevant only if a future design adds one back).
  - RDS `db.t4g.micro` PostgreSQL Single-AZ: $0.025/hour (matches observed real billing).
- App Runner's own pricing page (aws.amazon.com/apprunner/pricing) does **not** publish a rate for `ap-southeast-1` at all — only US East/West, EU Ireland, and Tokyo are listed. This was a contributing reason to prefer moving off App Runner rather than trying to further optimize its (opaquely-priced, in this region) compute.
- Core API has no runtime AWS SDK usage (`grep` for `AWSSDK.`/`Amazon.SecretsManager`/`Amazon.S3` under `src/Core` returns nothing) — secrets are injected as env vars by the container platform (App Runner today, ECS's task execution role after this change), not fetched by the app itself. This means the new **ECS task role** needs no custom permissions; only the **task execution role** needs `secretsmanager:GetSecretValue` + ECR pull + CloudWatch Logs write.
- External integrations confirmed via codebase search, all called over plain outbound HTTPS from the container (none use AWS-specific networking):
  - Google Vertex AI / Gemini (`src/Core/StoryPlatform.Infrastructure/AI/*`)
  - Supabase media storage (`SupabaseMediaStorage.cs`)
  - Resend (`ResendEmailSender.cs`)
  - SePay (`SePayWebhookAuthenticator.cs`, `SePayQrUrlBuilder.cs`) — **also receives inbound webhook calls**, which is the one integration sensitive to the task's public IP changing on every deploy (§14).
- Confirmed via codebase search: **no Redis usage anywhere today** (no `StackExchange.Redis`, no `IDistributedCache` implementation, no cache service). `Program.cs` does call `AddSignalR()` (`StoryPlatform.Api/Hubs/NotificationHub.cs`) with no backplane configured — this works correctly today because the service runs as a single instance; it is **not** a reason this task needs Redis, since the design still runs exactly 1 API task. A SignalR Redis backplane would only become necessary if the API service were ever scaled to more than 1 task, which is out of scope here (§2).
- Verified real ARM64 (Graviton) Fargate pricing for `ap-southeast-1`: $0.04045/vCPU-hour + $0.00442/GB-hour — exactly 20% cheaper than the Linux/X86 rates in the previous bullet. The official .NET 10 container images (`mcr.microsoft.com/dotnet/sdk:10.0`, `mcr.microsoft.com/dotnet/aspnet:10.0`) are multi-arch and support `linux/arm64` natively, so no code changes are needed to run the API on Graviton — only the Docker build platform and the task definition's `RuntimePlatform`.
- **Design-review finding (self-hosted Redis rejected):** an earlier version of this spec placed a self-hosted `redis:7-alpine` container in a `PRIVATE_ISOLATED` subnet with no NAT Gateway, using Fargate Spot. On review this was found to be non-functional as specified: a Fargate task with no NAT and no VPC Interface Endpoints cannot pull its container image (from ECR *or* Docker Hub) or reach the CloudWatch Logs/ECS/Service Discovery control-plane APIs it needs just to start. The cheapest structural fix (moving the task to the `PUBLIC` subnet, like the API task) was still evaluated and rejected — see the next finding.
- **Design-review finding (missing Public IPv4 Address charge):** verified on aws.amazon.com/vpc/pricing that AWS bills **$0.005/hour per in-use public IPv4 address** (~$3.65/month), including auto-assigned Fargate task IPs, across all commercial regions. This charge was missing from every cost estimate in this document up to that point — it applies to the API task's public IP (§5.7) and would have applied a second time to a public-subnet-hosted Redis task. Counting it correctly pushed the self-hosted-Redis design to ~$216.5 over 5 months, over the $190 budget. Switching to an external managed Redis provider removes the second charge entirely and keeps the API task's own (now correctly counted) charge as the only one.
- Given the above, Redis is instead reached as a new **external integration**, alongside the existing Vertex AI/Gemini, Supabase, Resend, and SePay integrations (previous bullet) — a managed free-tier provider (e.g. Upstash or Redis Cloud) over TLS, requiring no AWS networking changes and no additional AWS spend at this scale.

## 4. Architecture

```
GitHub (push: dev or main)
   │  OIDC token
   ▼
GitHub Actions workflow
   │  assumes IAM Role (scoped: this repo + dev/main branches only)
   ▼
Build Docker image (ARM64) ──push──► Amazon ECR repo (storyplatform-core-api)
                                   │
                                   │ explicit: aws ecs update-service --force-new-deployment
                                   ▼
                         Amazon ECS Service — API (Fargate, on-demand, 1 task, ARM64)
                                   │  Public subnet, task has its own public IP
                                   │  reads secret via task execution role
                                   │  no NAT Gateway, no ALB
                                   │
                    ┌──────────────┼───────────────────────┬──────────────────┐
                    ▼              ▼                        ▼                  ▼
        Amazon RDS PostgreSQL  AWS Secrets Manager   External Redis      Vertex AI / Supabase /
        (private isolated,     (1 consolidated       (Upstash or         Resend / SePay
         SG: ECS task SG only)  secret: DB conn        Redis Cloud,       (existing external
                                 string, JWT key,       free tier, TLS,    integrations, unchanged)
                                 Resend key, SePay      outbound only,
                                 key, Redis conn URL)   same pattern)
```

All AWS resources remain in the single existing CDK stack: `StoryPlatformCoreStack`. Redis is not an AWS resource and is not managed by this stack — only its connection URL (stored as a Secrets Manager key) is.

## 5. Components

### 5.1 VPC (modified)
- `NatGateways = 0` (removed).
- Subnet groups: `PUBLIC` (for the API task's public IP) and `PRIVATE_ISOLATED` (for RDS, unchanged). The `PRIVATE_WITH_EGRESS` subnet group is removed — nothing uses it once the VPC Connector is gone, and a subnet of that type with no NAT would have no route out anyway. Redis is not in this VPC at all (§5.8).
- 2 AZs (unchanged).

### 5.2 RDS PostgreSQL (unchanged)
- `db.t4g.micro`, Single-AZ, 20GB, `PRIVATE_ISOLATED` subnet group.
- Security group: inbound TCP 5432 allowed only from the API task's security group (replaces the old rule allowing the VPC Connector's security group).

### 5.3 ECR (unchanged)
- Same repository, same lifecycle rule (max 10 images). Holds the API's own image only — Redis is an external service (§5.8), not a container this project builds, runs, or stores an image for.

### 5.4 AWS Secrets Manager (consolidated)
- **Changed from 4 secrets to 1**: `storyplatform/core/app-secrets`, a single JSON-valued secret with **5 keys** (`DbConnectionString`, `JwtSecretKey`, `ResendApiKey`, `SePayApiKey`, `RedisConnectionString`). ECS's `secrets` mapping supports referencing a specific JSON key within one secret (`valueFrom: <secretArn>:<jsonKey>::`), so each container env var still resolves to its own value — no application code change needed, only the CDK construct and the task definition's secret references.
- Same generation strategy per key as before (RDS-composed connection string, `GenerateSecretString` for the JWT key, placeholders for Resend/SePay/Redis pending manual post-deploy update — the Redis provider's connection URL/token is created outside AWS, same as the Resend/SePay API keys, then pasted in via `aws secretsmanager put-secret-value` post-deploy).
- Cost: ~$0.40/month (1 secret, regardless of key count) instead of ~$1.60/month (4 secrets).
- Read access: the API task's execution role only.

### 5.5 ECS Cluster
- New `Cluster` construct, Fargate capacity only (no EC2 capacity providers). Hosts the API service only — Redis is external (§3), not an ECS workload.

### 5.6 ECS Task Definition — API
- `FargateTaskDefinition`: `RuntimePlatform = { CpuArchitecture: ARM64, OperatingSystemFamily: LINUX }`, `Cpu = 256` (0.25 vCPU), `MemoryLimitMiB = 1024` (1GB) — smallest practical size for this .NET API at current (near-zero) traffic; documented as easy to bump to 512/1024 (0.5 vCPU/1GB) later purely by changing these two numbers, no architecture change, if demo-day load needs more headroom.
- Container: image `<ecr-repo-uri>:latest` (built for `linux/arm64`), port mapping 8080 (matches existing `ASPNETCORE_URLS=http://+:8080` in the Dockerfile — unchanged).
- Environment variables: same plaintext vars as today (`ASPNETCORE_ENVIRONMENT`, `Swagger__Enabled`, `JwtSettings__Issuer`, etc.) — unchanged; the Redis connection string is a **secret**, not a plaintext env var (next bullet), since it includes an auth token/password issued by the external provider.
- Secrets: the 1 consolidated secret (§5.4), each container env var — including the new `Redis__ConnectionString` — mapped to its own JSON key via `secrets`.
- Logging: `awslogs` driver to a new `LogGroup` with **7-day retention**, log level `Warning` in `appsettings.Production.json` (tightened from the original 14-day/default-verbosity plan — see decision log §13) — unlike App Runner's auto-managed logs, ECS requires retention to be set explicitly or it defaults to "never expire," which would grow storage cost indefinitely.
- **Container health check** (found missing during design review, §13): the task definition must declare an explicit `HealthCheck` (e.g. a `curl`/`wget` against the existing `/health` endpoint, same one App Runner's own health check already uses per the RUNBOOK). Without this, ECS only checks whether the container process is alive, not whether the app is actually serving requests — which would silently weaken the rolling-deployment safety property described in §9.
- **Task execution role** (new, replaces `AppRunnerInstanceRole` + `AppRunnerEcrAccessRole`): assumed by `ecs-tasks.amazonaws.com`, grants `secretsmanager:GetSecretValue` on the 1 consolidated secret's ARN, ECR pull on the one repo, and CloudWatch Logs write on the new log group (the standard `AmazonECSTaskExecutionRolePolicy` managed policy covers ECR + Logs; secrets read is added explicitly).
- **Task role**: left with no custom policies — confirmed the app makes no runtime AWS SDK calls (§3), so nothing beyond the execution role's job (pulling the image, injecting the secret, shipping logs) is needed.

### 5.7 ECS Service — API
- `FargateService`, `DesiredCount = 1` (matches current single-instance footprint), **standard (on-demand) Fargate capacity** — not Spot, since this is the user-facing service and stability is preferred over the extra savings (§13).
- `AssignPublicIp = true`, placed in the `PUBLIC` subnet group.
- New security group: inbound TCP 8080 from `0.0.0.0/0` (no ALB in front, so the app itself is the public-facing listener — same exposure model as App Runner's public default domain, minus the automatic HTTPS termination); outbound allowed to the internet (covers all external calls — Vertex AI, Supabase, Resend, SePay, and now Redis, §5.8 — no per-destination security group rules needed since none of them are AWS-networked resources).
- Deployment: **no** auto-deployment trigger (ECS has no direct equivalent to App Runner's `AutoDeploymentsEnabled`); the CI workflow explicitly forces a new deployment after each image push (§7), mirroring the explicit `start-deployment` + poll pattern already used for App Runner today.

### 5.8 External Redis (new — not an AWS resource)
- Provider: a managed Redis-compatible service with a usable free tier, e.g. **Upstash** or **Redis Cloud** — final provider choice is an implementation-plan detail, not fixed by this spec (either fits the same wiring: a `rediss://` TLS connection string).
- Reached directly from the API task over the internet (the API task already has a public IP and outbound access, §5.7) — the exact same integration shape as Supabase/Resend/SePay (§3), not a new networking pattern.
- Connection string/token stored as the `RedisConnectionString` key in the consolidated Secrets Manager secret (§5.4) — never in plaintext env vars or committed config, since it carries an auth credential.
- No AWS resource is created for Redis itself; it is provisioned manually (or via the provider's own API/Terraform, out of scope here) outside this CDK stack, the same way Supabase/Resend/SePay accounts already are.
- **Before relying on this in production-like use**, the chosen provider's free-tier limits (request/command quota, max connections, data size) must be checked against expected traffic — flagged as a follow-up (§14), not a blocker for this task at current (near-zero) traffic.

### 5.9 GitHub OIDC + IAM Role (CI identity) — modified
- OIDC provider and trust policy: unchanged (still scoped to this repo, `dev`/`main` branches).
- Permissions: remove `apprunner:StartDeployment` / `DescribeService` / `ListOperations`; add `ecs:UpdateService` and `ecs:DescribeServices`, scoped to the API service's ARN (the only ECS service this stack creates — Redis is external, §5.8, and never touched by CI). The task definition keeps referencing its image as `:latest` (unchanged tag, same as today's App Runner setup), so routine deploys only need `--force-new-deployment` to re-pull the image — no `ecs:RegisterTaskDefinition` / `iam:PassRole` needed in CI, since the CI workflow never registers a new task definition revision (that only happens via `cdk deploy` when the task definition itself changes).

## 6. Dockerfile

`src/Core/StoryPlatform.Api/Dockerfile` itself is unchanged (still produces a container listening on 8080 via `ASPNETCORE_URLS=http://+:8080`) — the only change is **how it's built**: targeting `linux/arm64` instead of the default x86_64, to match the ARM64 Fargate task (§5.6). The `mcr.microsoft.com/dotnet/sdk:10.0` and `mcr.microsoft.com/dotnet/aspnet:10.0` base images are multi-arch, so no `FROM` line changes are needed — only the build command's target platform (§7). No Dockerfile is needed for Redis — it is an external service (§5.8), not a container this project builds or runs.

## 7. CI/CD (`.github/workflows/deploy-core-api.yml`, modified)

Replaces the App Runner-specific steps only; touches the API service only (Redis is not part of this workflow):

1. Checkout, OIDC auth, ECR login — unchanged.
2. `docker build` — add `--platform linux/arm64` (requires `docker/setup-qemu-action` or `docker buildx` on the GitHub-hosted x86 runner, since it cross-builds for ARM64); push both `:sha` and `:latest` tags — otherwise unchanged.
3. **New:** force the ECS service to redeploy with the freshly-pushed `:latest` image:
   ```
   aws ecs update-service --cluster <cluster> --service <service> --force-new-deployment
   ```
   (`--force-new-deployment` makes ECS pull `:latest` fresh even though the task definition's image reference string doesn't change — same effect as App Runner's `start-deployment`.)
4. **New:** wait for stability instead of polling `list-operations`:
   ```
   aws ecs wait services-stable --cluster <cluster> --services <service>
   ```
   A timeout/failure here fails the GitHub Actions job, same guarantee as today's explicit polling loop (a bad deploy is never silent).

## 8. Data flow

**Deploy-time:** `git push (dev|main)` → GitHub Actions assumes CI role via OIDC → build ARM64 image → push to ECR (`latest` + SHA tags) → workflow calls `ecs update-service --force-new-deployment` → ECS starts a new API task from `:latest`, waits for it to pass its health check before draining the old one → workflow polls `ecs wait services-stable` until the new task is running (or the job fails) → new task reads the consolidated secret (including the Redis connection string) via its task execution role → connects to RDS over the VPC's internal routing (same subnet-to-subnet path as before, now via security group reference instead of VPC Connector).

**Runtime (cache read/write):** API task connects directly to the external Redis provider over TLS (`rediss://`), the same way it already calls Supabase/Resend/SePay — no AWS-side routing or service discovery involved. On a cache miss, or whenever the external provider is briefly unreachable (network blip, provider-side maintenance), the `IDistributedCache` call must fail fast (connection timeout short enough not to visibly delay a request) and the caller falls back to its normal data path (RDS query, external API call). **This fallback behavior is a requirement of the `Program.cs` wiring in this task, not an automatic property of adding Redis** — it is what makes an external, best-effort cache provider (§5.8) an acceptable choice at all, since the app must never treat a cache failure as a request failure.

## 9. Error handling / rollback

- **API deploys**: ECS's default rolling deployment (minimum healthy percent 100%) keeps the previous task running until a new one passes its health check, so a crash-looping new task does not take down the service — the old task stays up. However, unlike App Runner, ECS does **not** automatically revert the service's *active task definition* back to the last-known-good revision when the new one keeps failing; it just keeps retrying the new revision indefinitely while the old task continues serving traffic. This is a real behavior difference from today: for now, a bad deploy requires a manual `aws ecs update-service --task-definition <previous-revision>` to actually resolve the stuck deployment, since there are no real users yet and this manual step is acceptable at current scale. (ECS does offer an opt-in "deployment circuit breaker with rollback" feature that would automate this — not enabled in this task, tracked as a possible follow-up.)
- **Redis unavailability** (provider-side outage, network blip, or free-tier quota briefly exceeded): out of this stack's control entirely, since Redis isn't an AWS resource this task manages. Because the app treats Redis as a soft dependency (previous paragraph in §8), this produces a brief period of cache misses, never an API outage — there is nothing to roll back on the Redis side.
- If OIDC auth or the ECR push fails, the workflow fails before `update-service` is ever called — no partial state, same as today.
- RDS is untouched by this workflow, same as today.

## 10. Security considerations

- No change to CI authentication model (OIDC, scoped role).
- RDS keeps no public endpoint; the security group reference simply moves from the VPC Connector's SG to the API task's SG.
- **External Redis**: connection is outbound-only from the API task (no inbound rule needed, no AWS resource to expose), and must use TLS (`rediss://`) since the connection string carries an auth credential over the public internet, same requirement as the existing Resend/SePay integrations. AWS-side blast radius is limited to whatever's in the connection string itself — there's no IAM role, security group, or VPC exposure to reason about, because there's no AWS resource.
- **New exposure (API)**: the API Fargate task's public IP accepts inbound traffic directly on port 8080 with no load balancer or WAF in front, and **no TLS** — App Runner terminated HTTPS automatically; this design serves plain HTTP. Acceptable for now because there are no real users and no sensitive data flowing yet, but called out explicitly as a downgrade from the current state, to revisit before any real user traffic (§14).
- Secrets Manager consolidation (§5.4) does not change the security model — the same logical values (now 5, including the Redis connection string) are still scoped to the same IAM principal, just addressed as one secret ARN with multiple JSON keys instead of separate ARNs.

## 11. Cost estimate (ap-southeast-1, real pricing where available)

| Component | Configuration | $/month |
|---|---|---|
| Fargate — API | 0.25 vCPU / 1GB, ARM64, on-demand, 1 task, 24/7 | ~$10.6 |
| **Public IPv4 address** | 1 in-use address (the API task's auto-assigned public IP) — verified real rate: $0.005/hour, applies across all commercial regions | **~$3.65** |
| RDS | `db.t4g.micro`, unchanged (real observed: ~$18.0 compute + ~$2.8 storage) | ~$20.8 |
| Secrets Manager | 1 consolidated secret, 5 keys (was 4 separate secrets) | ~$0.4 |
| CloudWatch Logs | 7-day retention, `Warning` level, low volume | ~$1–1.5 |
| ECR | unchanged | ~$0 |
| External Redis (Upstash/Redis Cloud, free tier) | outside AWS billing entirely | $0 |
| ~~NAT Gateway~~ | removed | $0 (was ~$37/month) |
| ~~ALB~~ | not added | $0 |
| **Total** | | **~$36.95/month** |
| **5-month projection** | | **~$184.75** — fits the $190 budget with a **~$5.25 buffer** |

Compare to the current App Runner + NAT design's real measured rate of **~$66/month (~$330 over 5 months)**, which would exhaust the credit in ~2.9 months.

**Note on this revision:** an earlier version of this table (self-hosted Redis on ECS Fargate Spot) omitted the Public IPv4 Address charge entirely and total was understated at ~$36/month. Adding that charge correctly, while also removing the self-hosted Redis compute line (now $0, external), nets out close to the original figure — but the **5-month buffer shrank from ~$10 to ~$5.25 total** (not per month), since the previous "~$10 buffer" was partly an artifact of the missing IP charge, not real headroom. Treat ~$5.25 total (~$1.05/month) as the true remaining margin for data transfer, traffic growth, or estimation error — tighter than it looked before this review, and worth re-checking against real Cost Explorer data (§3's methodology) once this is actually deployed rather than trusting the estimate alone.

## 12. Testing / validation plan

- `cdk synth` — validate the generated CloudFormation template before any real deploy (no NAT Gateway resource, no App Runner resources, one ECS service present, 1 consolidated secret with 5 keys).
- Update `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs` to assert on the new ECS resources instead of the App Runner ones (exact assertions are an implementation-plan concern, not this spec).
- `cdk deploy` — requires explicit user confirmation (creates/destroys billed resources); not run automatically.
- After deploy: confirm the API task reaches `RUNNING` + healthy (via the new container health check, §5.6), hit its public IP on port 8080 for the Swagger/health endpoint, confirm RDS connectivity (existing DB, migrations already applied per the prior deployment's history), confirm at least one of each external integration path (Resend, Vertex AI, Supabase, SePay QR generation, **and Redis**) still succeeds from the new network path.
- **Redis fallback check**: temporarily point `Redis__ConnectionString` at an invalid endpoint (or otherwise make the provider unreachable) while the API is running, and confirm API requests that would normally hit the cache still succeed (falling back to RDS/external calls) — this directly validates the §8/§9 fallback requirement, not just the happy path.
- Confirm the chosen Redis provider's actual endpoint/region is reachable and low-latency from `ap-southeast-1` before committing to it — an external cache with high cross-region latency could make caching a net loss.
- CI workflow validated by pushing a trivial change to `dev`, confirming: OIDC auth succeeds, ARM64 image builds/pushes, `ecs update-service --force-new-deployment` succeeds, `ecs wait services-stable` returns success within a reasonable timeout.
- After validation, decommission the old App Runner service (`storyplatform-core-api-v2`) and its now-unused IAM resources (`AppRunnerInstanceRole`, `AppRunnerEcrAccessRole`, `CfnVpcConnector`) to stop any residual billing — exact teardown sequencing is an implementation-plan concern.

## 13. Decision log (from brainstorming Q&A)

| Decision | Choice | Rationale |
|---|---|---|
| Migrate now vs. wait | Migrate now | Not forced by App Runner's deprecation notice (existing services keep working) — driven entirely by the real ~$66/month burn rate not fitting the $190/5-month budget |
| Target compute | ECS Fargate, no Express Mode/ALB | Real Cost Explorer data showed NAT Gateway (not the App Runner-vs-Fargate compute choice) is 57% of spend; only Fargate's public-subnet task networking eliminates NAT while keeping RDS private. Plain Fargate (no ALB) chosen over Express Mode specifically to avoid the ~$20/month ALB cost, since there's no traffic yet to justify it |
| HTTPS / custom domain | Dropped for now | App Runner provided it for free; replicating it (ALB + ACM, or a reverse proxy) adds cost/complexity not justified before real users exist. Tracked as a known gap (§14) |
| Task size | 0.25 vCPU / 1GB | Smallest Fargate config that fits a .NET 10 minimal API workload with near-zero current traffic; explicitly documented as a one-line bump to 0.5 vCPU/1GB if demo-day load needs it |
| Rollback strategy on bad deploy | Manual `update-service --task-definition <previous>` | ECS doesn't auto-revert like App Runner did; accepted as a manual step given no real users are affected yet |
| SePay webhook dynamic IP | Deferred, not solved in this task | No live payment traffic yet; solving it now (Route 53 auto-update or re-adding an ALB) would add cost/complexity ahead of need |
| Cost verification method | `aws ce get-cost-and-usage` (real billing data) over manual estimation | Manual estimation using published AWS pricing pages overstated App Runner's real cost by ~6.5x (guessed $72/month using Tokyo's published rate as a stand-in, since Singapore isn't listed on App Runner's pricing page at all; real observed cost was ~$11/month) — real Cost Explorer data was materially more accurate and is preferred whenever available |
| Compute architecture | ARM64 (Graviton) for the API task, not x86 | Verified real Singapore pricing is exactly 20% cheaper on ARM ($0.04045/$0.00442 vs $0.05056/$0.00553 per vCPU-hr/GB-hr); .NET 10's official container images are multi-arch, so this is a free performance-neutral win with no code change |
| Secrets Manager layout | 1 consolidated secret (JSON, now 5 keys after Redis was added) instead of 4 separate secrets | Saves ~$1.2/month regardless of key count (Secrets Manager bills per secret, not per key); ECS's `secrets` mapping supports per-key `valueFrom` references into one secret, so no application code change is needed |
| Redis: add now vs. defer | Add now, in this same task | User requested it explicitly; bundling it with the ECS migration avoids touching the same networking/CDK stack a second time later purely to add a cache |
| Redis: purpose | General-purpose data caching only | User confirmed via clarifying question — not session store, not rate limiting, not a SignalR backplane (the app's existing `AddSignalR()` call has no backplane today and doesn't need one at 1 API instance) |
| Redis: hosting model (reversed twice) | **External managed provider** (e.g. Upstash/Redis Cloud free tier) | The choice moved twice: first the user picked self-hosted-on-ECS over Amazon ElastiCache, for cost reasons. Then design review found the self-hosted design technically broken (a `PRIVATE_ISOLATED`, NAT-less Fargate task cannot pull its own image or reach CloudWatch/Service Discovery), and its cheapest fix (moving it to the public subnet) stopped being meaningfully cheaper once the previously-missing Public IPv4 charge (next row) was counted correctly. Going external avoids the networking problem entirely, costs $0 at this scale, and matches the project's existing pattern (Supabase/Resend/SePay are all already external SaaS). User confirmed this final pivot after the review findings were presented |
| Public IPv4 Address charge | Counted correctly: $0.005/hour (~$3.65/month) per in-use public IP, verified on aws.amazon.com/vpc/pricing | Found missing from every cost estimate in this document during design review — applies to the API task's already-planned public IP; would have applied a second time had Redis stayed self-hosted in the public subnet. Correcting it shrank the 5-month buffer from ~$10 to ~$5.25 |
| API container health check | Explicit `HealthCheck` added to the task definition (§5.6), targeting the existing `/health` endpoint | Found missing during design review — without it, ECS's rolling-deployment safety (§9) would only confirm the container process is alive, not that the app is actually serving requests |
| Secrets Manager vs. SSM Parameter Store (re-considered, rejected) | Keep Secrets Manager (1 consolidated secret, §5.4) | Switching to SSM Standard SecureString parameters would save only ~$0.40/month net (RDS's own master-credential secret is a separate, CDK-L2-managed Secrets Manager secret regardless, and cannot move to SSM) — not worth the real cost: CloudFormation cannot create `SecureString` SSM parameters natively, so every value (including the JWT secret and the RDS-composed connection string, both currently auto-generated by CDK) would instead need manual creation via CLI before/around deploy, plus new `ssm:GetParameter`/`kms:Decrypt` IAM permissions. User confirmed keeping Secrets Manager after this trade-off was surfaced |

## 14. Follow-up work (not in this task)

- **SePay webhook stability**: the API Fargate task's public IP changes on every deployment (and likely on task restarts). Before any real payment traffic, decide between: (a) Route 53 hosted zone + a CI step that updates a DNS A record post-deploy (~$0.5/month, requires owning a domain), (b) re-introducing an ALB with a stable DNS name (~$20/month), or (c) accepting manual webhook URL updates at low frequency.
- **HTTPS**: add TLS before any real/sensitive user traffic — likely via the same mechanism chosen for the SePay fix above (ALB with ACM cert, or a domain + reverse proxy like Caddy/Cloudflare in front).
- Bump the API task size to 0.5 vCPU/1GB (or higher) if real usage/demo load shows the 0.25 vCPU config is insufficient — no architecture change required, just task definition values.
- **Decide what the app actually caches** (§2 non-goal) — this task only delivers the infrastructure and generic `IDistributedCache` wiring; picking specific expensive queries/AI calls to cache is separate follow-on work.
- **Pick the specific external Redis provider and verify its free-tier limits** (command/request quota, connection limits, data size) against realistic traffic before this matters — not urgent at near-zero current traffic, but should happen before assuming the cache is reliably available under real load. If usage ever outgrows the free tier, re-evaluate against a paid tier of the same provider before considering AWS-hosted alternatives again.
- Enable ECS's deployment circuit breaker with automatic rollback for the API service, to remove the manual-rollback step noted in §9.
- The 5-month cost buffer is now thin (~$5.25 total, not ~$10) — re-verify against real `aws ce get-cost-and-usage` data (§3's methodology) shortly after this design is actually deployed, rather than trusting the estimate for the full 5 months unchecked.
- Design and implement EF Core migration application strategy against RDS in the new ECS environment (the migration mechanism itself — `Database.Migrate()` at startup — is unchanged by this task, but should be re-verified once running under ECS).
- Deploy `StoryPlatform.AI.Api` (separate stack or extend this one — TBD in a future brainstorming session).
