# AWS Deployment Design — Core API (StoryPlatform.Api)

**Date:** 2026-09-19
**Status:** Approved by user, pending implementation plan
**Scope:** Deploy only `StoryPlatform.Api` (Core backend). `StoryPlatform.AI.Api` is explicitly out of scope for this iteration.

## 1. Goal

Stand up a minimal, cost-conscious, reproducible AWS deployment for the Core API using Infrastructure-as-Code (AWS CDK, C#), so that:

- The Core API runs on AWS reachable over HTTPS.
- Its PostgreSQL database runs on RDS, not publicly reachable.
- Deploys happen automatically from GitHub (`dev` and `main` branches) without long-lived AWS credentials in GitHub.
- The whole environment can be created and torn down with a single stack (`cdk deploy` / `cdk destroy`).

## 2. Non-goals (explicitly out of scope)

- Deploying `StoryPlatform.AI.Api` (AI service) — future task.
- Applying EF Core migrations to RDS — deferred to a follow-up task (separate migration step, to be designed alongside a future CI/CD iteration). This spec's infra produces an **empty** RDS database.
- Custom domain / Route 53 / ACM certificate — App Runner's default HTTPS domain is sufficient for now.
- Multi-environment isolation (dev vs prod stacks) — user explicitly chose a **single shared environment**; both `dev` and `main` branches deploy to the same App Runner service/RDS instance.
- S3 / CloudFront for media — not needed until AI-generated media work begins.
- Bedrock or any AWS AI service — Core/AI already call OpenAI/Anthropic directly via `StoryPlatform.AI.Infrastructure/LLM`; no architecture change requested.

## 3. Context (confirmed from repo inspection)

- `.NET 10.0` (`TargetFramework` in `StoryPlatform.Api.csproj`).
- No existing Dockerfile anywhere in the repo.
- No auto-migration in `Program.cs` (`Database.Migrate()` not called).
- Config is read via standard ASP.NET Core configuration binding; secrets are already expected via double-underscore env vars, e.g.:
  - `ConnectionStrings__DefaultConnection`
  - `JwtSettings__SecretKey` (current appsettings.json value is a known demo/placeholder secret, flagged in GitGuardian per commit `a253add` — must NOT be reused for the real deployment; CDK will provision a freshly generated secret)
  - `ResendSettings__ApiKey` (email, via Resend — `ResendEmailSender.cs`)
  - `SePaySettings__*` (payment webhook, `SePayOptions.cs` / `SePayWebhookAuthenticator.cs`)
- AWS account for this deployment: `028718096070`, IAM user `admin-deploy` (AdministratorAccess, used only for initial manual setup — not for CI), region `ap-southeast-1`, CLI profile `storyplatform`.
- Existing spec convention: `docs/superpowers/specs/YYYY-MM-DD-<topic>-design.md`.

## 4. Architecture

```
GitHub (push: dev or main)
   │  OIDC token
   ▼
GitHub Actions workflow
   │  assumes IAM Role (scoped: this repo + dev/main branches only)
   ▼
Build Docker image ──push──► Amazon ECR repo (storyplatform-core-api)
                                   │
                                   │ AutoDeploymentsEnabled=true
                                   ▼
                         AWS App Runner service (Core API)
                                   │  VPC Connector (private-isolated subnets)
                                   │  reads secrets via RuntimeEnvironmentSecrets
                                   ▼
                    ┌──────────────┴───────────────┐
                    ▼                               ▼
        Amazon RDS PostgreSQL              AWS Secrets Manager
        (private subnet, SG restricted        (DB conn string, JWT secret,
         to App Runner connector SG)            Resend key, SePay key)
```

All resources live in a single CDK stack: `StoryPlatformCoreStack`, deployed to account `028718096070` / `ap-southeast-1`.

## 5. Components

### 5.1 VPC
- New dedicated VPC, 2 AZs.
- ~~**Only `PRIVATE_ISOLATED` subnets** (no public subnets, no NAT Gateway). App Runner does not run "inside" the VPC directly — its VPC Connector creates ENIs in the specified subnets purely to reach RDS. Isolated subnets are sufficient since RDS needs no outbound internet access.~~
- ~~Rationale: avoids NAT Gateway cost (~$32/month), which is unjustified for this scope.~~
- **Superseded (post-final-review fix, see §13 decision log):** the VPC now also has a `PUBLIC` subnet group and a `PRIVATE_WITH_EGRESS` subnet group with a single shared NAT Gateway (`NatGateways = 1`, one NAT for both AZs to bound cost). The App Runner VPC Connector's ENIs sit in the `PRIVATE_WITH_EGRESS` subnets so the deployed app has outbound internet access. RDS stays in `PRIVATE_ISOLATED` subnets, unchanged — it still needs no internet access itself.

### 5.2 RDS PostgreSQL
- Instance class: `db.t4g.micro` (or `db.t3.micro` if Graviton unavailable in AZ) — within 12-month free tier.
- Single-AZ (cost: no Multi-AZ needed for a capstone demo).
- Placed in the VPC's isolated subnet group.
- Security Group: inbound TCP 5432 allowed **only** from the App Runner VPC Connector's security group. No public accessibility.
- Master credentials: generated via CDK `rds.DatabaseInstance` with `credentials: rds.Credentials.fromGeneratedSecret(...)` — lands in its own Secrets Manager secret (managed by the L2 construct). The application-facing `storyplatform/core/db-connection-string` secret (5.4) is a **separate** Secrets Manager secret whose value is composed at synth time via CDK token string interpolation (`$"Host={Database.DbInstanceEndpointAddress};Port={Database.DbInstanceEndpointPort};Database=storyplatform;Username={...};Password={...}"`, using `Database.Secret.SecretValueFromJson(...)` for the username/password tokens) — no custom Lambda needed, since CDK resolves nested tokens into an `Fn::Join` in the synthesized template automatically. App Runner only ever reads the composed `db-connection-string` secret, never the raw RDS-generated one directly.
- Database starts **empty** — no schema. Out of scope: applying EF Core migrations (see Non-goals).

### 5.3 ECR
- One repository: `storyplatform-core-api`.
- Lifecycle rule: keep last N images (e.g. 10) to bound storage cost.

### 5.4 AWS Secrets Manager (revised from SSM Parameter Store — see §13 decision log)
CloudFormation cannot natively create `AWS::SSM::Parameter` of type `SecureString` (a real AWS/CFN limitation — it would require a custom Lambda-backed resource). Secrets Manager supports secret creation natively in CDK with no custom Lambda needed, so all 4 app secrets are Secrets Manager secrets instead:
- `storyplatform/core/db-connection-string` — composed from the RDS-generated credentials (§5.2) via CDK token string interpolation at synth time, no Lambda required.
- `storyplatform/core/jwt-secret-key` — random value generated at synth time (not the repo's demo value).
- `storyplatform/core/resend-api-key` — placeholder value at deploy time; user manually updates the real key post-deploy via `aws secretsmanager put-secret-value` (Claude does not enter real API keys).
- `storyplatform/core/sepay-api-key` — same as above, placeholder + manual update.

Cost: ~$0.40/secret/month × 4 = ~$1.6/month (accepted trade-off — see §11).

### 5.5 IAM — App Runner instance role
- Least-privilege: `secretsmanager:GetSecretValue` scoped to the 4 secret ARNs from §5.4 only (App Runner's `RuntimeEnvironmentSecrets` grants this automatically when secrets are attached via the CDK construct's `.GrantRead()` — no manual KMS decrypt permission needed since Secrets Manager uses its own default encryption handled transparently).
- CloudWatch Logs write access is handled automatically by App Runner's own service role (separate from instance role) — standard CDK construct default.

### 5.6 App Runner service
- Source: ECR image, tag `latest`.
- `AutoDeploymentsEnabled: true` — new `latest` pushes trigger automatic redeploy (no explicit deploy API call needed from the workflow).
- VPC Connector attached (isolated subnets from 5.1) for RDS reachability.
- Environment: `ASPNETCORE_ENVIRONMENT=Production`, plus `RuntimeEnvironmentSecrets` mapping the Secrets Manager secrets from 5.4 into the container's env vars (`ConnectionStrings__DefaultConnection`, etc.).
- Default App Runner HTTPS domain (no custom domain in this iteration).

### 5.7 GitHub OIDC + IAM Role (CI identity)
- IAM OIDC Identity Provider for `token.actions.githubusercontent.com` (one per AWS account — created if not already present).
- IAM Role trust policy restricted to: this specific GitHub repo, and `ref:refs/heads/dev` / `ref:refs/heads/main` only.
- Role permissions (least-privilege, NOT AdministratorAccess):
  - ECR: `GetAuthorizationToken`, `BatchCheckLayerAvailability`, `PutImage`, `InitiateLayerUpload`, `UploadLayerPart`, `CompleteLayerUpload` scoped to the `storyplatform-core-api` repo.
  - No App Runner API permissions needed in the workflow itself, since `AutoDeploymentsEnabled` handles redeploy on image push.

## 6. Dockerfile (new file: `src/Core/StoryPlatform.Api/Dockerfile`)

Multi-stage build:
- Build stage: `mcr.microsoft.com/dotnet/sdk:10.0`, restore + publish `StoryPlatform.Api.csproj` (and its project references — Application/Domain/Infrastructure/Contracts).
- Runtime stage: `mcr.microsoft.com/dotnet/aspnet:10.0`, copy publish output, `EXPOSE 8080`, `ENTRYPOINT ["dotnet", "StoryPlatform.Api.dll"]`.
- `ASPNETCORE_URLS=http://+:8080` set via env in the image (App Runner expects the container to listen on a known port, configured in the CDK App Runner construct to match).

## 7. CI/CD (new file: `.github/workflows/deploy-core-api.yml`)

Trigger: `push` to `dev` or `main`, path-filtered to relevant source (`src/Core/**`, `src/Shared/**`, the Dockerfile, and the workflow file itself) to avoid irrelevant rebuilds (e.g. AI-only or docs-only changes).

Steps:
1. Checkout.
2. `aws-actions/configure-aws-credentials` using OIDC (`role-to-assume` = the CI role from 5.7, no stored access keys).
3. `aws-actions/amazon-ecr-login`.
4. `docker build` (context: repo root, dockerfile: `src/Core/StoryPlatform.Api/Dockerfile`), tag with both `latest` and the git SHA.
5. `docker push` both tags to ECR.

No explicit "deploy" step — App Runner's `AutoDeploymentsEnabled` picks up the new `latest` image automatically.

## 8. Data flow (deploy-time)

`git push (dev|main)` → GitHub Actions assumes CI role via OIDC → build image → push to ECR (`latest` + SHA tags) → App Runner detects new `latest` digest → pulls and redeploys → new container starts, reads env/secrets from Secrets Manager (via `RuntimeEnvironmentSecrets`) → (once migrations are applied in a later task) connects to RDS over the private VPC Connector path.

## 9. Error handling / rollback

- App Runner keeps the previous running deployment until the new one passes health checks; a failed deploy (crash loop, failed health check) automatically rolls back to the last working version — no manual rollback step needed for this iteration.
- If the CI OIDC role assumption fails or ECR push fails, the workflow fails before any deploy is attempted — no partial state.
- RDS and other stateful resources are not touched by the CI workflow at all (migrations are out of scope), so a bad app deploy cannot corrupt schema state.

## 10. Security considerations

- No long-lived AWS access keys stored in GitHub — OIDC federation only, scoped to this repo + branches.
- RDS has no public endpoint; reachable only from the App Runner VPC Connector's security group.
- App secrets (JWT key, Resend key, SePay key, DB connection string) live in AWS Secrets Manager, read at runtime only by the App Runner instance role (scoped IAM policy, `secretsmanager:GetSecretValue` on the 4 secret ARNs only) — never embedded in the Docker image or committed to git.
- The repo's current demo `JwtSettings:SecretKey` (flagged in GitGuardian, commit `a253add`) is never reused for the real deployment — CDK generates/stores a fresh one.
- `admin-deploy` (AdministratorAccess) is used only for the human's manual `cdk deploy` from their machine; it is never used by CI.

## 11. Cost estimate (rough, ap-southeast-1)

- RDS `db.t4g.micro`: free tier eligible for 12 months, then ~$12–15/month.
- App Runner: pay-per-use, roughly $5–25/month depending on min instance/traffic — can configure to scale to a low minimum.
- ECR storage: negligible for a handful of images with lifecycle cleanup.
- ~~No NAT Gateway (saves ~$32/month vs. a public-subnet design).~~ **Revised (post-final-review fix, see §13):** a single NAT Gateway was added (~$32/month base + data transfer charges), since the app's external integrations (Resend, Gemini, SePay, Supabase) require outbound internet access that a NAT-less VPC could not provide.
- AWS Secrets Manager: ~$0.40/secret/month × 4 secrets = ~$1.6/month (switched from SSM Parameter Store — see §13; CFN cannot natively create SecureString parameters).

## 12. Testing / validation plan

- `cdk synth` — validate the generated CloudFormation template before any real deploy.
- `cdk deploy` — requires explicit user confirmation before running (creates billed resources); will not be run automatically.
- After deploy: hit the App Runner default HTTPS URL's Swagger/health endpoint to confirm the container starts and responds (DB-dependent endpoints will fail until migrations are applied in the follow-up task — expected and acceptable for this iteration).
- CI workflow validated by pushing a trivial change to `dev` and confirming: OIDC auth succeeds, image builds, pushes to ECR, App Runner auto-redeploys.

## 13. Decision log (from brainstorming Q&A)

| Decision | Choice | Rationale |
|---|---|---|
| RDS network | Private subnet + VPC Connector | Security best practice, user-approved over public+SG |
| EF Core migrations | Deferred to a future task | Keeps this task focused on infra; no auto-migrate added to `Program.cs` (would be out-of-scope app code change) |
| CI/CD | Included in this task | User explicitly requested GitHub Actions now, not deferred |
| Secrets storage | AWS Secrets Manager (not SSM Parameter Store) | Discovered during plan-writing: CloudFormation cannot natively create `SecureString` SSM parameters (would require a custom Lambda). Secrets Manager supports native creation in CDK. User approved the ~$1.6/month trade-off over adding Lambda complexity |
| Deploy trigger | Push to `dev` or `main` | User-specified |
| Environment topology | Single shared environment for both branches | User explicitly chose this over separate dev/prod stacks, accepting the risk of dev pushes affecting the demo instance |
| CI AWS auth | GitHub OIDC + scoped IAM Role | User chose over long-lived access keys in GitHub Secrets |
| Compute | App Runner (not EC2) | Fully managed, no OS/patching burden, native ECR auto-deploy integration matches the CI design; EC2 rejected as unnecessary operational overhead for this scope |
| NAT Gateway (added after initial deploy) | Add 1 shared NAT Gateway + `PUBLIC`/`PRIVATE_WITH_EGRESS` subnets; VPC Connector moved from `PRIVATE_ISOLATED` to `PRIVATE_WITH_EGRESS` | A final whole-branch code review (2026-09-19, post-deploy) found the original NAT-less design (§5.1, §11) gave the deployed app ZERO outbound internet access, breaking its dependencies on Resend (email), Gemini (AI generation), SePay (payment QR), and Supabase (media storage) — all of which require egress the isolated-subnet-only VPC could not provide. User approved adding a NAT Gateway to restore functionality, accepting the ~$32/month + data-transfer cost this reintroduces (see revised §11). RDS stays in `PRIVATE_ISOLATED`, unaffected. |
| JWT secret generation | Secrets Manager's own `GenerateSecretString` (server-side) instead of C# `RandomNumberGenerator` + `SecretValue.UnsafePlainText` | Same final review found `UnsafePlainText(GenerateRandomSecret())` embedded the generated plaintext secret directly into the synthesized CloudFormation template (readable by anyone with template/asset-bucket access), and regenerated a new value on every `cdk synth`. Switching to `GenerateSecretString` keeps the value out of the template and stable across synths/deploys, matching how the other 3 secrets already behave. |

## 14. Follow-up work (not in this task)

- Design and implement EF Core migration application strategy against RDS.
- Deploy `StoryPlatform.AI.Api` (separate stack or extend this one — TBD in a future brainstorming session).
- Consider S3 + CloudFront once AI-generated media (images/TTS audio) needs storage.
- Consider custom domain (Route 53 + ACM) if needed beyond the App Runner default domain.
