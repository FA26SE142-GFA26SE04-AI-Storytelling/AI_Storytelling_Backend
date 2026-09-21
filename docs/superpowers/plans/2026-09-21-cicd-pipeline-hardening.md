# CI/CD Pipeline Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden `deploy-core-api.yml` so every push to `dev`/`main` is test-gated, applies pending EF Core migrations automatically, and reports a trustworthy pass/fail deploy signal — closing the migration-strategy gap left open by the original AWS deployment spec.

**Architecture:** Add a `test` job (with its own .NET SDK setup) ahead of the existing `build-and-push` job; switch the image build to the root `Dockerfile` (`--target core-api`) to match local dev; disable App Runner's implicit `AutoDeploymentsEnabled` and replace it with an explicit `aws apprunner start-deployment` + poll step in CI; add a tiny startup extension method that calls `ApplicationDbContext.Database.Migrate()` inside the already-VPC-connected App Runner container; fix the CDK health check path so App Runner's built-in rollback-on-unhealthy safety net actually protects both of the above.

**Tech Stack:** .NET 10 / ASP.NET Core, EF Core (Npgsql provider), AWS CDK (C#), GitHub Actions, AWS App Runner/ECR/IAM, xUnit, Amazon.CDK.Assertions, EF Core Sqlite provider (test-only, new).

**Spec:** `docs/superpowers/specs/2026-09-21-cicd-pipeline-hardening-design.md` (this plan implements it in full; read both together).

## Global Constraints

- Single shared environment — both `dev` and `main` deploy to the same App Runner service/RDS instance. No staging/prod split.
- `cdk deploy` stays a manual, human-run step — never automate it in CI, and do not attempt to run it from an agent session (the harness blocks IaC-apply actions from agents by policy).
- Scope is backend Core API only (`StoryPlatform.Api` and its dependencies). `StoryPlatform.AI.Api` is untouched.
- No new long-lived AWS credentials. The existing OIDC-federated CI role (`storyplatform-core-api-ci`) is extended with narrowly-scoped permissions only.
- AWS account `028718096070`, region `ap-southeast-1`, App Runner service ARN `arn:aws:apprunner:ap-southeast-1:028718096070:service/storyplatform-core-api/fda923da69b54ce4b6f7f37d67f9cdcf`.

---

## Task 1: CDK stack — health check path, disable implicit auto-deploy, CI role App Runner permissions

**Files:**
- Modify: `infra/aws-cdk/src/StoryPlatformCoreStack.cs`
- Modify: `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`

**Interfaces:**
- Consumes: existing `CiRole` (`IRole`), `AppRunnerService` (`CfnService?`, non-null only when `includeAppRunnerService` context is `true`), both already fields on `StoryPlatformCoreStack`.
- Produces: no new public members. `AppRunnerService.AttrServiceArn` becomes referenced by the workflow (Task 3) via a hardcoded literal, not passed through code — no interface coupling to later tasks.

- [ ] **Step 1: Update the two existing tests to assert the new expected values (TDD — make them fail first)**

In `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`, replace the `Stack_CreatesAppRunnerServiceWithAutoDeploy_WhenContextFlagEnabled` test:

```csharp
    [Fact]
    public void Stack_CreatesAppRunnerServiceWithAutoDeployDisabled_WhenContextFlagEnabled()
    {
        var template = SynthTemplate(new System.Collections.Generic.Dictionary<string, object>
        {
            ["includeAppRunnerService"] = true
        });
        template.ResourceCountIs("AWS::AppRunner::Service", 1);
        template.HasResourceProperties("AWS::AppRunner::Service", new System.Collections.Generic.Dictionary<string, object>
        {
            ["SourceConfiguration"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
            {
                ["AutoDeploymentsEnabled"] = false
            })
        });
    }
```

Replace the `Stack_AppRunnerServiceHasHealthCheckConfiguration` test's expected path:

```csharp
    [Fact]
    public void Stack_AppRunnerServiceHasHealthCheckConfiguration()
    {
        var template = SynthTemplate(new System.Collections.Generic.Dictionary<string, object>
        {
            ["includeAppRunnerService"] = true
        });
        template.HasResourceProperties("AWS::AppRunner::Service", new System.Collections.Generic.Dictionary<string, object>
        {
            ["HealthCheckConfiguration"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
            {
                ["Protocol"] = "HTTP",
                ["Path"] = "/health"
            })
        });
    }
```

Add a new test at the end of the class (before the final closing `}`):

```csharp
    [Fact]
    public void Stack_GrantsCiRoleAppRunnerDeployPermissions_WhenContextFlagEnabled()
    {
        var template = SynthTemplate(new System.Collections.Generic.Dictionary<string, object>
        {
            ["includeAppRunnerService"] = true
        });
        template.HasResourceProperties("AWS::IAM::Policy", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["PolicyDocument"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
            {
                ["Statement"] = Match.ArrayWith(new object[]
                {
                    Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["Action"] = Match.ArrayWith(new object[] { "apprunner:StartDeployment", "apprunner:DescribeService" }),
                        ["Resource"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["Fn::GetAtt"] = Match.ArrayWith(new object[]
                            {
                                Match.StringLikeRegexp("^CoreApiService"),
                                "ServiceArn"
                            })
                        })
                    })
                })
            })
        }));
    }
```

- [ ] **Step 2: Run tests to verify the 3 assertions fail against current code**

Run: `cd infra/aws-cdk && dotnet test test/StoryPlatform.Infra.Tests.csproj`
Expected: FAIL — `Stack_CreatesAppRunnerServiceWithAutoDeployDisabled_WhenContextFlagEnabled` (still `true` in template), `Stack_AppRunnerServiceHasHealthCheckConfiguration` (still `/index.html`), `Stack_GrantsCiRoleAppRunnerDeployPermissions_WhenContextFlagEnabled` (no matching IAM policy statement exists yet). 3 failed, rest pass.

- [ ] **Step 3: Implement the CDK changes**

In `infra/aws-cdk/src/StoryPlatformCoreStack.cs`, inside the `if (includeAppRunnerService)` block:

Change the `SourceConfiguration.AutoDeploymentsEnabled` value from `true` to `false`:

```csharp
                SourceConfiguration = new CfnService.SourceConfigurationProperty
                {
                    AutoDeploymentsEnabled = false,
```

Change `HealthCheckConfiguration.Path`:

```csharp
                HealthCheckConfiguration = new CfnService.HealthCheckConfigurationProperty
                {
                    Protocol = "HTTP",
                    Path = "/health",
                    Interval = 10,
                    Timeout = 5,
                    HealthyThreshold = 1,
                    UnhealthyThreshold = 5
                },
```

Immediately after the `AppRunnerService = new CfnService(...)` assignment closes (i.e. right after the statement that ends with `});` for `AppRunnerService`, still inside the `if (includeAppRunnerService)` block), add:

```csharp
            CiRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[] { "apprunner:StartDeployment", "apprunner:DescribeService" },
                Resources = new[] { AppRunnerService.AttrServiceArn }
            }));
```

This requires `using Amazon.CDK.AWS.IAM;` — already present in the file (used for `Role`, `ServicePrincipal`, etc.), so no new `using` needed; `PolicyStatement`, `PolicyStatementProps`, and `Effect` all live in that same namespace.

- [ ] **Step 4: Run tests to verify all pass**

Run: `cd infra/aws-cdk && dotnet test test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS — all tests green (13 existing + 1 new = 14 total, with 2 renamed/modified).

- [ ] **Step 5: Build to confirm no other breakage**

Run: `cd infra/aws-cdk && dotnet build src/StoryPlatform.Infra.csproj`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add infra/aws-cdk/src/StoryPlatformCoreStack.cs infra/aws-cdk/test/StoryPlatformCoreStackTests.cs
git commit -m "fix(infra): disable implicit App Runner auto-deploy, fix health check path, grant CI deploy permissions"
```

---

## Task 2: Auto-migration on App Runner startup

**Files:**
- Create: `src/Core/StoryPlatform.Api/Extensions/DatabaseMigrationExtensions.cs`
- Modify: `src/Core/StoryPlatform.Api/Program.cs`
- Create: `tests/StoryPlatform.UnitTests/Extensions/DatabaseMigrationExtensionsTests.cs`
- Modify: `tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj` (new test-only dependency, see Step 1)

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `StoryPlatform.Api.Extensions.DatabaseMigrationExtensions.ApplyPendingMigrations<TContext>(this IHost host) where TContext : DbContext` — generic extension method, no other task depends on its exact signature.

- [ ] **Step 1: Add the EF Core Sqlite test dependency**

This test needs a real (if lightweight) relational database to prove `Database.Migrate()` actually executes — EF Core's InMemory provider throws on `Migrate()` (relational-only API), and the project has no Postgres-in-CI infrastructure. SQLite-in-memory is the standard, dependency-light way to test EF Core migration code without a real server; it is test-only and never ships in the API image.

In `tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj`, add to the existing `<ItemGroup>` with the other `PackageReference` entries:

```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0" />
```

(Match whatever exact EF Core version `StoryPlatform.Infrastructure.csproj` already pins for `Microsoft.EntityFrameworkCore` — open that file and use the same version number instead of `10.0.0` if it differs, so the Sqlite provider and the Npgsql provider stay on the same EF Core major/minor version.)

- [ ] **Step 2: Write the failing test**

Create `tests/StoryPlatform.UnitTests/Extensions/DatabaseMigrationExtensionsTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StoryPlatform.Api.Extensions;
using Xunit;

namespace StoryPlatform.UnitTests.Extensions;

public sealed class ProbeDbContext : DbContext
{
    public ProbeDbContext(DbContextOptions<ProbeDbContext> options) : base(options) { }
    public DbSet<ProbeWidget> Widgets => Set<ProbeWidget>();
}

public sealed class ProbeWidget
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

[DbContext(typeof(ProbeDbContext))]
[Migration("20260101000000_CreateProbeWidgets")]
public sealed class CreateProbeWidgets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProbeWidgets",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ProbeWidgets", x => x.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProbeWidgets");
    }
}

public class DatabaseMigrationExtensionsTests
{
    [Fact]
    public void ApplyPendingMigrations_RunsPendingMigrationAgainstResolvedContext()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<ProbeDbContext>(options => options.UseSqlite(connection));
            })
            .Build();

        host.ApplyPendingMigrations<ProbeDbContext>();

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();

        Assert.Contains("20260101000000_CreateProbeWidgets", context.Database.GetAppliedMigrations());
        Assert.Empty(context.Widgets.ToList());
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~DatabaseMigrationExtensionsTests"`
Expected: FAIL to build — `ApplyPendingMigrations` does not exist yet (CS1061 or similar).

- [ ] **Step 4: Write the minimal implementation**

Create `src/Core/StoryPlatform.Api/Extensions/DatabaseMigrationExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace StoryPlatform.Api.Extensions;

public static class DatabaseMigrationExtensions
{
    public static void ApplyPendingMigrations<TContext>(this IHost host) where TContext : DbContext
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        context.Database.Migrate();
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj --filter "FullyQualifiedName~DatabaseMigrationExtensionsTests"`
Expected: PASS.

- [ ] **Step 6: Wire the extension method into the real app startup**

In `src/Core/StoryPlatform.Api/Program.cs`, add the using at the top (alongside the existing `using StoryPlatform.Api.*` lines):

```csharp
using StoryPlatform.Api.Extensions;
```

Then, immediately after `var app = builder.Build();` (line 52) and before the `// Pipeline xử lý HTTP Request` comment, add:

```csharp

app.ApplyPendingMigrations<StoryPlatform.Infrastructure.Persistence.ApplicationDbContext>();
```

- [ ] **Step 7: Run the full unit test suite and build to confirm nothing else broke**

Run: `dotnet build StoryPlatform.sln --configuration Release`
Expected: `Build succeeded`, 0 errors.

Run: `dotnet test tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj`
Expected: all PASS.

- [ ] **Step 8: Local smoke test against a real Postgres (validates the Npgsql-specific path the unit test above deliberately doesn't cover)**

This step needs a local `.env` file. If one doesn't already exist at the repo root, copy the example and fill in real local-only values:

```bash
cp .env.docker.example .env
```

Edit `.env` and set non-empty values for at minimum `POSTGRES_PASSWORD`, `JWT_SECRET_KEY` (32+ characters), and `AI_INTERNAL_API_KEY` (any local placeholder string — this step never touches the AI service).

Build and start the stack:

```bash
docker compose up -d --build postgres core-api
```

Wait for `core-api` to report healthy (compose's own healthcheck already targets `/health`, matching Task 1's CDK fix):

```bash
docker compose ps
```

Expected: both `postgres` and `core-api` show `healthy`.

Confirm the app itself is reachable and confirm the migrations actually ran, by checking the EF Core migrations history table was populated:

```bash
curl -f http://localhost:${CORE_API_PORT:-5259}/health
docker compose exec postgres psql -U ${POSTGRES_USER:-postgres} -d ${POSTGRES_DB:-AIStorytellingDB} -c "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";"
```

Expected: `/health` returns HTTP 200 (curl exits 0), and the query lists every migration under `src/Core/StoryPlatform.Infrastructure/Migrations` — proving `Database.Migrate()` really executed against Postgres on startup, not just against the SQLite fixture from Step 2.

Tear down:

```bash
docker compose down
```

- [ ] **Step 9: Commit**

```bash
git add src/Core/StoryPlatform.Api/Extensions/DatabaseMigrationExtensions.cs \
        src/Core/StoryPlatform.Api/Program.cs \
        tests/StoryPlatform.UnitTests/Extensions/DatabaseMigrationExtensionsTests.cs \
        tests/StoryPlatform.UnitTests/StoryPlatform.UnitTests.csproj
git commit -m "feat(api): apply pending EF Core migrations automatically at startup"
```

(`.env` is already covered by the repo's existing `.gitignore`/`.dockerignore` — do not add it.)

---

## Task 3: CI workflow — test gate, root Dockerfile, explicit deploy verification

**Files:**
- Modify: `.github/workflows/deploy-core-api.yml`

**Interfaces:**
- Consumes: the App Runner service ARN (Global Constraints) and the CI role's new IAM permissions from Task 1 (must be live in AWS — i.e. Task 1's `cdk deploy` must have happened — before this workflow's `start-deployment` step can succeed; the `test` and `build-and-push` steps work regardless).
- Produces: nothing consumed by later tasks in this plan.

- [ ] **Step 1: Replace the workflow file**

There is no automated test harness for GitHub Actions YAML in this repo (confirmed during spec research — no `actionlint` or similar tooling present). This step's correctness is verified live in Task 5. Replace the full contents of `.github/workflows/deploy-core-api.yml` with:

```yaml
# .github/workflows/deploy-core-api.yml
name: Deploy Core API

on:
  push:
    branches: [dev, main]
    paths:
      - 'src/Core/**'
      - 'src/Shared/**'
      - 'Dockerfile'
      - '.github/workflows/deploy-core-api.yml'

permissions:
  id-token: write
  contents: read

env:
  AWS_REGION: ap-southeast-1
  ECR_REPOSITORY: storyplatform-core-api
  APP_RUNNER_SERVICE_ARN: arn:aws:apprunner:ap-southeast-1:028718096070:service/storyplatform-core-api/fda923da69b54ce4b6f7f37d67f9cdcf

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET SDK
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore StoryPlatform.sln

      - name: Build
        run: dotnet build StoryPlatform.sln --no-restore --configuration Release

      - name: Test
        run: dotnet test StoryPlatform.sln --no-build --configuration Release --verbosity normal

  build-and-push:
    needs: test
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Configure AWS credentials via OIDC
        uses: aws-actions/configure-aws-credentials@v4
        with:
          role-to-assume: arn:aws:iam::028718096070:role/storyplatform-core-api-ci
          aws-region: ${{ env.AWS_REGION }}

      - name: Login to Amazon ECR
        id: ecr-login
        uses: aws-actions/amazon-ecr-login@v2

      - name: Build and push image
        env:
          ECR_REGISTRY: ${{ steps.ecr-login.outputs.registry }}
          IMAGE_TAG: ${{ github.sha }}
        run: |
          docker build \
            --file Dockerfile \
            --target core-api \
            --tag "$ECR_REGISTRY/$ECR_REPOSITORY:$IMAGE_TAG" \
            --tag "$ECR_REGISTRY/$ECR_REPOSITORY:latest" \
            .
          docker push "$ECR_REGISTRY/$ECR_REPOSITORY:$IMAGE_TAG"
          docker push "$ECR_REGISTRY/$ECR_REPOSITORY:latest"

      - name: Trigger App Runner deployment
        run: |
          aws apprunner start-deployment \
            --service-arn "$APP_RUNNER_SERVICE_ARN" \
            --region "$AWS_REGION"

      - name: Wait for App Runner deployment to finish
        run: |
          for i in $(seq 1 40); do
            STATUS=$(aws apprunner describe-service \
              --service-arn "$APP_RUNNER_SERVICE_ARN" \
              --region "$AWS_REGION" \
              --query 'Service.Status' --output text)
            echo "App Runner service status: $STATUS"
            if [ "$STATUS" = "RUNNING" ]; then
              echo "Deployment succeeded."
              exit 0
            fi
            if [ "$STATUS" = "ROLLBACK_SUCCEEDED" ] || [ "$STATUS" = "ROLLBACK_FAILED" ] || [ "$STATUS" = "CREATE_FAILED" ]; then
              echo "Deployment failed, App Runner reported status: $STATUS"
              exit 1
            fi
            sleep 15
          done
          echo "Timed out after 10 minutes waiting for App Runner deployment to finish."
          exit 1
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/deploy-core-api.yml
git commit -m "ci(deploy): add test gate, build from root Dockerfile, verify App Runner deploy explicitly"
```

---

## Task 4: Update RUNBOOK.md to describe the new deploy flow

**Files:**
- Modify: `infra/aws-cdk/RUNBOOK.md`

**Interfaces:**
- Consumes: nothing (documentation only).
- Produces: nothing.

- [ ] **Step 1: Add a new section describing the hardened flow**

In `infra/aws-cdk/RUNBOOK.md`, add a new section after the existing "## Two-phase deploy (why it exists)" section (that section is unaffected and stays as-is — it governs `includeAppRunnerService` bootstrapping, which this task does not change):

```markdown
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
```

- [ ] **Step 2: Commit**

```bash
git add infra/aws-cdk/RUNBOOK.md
git commit -m "docs(infra): document the hardened CI/CD deploy flow in RUNBOOK"
```

---

## Task 5: Apply the infra change and verify end-to-end

**Files:** none (operational task — running commands and observing real AWS/GitHub state).

**Interfaces:** none.

**⚠️ This task's `cdk deploy` and `git push` steps mutate real, shared infrastructure and a shared branch. Do not run them from an unattended agent — this plan's earlier tasks (1–4) are the code changes; this task hands control back to the human (you), same as the OIDC trust-policy fix earlier this session.**

- [ ] **Step 1: Human runs `cdk deploy` to apply Task 1's stack changes**

```bash
cd infra/aws-cdk
cdk deploy --profile storyplatform --require-approval never
```

Expected: CloudFormation update succeeds, in place (no resource replacement — already confirmed via `aws cloudformation describe-type AWS::AppRunner::Service` that `SourceConfiguration` and `HealthCheckConfiguration` are not `createOnlyProperties`).

- [ ] **Step 2: Human verifies the new health check path is live**

```bash
aws apprunner describe-service \
  --service-arn arn:aws:apprunner:ap-southeast-1:028718096070:service/storyplatform-core-api/fda923da69b54ce4b6f7f37d67f9cdcf \
  --region ap-southeast-1 \
  --query 'Service.SourceConfiguration.AutoDeploymentsEnabled'
```

Expected output: `false`.

- [ ] **Step 3: Human merges the work into `main` to trigger the workflow**

`dev` does not currently exist as a branch on `origin` for this repo (confirmed: `git branch -a` on 2026-09-21 shows only `origin/main` plus feature/worktree branches — no `origin/dev`). The workflow only triggers on push to `dev` or `main`, so the realistic trigger path is a PR from this worktree's branch (`worktree-aws-core-api-deployment`) into `main`, matching how the prior AWS deployment work was merged (PR #25):

```bash
git push origin worktree-aws-core-api-deployment
gh pr create --base main --head worktree-aws-core-api-deployment \
  --title "CI/CD pipeline hardening: test gate, auto-migration, deploy verification" \
  --body "Implements docs/superpowers/specs/2026-09-21-cicd-pipeline-hardening-design.md"
```

Then merge that PR (after review) — the merge commit landing on `main` is what actually triggers `deploy-core-api.yml`.

- [ ] **Step 4: Human watches the GitHub Actions run**

Open the repo's Actions tab, find the "Deploy Core API" run triggered by the push. Confirm in order:
- `test` job: green.
- `build-and-push` job: "Trigger App Runner deployment" step succeeds, "Wait for App Runner deployment to finish" step logs status transitioning to `RUNNING` and exits 0.

- [ ] **Step 5: Human confirms the live service is healthy on the new path**

```bash
SERVICE_URL=$(aws apprunner describe-service \
  --service-arn arn:aws:apprunner:ap-southeast-1:028718096070:service/storyplatform-core-api/fda923da69b54ce4b6f7f37d67f9cdcf \
  --region ap-southeast-1 \
  --query 'Service.ServiceUrl' --output text)
curl -f "https://$SERVICE_URL/health"
```

Expected: HTTP 200.

- [ ] **Step 6: Human confirms migrations were applied to the real RDS instance**

Since RDS has no public endpoint, this must run from something already inside the VPC — simplest is to temporarily check via the App Runner service's own logs in CloudWatch (search for EF Core migration log lines around the deploy's startup), rather than opening a new network path into RDS for this one check:

```bash
aws logs tail /aws/apprunner/storyplatform-core-api/*/application \
  --region ap-southeast-1 --profile storyplatform --since 10m \
  | grep -i migrat
```

Expected: log lines showing the migrations that were applied (or, on a repeat deploy with nothing pending, no error and the app still starts — both are correct outcomes).

---

## Self-review notes (from the plan-writing pass)

- **Spec coverage:** §5.1 (test gate) → Task 3. §5.2 (auto-migration) → Task 2. §5.3 (explicit deploy + verification) → Task 3. §5.4 (health check path) → Task 1. §5.5 (IAM + `AutoDeploymentsEnabled=false`) → Task 1. §5.6 (RUNBOOK update) → Task 4. §9 (testing/validation plan, including the local negative-migration check) → covered by Task 2 Step 8's real-Postgres smoke test plus Task 5's live verification; the spec's own negative-case (deliberately broken migration) is intentionally not turned into a permanently-committed automated test — see the spec's §9 rationale (don't risk the one shared environment) — a developer wanting to exercise that path can temporarily add a broken migration locally against the Step 8 compose stack and observe the container fail to become healthy, then discard it before committing.
- **New finding folded in during plan-writing, not in the original spec:** the CI workflow was building from a now-superseded `src/Core/StoryPlatform.Api/Dockerfile` that has drifted from the root `Dockerfile` added by the separately-merged Docker Compose work (missing `curl`, single-service only). User confirmed (this session) switching `deploy-core-api.yml` to build from the root `Dockerfile --target core-api` instead — folded into Task 3. The old per-service Dockerfile is left on disk untouched (not deleted — wasn't asked for).
