# AWS Core API Deployment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a working AWS deployment (via AWS CDK, C#) for `StoryPlatform.Api` (Core backend only) — VPC, private RDS PostgreSQL, ECR, Secrets Manager, App Runner — plus a Dockerfile and a GitHub Actions CI/CD pipeline that auto-deploys on push to `dev` or `main`.

**Architecture:** A single CDK stack (`StoryPlatformCoreStack`) built incrementally, construct by construct, each validated with `Amazon.CDK.Assertions` template tests before moving to the next. Secrets (DB connection string, JWT key, Resend key, SePay key) live in AWS Secrets Manager, composed via CDK token interpolation (no custom Lambda). CI authenticates to AWS via GitHub OIDC (no long-lived keys); App Runner auto-redeploys on new ECR image pushes.

**Tech Stack:** AWS CDK 2.x (C#, .NET 8 for the infra project — independent of the app's net10.0), xUnit + `Amazon.CDK.Assertions` for infra tests, Docker multi-stage build (.NET 10 SDK/ASP.NET runtime images), GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-19-aws-core-api-deployment-design.md`

## Global Constraints

- AWS account: `028718096070`, region: `ap-southeast-1`.
- Scope: `StoryPlatform.Api` (Core) only — never touch `StoryPlatform.AI.Api` or its infra in this plan.
- No NAT Gateway — VPC uses `PRIVATE_ISOLATED` subnets only.
- No public RDS access — reachable only from the App Runner VPC Connector's security group.
- All app secrets in AWS Secrets Manager (not SSM Parameter Store — CFN cannot natively create `SecureString` SSM parameters).
- No EF Core migration application in this plan (RDS is provisioned empty; deferred to a future task).
- CI authenticates via GitHub OIDC + scoped IAM Role — never long-lived access keys in GitHub Secrets.
- `cdk deploy` (real, billed AWS resources) is never run without explicit user confirmation immediately before the command.
- App Runner Service creation is bootstrap-ordered behind a CDK context flag (`includeAppRunnerService`, default `false`) because it references an ECR image that cannot exist before the stack's own ECR repo is created — see Task 8 and Task 13 for the two-phase deploy this requires. Do not remove this conditional as a "simplification"; without it, the first `cdk deploy` fails and CloudFormation rolls back the entire stack.
- Never enter real third-party API keys (Resend, SePay) into any file, CLI command, or AWS console field — those are placeholder values the user fills in manually post-deploy.

---

### Task 1: CDK project scaffold

**Files:**
- Create: `infra/aws-cdk/cdk.json`
- Create: `infra/aws-cdk/src/StoryPlatform.Infra.csproj`
- Create: `infra/aws-cdk/src/Program.cs`
- Create: `infra/aws-cdk/src/StoryPlatformCoreStack.cs`
- Create: `infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
- Create: `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`
- Create: `infra/aws-cdk/.gitignore`

**Interfaces:**
- Produces: `StoryPlatformCoreStack` class (namespace `StoryPlatform.Infra`), constructor `(Construct scope, string id, IStackProps props)`. Later tasks add public properties to this class (`Vpc`, `Database`, `EcrRepository`, etc.) — each task documents what it adds. Also produces the test helper `StoryPlatformCoreStackTests.SynthTemplate(IDictionary<string, object>? context = null)` — Task 8 passes `context` to synthesize with/without the App Runner Service (see Task 8's bootstrap note).

- [ ] **Step 1: Create the infra directory and `.gitignore`**

```
# infra/aws-cdk/.gitignore
bin/
obj/
cdk.out/
*.user
```

- [ ] **Step 2: Create `cdk.json`**

```json
{
  "app": "dotnet run --project src/StoryPlatform.Infra.csproj"
}
```

- [ ] **Step 3: Create the infra project file**

```xml
<!-- infra/aws-cdk/src/StoryPlatform.Infra.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>StoryPlatform.Infra</RootNamespace>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Amazon.CDK.Lib" Version="2.170.0" />
    <PackageReference Include="Constructs" Version="10.4.2" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create `Program.cs`**

```csharp
// infra/aws-cdk/src/Program.cs
using Amazon.CDK;

namespace StoryPlatform.Infra;

public static class Program
{
    public static void Main(string[] args)
    {
        var app = new App();

        new StoryPlatformCoreStack(app, "StoryPlatformCoreStack", new StackProps
        {
            Env = new Amazon.CDK.Environment
            {
                Account = "028718096070",
                Region = "ap-southeast-1"
            }
        });

        app.Synth();
    }
}
```

- [ ] **Step 5: Create the empty stack skeleton**

```csharp
// infra/aws-cdk/src/StoryPlatformCoreStack.cs
using Amazon.CDK;
using Constructs;

namespace StoryPlatform.Infra;

public sealed class StoryPlatformCoreStack : Stack
{
    public StoryPlatformCoreStack(Construct scope, string id, IStackProps? props = null)
        : base(scope, id, props)
    {
    }
}
```

- [ ] **Step 6: Create the test project file**

```xml
<!-- infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Amazon.CDK.Lib" Version="2.170.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\src\StoryPlatform.Infra.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 7: Create the first test**

```csharp
// infra/aws-cdk/test/StoryPlatformCoreStackTests.cs
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Xunit;

namespace StoryPlatform.Infra.Tests;

public class StoryPlatformCoreStackTests
{
    internal static Template SynthTemplate(System.Collections.Generic.IDictionary<string, object>? context = null)
    {
        var app = new App(new AppProps { Context = context });
        var stack = new StoryPlatformCoreStack(app, "TestStack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "028718096070", Region = "ap-southeast-1" }
        });
        return Template.FromStack(stack);
    }

    [Fact]
    public void Stack_SynthesizesWithoutError()
    {
        var template = SynthTemplate();
        Assert.NotNull(template);
    }
}
```

- [ ] **Step 8: Run the test**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS (1 test)

- [ ] **Step 9: Commit**

```bash
git add infra/aws-cdk/
git commit -m "infra: scaffold CDK C# project for AWS Core API deployment"
```

---

### Task 2: VPC (no NAT, isolated subnets only)

**Files:**
- Modify: `infra/aws-cdk/src/StoryPlatformCoreStack.cs`
- Modify: `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`

**Interfaces:**
- Consumes: `SynthTemplate()` from Task 1.
- Produces: `public IVpc Vpc { get; }` on `StoryPlatformCoreStack`, usable by later tasks needing VPC placement (RDS, VPC Connector).

- [ ] **Step 1: Write the failing tests**

Add to `StoryPlatformCoreStackTests.cs`:

```csharp
    [Fact]
    public void Stack_CreatesVpcWithNoNatGateways()
    {
        var template = SynthTemplate();
        template.ResourceCountIs("AWS::EC2::VPC", 1);
        template.ResourceCountIs("AWS::EC2::NatGateway", 0);
    }

    [Fact]
    public void Stack_VpcHasTwoIsolatedSubnetsOnly()
    {
        var template = SynthTemplate();
        // 2 AZs x 1 isolated subnet each, no public subnets
        template.ResourceCountIs("AWS::EC2::Subnet", 2);
        template.ResourceCountIs("AWS::EC2::InternetGateway", 0);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: FAIL (0 VPC resources found)

- [ ] **Step 3: Implement the VPC**

In `StoryPlatformCoreStack.cs`, add the using and the property + construction:

```csharp
using Amazon.CDK.AWS.EC2;
```

```csharp
public sealed class StoryPlatformCoreStack : Stack
{
    public IVpc Vpc { get; }

    public StoryPlatformCoreStack(Construct scope, string id, IStackProps? props = null)
        : base(scope, id, props)
    {
        Vpc = new Vpc(this, "CoreVpc", new VpcProps
        {
            MaxAzs = 2,
            NatGateways = 0,
            SubnetConfiguration = new[]
            {
                new SubnetConfiguration
                {
                    Name = "Isolated",
                    SubnetType = SubnetType.PRIVATE_ISOLATED,
                    CidrMask = 24
                }
            }
        });
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add infra/aws-cdk/
git commit -m "infra: add VPC with isolated-only subnets (no NAT Gateway)"
```

---

### Task 3: RDS PostgreSQL (private)

**Files:**
- Modify: `infra/aws-cdk/src/StoryPlatformCoreStack.cs`
- Modify: `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`

**Interfaces:**
- Consumes: `Vpc` (Task 2).
- Produces: `public DatabaseInstance Database { get; }` on `StoryPlatformCoreStack` — later tasks (Secrets Manager composition, VPC Connector security group rule) read `Database.DbInstanceEndpointAddress`, `Database.DbInstanceEndpointPort`, `Database.Secret`, `Database.Connections.SecurityGroups`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void Stack_CreatesPrivateRdsPostgresInstance()
    {
        var template = SynthTemplate();
        template.ResourceCountIs("AWS::RDS::DBInstance", 1);
        template.HasResourceProperties("AWS::RDS::DBInstance", new System.Collections.Generic.Dictionary<string, object>
        {
            ["Engine"] = "postgres",
            ["DBInstanceClass"] = "db.t4g.micro",
            ["PubliclyAccessible"] = false
        });
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: FAIL (0 DBInstance resources found)

- [ ] **Step 3: Implement RDS**

```csharp
using Amazon.CDK.AWS.RDS;
```

```csharp
    public DatabaseInstance Database { get; }

    public StoryPlatformCoreStack(Construct scope, string id, IStackProps? props = null)
        : base(scope, id, props)
    {
        Vpc = new Vpc(this, "CoreVpc", new VpcProps { /* ...unchanged from Task 2... */ });

        Database = new DatabaseInstance(this, "CoreDatabase", new DatabaseInstanceProps
        {
            Engine = DatabaseInstanceEngine.Postgres(new PostgresInstanceEngineProps
            {
                Version = PostgresEngineVersion.VER_16_4
            }),
            InstanceType = Amazon.CDK.AWS.EC2.InstanceType.Of(InstanceClass.BURSTABLE4_GRAVITON, InstanceSize.MICRO),
            Vpc = Vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
            Credentials = Credentials.FromGeneratedSecret("storyplatform_admin"),
            DatabaseName = "storyplatform",
            MultiAz = false,
            AllocatedStorage = 20,
            PubliclyAccessible = false,
            RemovalPolicy = RemovalPolicy.DESTROY,
            DeletionProtection = false
        });
    }
```

> Note: `RemovalPolicy.DESTROY` + `DeletionProtection = false` means `cdk destroy` deletes the database and its data permanently. This is intentional — the user explicitly wants fast, complete teardown for this demo/capstone environment.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add infra/aws-cdk/
git commit -m "infra: add private RDS PostgreSQL instance"
```

---

### Task 4: ECR repository

**Files:**
- Modify: `infra/aws-cdk/src/StoryPlatformCoreStack.cs`
- Modify: `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`

**Interfaces:**
- Consumes: nothing from prior tasks (independent resource).
- Produces: `public Repository EcrRepository { get; }` — Task 8 (App Runner) reads `EcrRepository.RepositoryUri` / grants pull access.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void Stack_CreatesEcrRepositoryWithLifecycleRule()
    {
        var template = SynthTemplate();
        template.ResourceCountIs("AWS::ECR::Repository", 1);
        template.HasResourceProperties("AWS::ECR::Repository", new System.Collections.Generic.Dictionary<string, object>
        {
            ["RepositoryName"] = "storyplatform-core-api"
        });
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: FAIL (0 ECR repositories found)

- [ ] **Step 3: Implement the ECR repository**

```csharp
using Amazon.CDK.AWS.ECR;
```

```csharp
    public Repository EcrRepository { get; }
```

```csharp
        EcrRepository = new Repository(this, "CoreApiRepository", new RepositoryProps
        {
            RepositoryName = "storyplatform-core-api",
            RemovalPolicy = RemovalPolicy.DESTROY,
            EmptyOnDelete = true,
            LifecycleRules = new[]
            {
                new LifecycleRule
                {
                    MaxImageCount = 10,
                    Description = "Keep only the last 10 images"
                }
            }
        });
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS (5 tests)

- [ ] **Step 5: Commit**

```bash
git add infra/aws-cdk/
git commit -m "infra: add ECR repository for Core API images"
```

---

### Task 5: Secrets Manager secrets (DB connection string, JWT, Resend, SePay)

**Files:**
- Modify: `infra/aws-cdk/src/StoryPlatformCoreStack.cs`
- Modify: `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`

**Interfaces:**
- Consumes: `Database` (Task 3) — `Database.DbInstanceEndpointAddress`, `Database.DbInstanceEndpointPort`, `Database.Secret!.SecretValueFromJson(...)`.
- Produces: `public Secret DbConnectionSecret { get; }`, `public Secret JwtSecret { get; }`, `public Secret ResendApiKeySecret { get; }`, `public Secret SePayApiKeySecret { get; }` — Task 6 (IAM) and Task 8 (App Runner `RuntimeEnvironmentSecrets`) reference these by name/ARN.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void Stack_CreatesFourApplicationSecrets()
    {
        var template = SynthTemplate();
        // 5 total: 1 auto-created by Database's Credentials.FromGeneratedSecret (Task 3)
        // + 4 app secrets created in this task (db-connection-string, jwt, resend, sepay).
        template.ResourceCountIs("AWS::SecretsManager::Secret", 5);
        template.HasResourceProperties("AWS::SecretsManager::Secret", new System.Collections.Generic.Dictionary<string, object>
        {
            ["Name"] = "storyplatform/core/jwt-secret-key"
        });
        template.HasResourceProperties("AWS::SecretsManager::Secret", new System.Collections.Generic.Dictionary<string, object>
        {
            ["Name"] = "storyplatform/core/db-connection-string"
        });
        template.HasResourceProperties("AWS::SecretsManager::Secret", new System.Collections.Generic.Dictionary<string, object>
        {
            ["Name"] = "storyplatform/core/resend-api-key"
        });
        template.HasResourceProperties("AWS::SecretsManager::Secret", new System.Collections.Generic.Dictionary<string, object>
        {
            ["Name"] = "storyplatform/core/sepay-api-key"
        });
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: FAIL (named secrets not found)

- [ ] **Step 3: Implement the secrets**

```csharp
using System;
using System.Security.Cryptography;
using Amazon.CDK.AWS.SecretsManager;
```

```csharp
    public Secret DbConnectionSecret { get; }
    public Secret JwtSecret { get; }
    public Secret ResendApiKeySecret { get; }
    public Secret SePayApiKeySecret { get; }
```

```csharp
        var dbUsername = Database.Secret!.SecretValueFromJson("username").UnsafeUnwrap();
        var dbPassword = Database.Secret!.SecretValueFromJson("password").UnsafeUnwrap();
        var connectionString =
            $"Host={Database.DbInstanceEndpointAddress};Port={Database.DbInstanceEndpointPort};" +
            $"Database=storyplatform;Username={dbUsername};Password={dbPassword}";

        DbConnectionSecret = new Secret(this, "DbConnectionSecret", new SecretProps
        {
            SecretName = "storyplatform/core/db-connection-string",
            SecretStringValue = SecretValue.UnsafePlainText(connectionString),
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        JwtSecret = new Secret(this, "JwtSecret", new SecretProps
        {
            SecretName = "storyplatform/core/jwt-secret-key",
            SecretStringValue = SecretValue.UnsafePlainText(GenerateRandomSecret()),
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        ResendApiKeySecret = new Secret(this, "ResendApiKeySecret", new SecretProps
        {
            SecretName = "storyplatform/core/resend-api-key",
            SecretStringValue = SecretValue.UnsafePlainText("REPLACE_ME_POST_DEPLOY"),
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        SePayApiKeySecret = new Secret(this, "SePayApiKeySecret", new SecretProps
        {
            SecretName = "storyplatform/core/sepay-api-key",
            SecretStringValue = SecretValue.UnsafePlainText("REPLACE_ME_POST_DEPLOY"),
            RemovalPolicy = RemovalPolicy.DESTROY
        });
```

Add the helper method at the bottom of the class:

```csharp
    private static string GenerateRandomSecret(int byteLength = 48)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS (all tests, including the new one)

- [ ] **Step 5: Commit**

```bash
git add infra/aws-cdk/
git commit -m "infra: add Secrets Manager secrets for DB connection string, JWT, Resend, SePay"
```

---

### Task 6: IAM instance role for App Runner (least-privilege secret read)

**Files:**
- Modify: `infra/aws-cdk/src/StoryPlatformCoreStack.cs`
- Modify: `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`

**Interfaces:**
- Consumes: `DbConnectionSecret`, `JwtSecret`, `ResendApiKeySecret`, `SePayApiKeySecret` (Task 5).
- Produces: `public Role AppRunnerInstanceRole { get; }` — Task 8 (App Runner service) assigns this as its instance role.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void Stack_CreatesAppRunnerInstanceRoleScopedToSecrets()
    {
        var template = SynthTemplate();
        template.HasResourceProperties("AWS::IAM::Role", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["AssumeRolePolicyDocument"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
            {
                ["Statement"] = Match.ArrayWith(new object[]
                {
                    Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["Principal"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["Service"] = "tasks.apprunner.amazonaws.com"
                        })
                    })
                })
            })
        }));

        template.HasResourceProperties("AWS::IAM::Policy", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["PolicyDocument"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
            {
                ["Statement"] = Match.ArrayWith(new object[]
                {
                    Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["Action"] = "secretsmanager:GetSecretValue"
                    })
                })
            })
        }));
    }
```

Add `using Amazon.CDK.Assertions;` (for `Match`) to the test file if not already present.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: FAIL (no matching IAM role/policy found)

- [ ] **Step 3: Implement the IAM role**

```csharp
using Amazon.CDK.AWS.IAM;
```

```csharp
    public Role AppRunnerInstanceRole { get; }
```

```csharp
        AppRunnerInstanceRole = new Role(this, "AppRunnerInstanceRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("tasks.apprunner.amazonaws.com")
        });

        DbConnectionSecret.GrantRead(AppRunnerInstanceRole);
        JwtSecret.GrantRead(AppRunnerInstanceRole);
        ResendApiKeySecret.GrantRead(AppRunnerInstanceRole);
        SePayApiKeySecret.GrantRead(AppRunnerInstanceRole);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add infra/aws-cdk/
git commit -m "infra: add least-privilege IAM instance role for App Runner"
```

---

### Task 7: VPC Connector + RDS security group rule

**Files:**
- Modify: `infra/aws-cdk/src/StoryPlatformCoreStack.cs`
- Modify: `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`

**Interfaces:**
- Consumes: `Vpc` (Task 2), `Database` (Task 3).
- Produces: `public CfnVpcConnector VpcConnector { get; }` — Task 8 references `VpcConnector.AttrVpcConnectorArn`.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void Stack_CreatesVpcConnectorAllowedIntoRds()
    {
        var template = SynthTemplate();
        template.ResourceCountIs("AWS::AppRunner::VpcConnector", 1);
        template.HasResourceProperties("AWS::EC2::SecurityGroupIngress", new System.Collections.Generic.Dictionary<string, object>
        {
            ["FromPort"] = 5432,
            ["ToPort"] = 5432
        });
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: FAIL (no VpcConnector resource found)

- [ ] **Step 3: Implement the VPC Connector and security group rule**

```csharp
using Amazon.CDK.AWS.AppRunner;
```

```csharp
    public CfnVpcConnector VpcConnector { get; }
```

```csharp
        var vpcConnectorSecurityGroup = new SecurityGroup(this, "VpcConnectorSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            Description = "Security group for the App Runner VPC Connector",
            AllowAllOutbound = true
        });

        Database.Connections.AllowFrom(vpcConnectorSecurityGroup, Port.Tcp(5432), "Allow App Runner VPC Connector to reach RDS");

        VpcConnector = new CfnVpcConnector(this, "AppRunnerVpcConnector", new CfnVpcConnectorProps
        {
            VpcConnectorName = "storyplatform-core-connector",
            Subnets = Vpc.SelectSubnets(new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED }).SubnetIds,
            SecurityGroups = new[] { vpcConnectorSecurityGroup.SecurityGroupId }
        });
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add infra/aws-cdk/
git commit -m "infra: add App Runner VPC Connector with RDS security group rule"
```

---

### Task 8: App Runner service (bootstrap-gated behind a context flag)

**Bootstrap problem this task must handle:** the App Runner Service resource references `{EcrRepository.RepositoryUri}:latest`. On the very first `cdk deploy`, that ECR repo is empty (no image pushed yet, because you can't push to a repo that doesn't exist until the stack that creates it has deployed). If the App Runner Service resource is created unconditionally in the same `cdk deploy` as the ECR repo, CloudFormation fails creating the service (image not found) and **rolls back the entire stack**, deleting the VPC/RDS/ECR/Secrets that succeeded too. To avoid this, App Runner Service creation is gated behind a CDK context flag `includeAppRunnerService` (default `false`). Task 13 deploys twice: once with the flag off (everything except App Runner), then pushes the first image, then deploys again with the flag on (App Runner now finds a real image).

**Files:**
- Modify: `infra/aws-cdk/src/StoryPlatformCoreStack.cs`
- Modify: `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`

**Interfaces:**
- Consumes: `EcrRepository` (Task 4), `DbConnectionSecret`/`JwtSecret`/`ResendApiKeySecret`/`SePayApiKeySecret` (Task 5), `AppRunnerInstanceRole` (Task 6), `VpcConnector` (Task 7), `SynthTemplate(context)` (Task 1).
- Produces: `public CfnService? AppRunnerService { get; }` (nullable — `null` when `includeAppRunnerService` context is not set/false).

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void Stack_OmitsAppRunnerServiceByDefault()
    {
        var template = SynthTemplate();
        template.ResourceCountIs("AWS::AppRunner::Service", 0);
    }

    [Fact]
    public void Stack_CreatesAppRunnerServiceWithAutoDeploy_WhenContextFlagEnabled()
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
                ["AutoDeploymentsEnabled"] = true
            })
        });
    }
```

- [ ] **Step 2: Run tests to verify the second one fails**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: `Stack_OmitsAppRunnerServiceByDefault` PASSES trivially (no App Runner code exists yet at all, so 0 is already true); `Stack_CreatesAppRunnerServiceWithAutoDeploy_WhenContextFlagEnabled` FAILS (0 resources found, expected 1).

- [ ] **Step 3: Implement the App Runner service and its access role, gated behind the context flag**

App Runner needs a separate **access role** (for pulling from ECR) distinct from the **instance role** (Task 6, for reading secrets at runtime). Both the access role and the service itself only need to exist once the image is real, so both go inside the `if`:

```csharp
        var includeAppRunnerServiceContext = Node.TryGetContext("includeAppRunnerService");
        var includeAppRunnerService = includeAppRunnerServiceContext switch
        {
            bool b => b,
            string s => bool.Parse(s),
            _ => false
        };

        if (includeAppRunnerService)
        {
            var appRunnerEcrAccessRole = new Role(this, "AppRunnerEcrAccessRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("build.apprunner.amazonaws.com")
            });
            EcrRepository.GrantPull(appRunnerEcrAccessRole);

            AppRunnerService = new CfnService(this, "CoreApiService", new CfnServiceProps
            {
                ServiceName = "storyplatform-core-api",
                SourceConfiguration = new CfnService.SourceConfigurationProperty
                {
                    AutoDeploymentsEnabled = true,
                    AuthenticationConfiguration = new CfnService.AuthenticationConfigurationProperty
                    {
                        AccessRoleArn = appRunnerEcrAccessRole.RoleArn
                    },
                    ImageRepository = new CfnService.ImageRepositoryProperty
                    {
                        ImageIdentifier = $"{EcrRepository.RepositoryUri}:latest",
                        ImageRepositoryType = "ECR",
                        ImageConfiguration = new CfnService.ImageConfigurationProperty
                        {
                            Port = "8080",
                            RuntimeEnvironmentVariables = new[]
                            {
                                new CfnService.KeyValuePairProperty { Name = "ASPNETCORE_ENVIRONMENT", Value = "Production" }
                            },
                            RuntimeEnvironmentSecrets = new[]
                            {
                                new CfnService.KeyValuePairProperty { Name = "ConnectionStrings__DefaultConnection", Value = DbConnectionSecret.SecretArn },
                                new CfnService.KeyValuePairProperty { Name = "JwtSettings__SecretKey", Value = JwtSecret.SecretArn },
                                new CfnService.KeyValuePairProperty { Name = "ResendSettings__ApiKey", Value = ResendApiKeySecret.SecretArn },
                                new CfnService.KeyValuePairProperty { Name = "SePaySettings__ApiKey", Value = SePayApiKeySecret.SecretArn }
                            }
                        }
                    }
                },
                InstanceConfiguration = new CfnService.InstanceConfigurationProperty
                {
                    Cpu = "1024",
                    Memory = "2048",
                    InstanceRoleArn = AppRunnerInstanceRole.RoleArn
                },
                NetworkConfiguration = new CfnService.NetworkConfigurationProperty
                {
                    EgressConfiguration = new CfnService.EgressConfigurationProperty
                    {
                        EgressType = "VPC",
                        VpcConnectorArn = VpcConnector.AttrVpcConnectorArn
                    }
                }
            });
        }
```

Add the property declaration near the top of the class, alongside the others (`Vpc`, `Database`, `EcrRepository`, etc.) — nullable, since it's only assigned inside the `if`:

```csharp
    public CfnService? AppRunnerService { get; private set; }
```

> This one property needs `private set` (unlike the others) because it's conditionally assigned — a plain get-only auto-property can still be assigned from within the constructor's `if` block in C#, but declaring `private set` here makes the conditional-assignment intent explicit to a reader.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS (both tests)

- [ ] **Step 5: Commit**

```bash
git add infra/aws-cdk/
git commit -m "infra: add App Runner service gated behind includeAppRunnerService context flag"
```

---

### Task 9: Dockerfile for Core API

**Files:**
- Create: `src/Core/StoryPlatform.Api/Dockerfile`
- Create: `.dockerignore` (repo root)

**Interfaces:**
- Consumes: nothing (standalone build artifact).
- Produces: a Docker image that listens on port 8080 — consumed by Task 8's App Runner `Port: "8080"` config and Task 10's CI workflow (`docker build`/`docker push`).

- [ ] **Step 1: Create `.dockerignore` at repo root**

```
# .dockerignore
**/bin/
**/obj/
**/.vs/
**/node_modules/
**/*.user
.git/
docs/
infra/
tests/
*.docx
```

- [ ] **Step 2: Create the Dockerfile**

```dockerfile
# src/Core/StoryPlatform.Api/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj src/Core/StoryPlatform.Api/
COPY src/Core/StoryPlatform.Application/StoryPlatform.Application.csproj src/Core/StoryPlatform.Application/
COPY src/Core/StoryPlatform.Domain/StoryPlatform.Domain.csproj src/Core/StoryPlatform.Domain/
COPY src/Core/StoryPlatform.Infrastructure/StoryPlatform.Infrastructure.csproj src/Core/StoryPlatform.Infrastructure/
COPY src/Shared/StoryPlatform.Contracts/StoryPlatform.Contracts.csproj src/Shared/StoryPlatform.Contracts/

RUN dotnet restore src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj

COPY src/Core/StoryPlatform.Api/ src/Core/StoryPlatform.Api/
COPY src/Core/StoryPlatform.Application/ src/Core/StoryPlatform.Application/
COPY src/Core/StoryPlatform.Domain/ src/Core/StoryPlatform.Domain/
COPY src/Core/StoryPlatform.Infrastructure/ src/Core/StoryPlatform.Infrastructure/
COPY src/Shared/StoryPlatform.Contracts/ src/Shared/StoryPlatform.Contracts/

RUN dotnet publish src/Core/StoryPlatform.Api/StoryPlatform.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "StoryPlatform.Api.dll"]
```

> Note: build context for this Dockerfile must be the **repo root** (not the `StoryPlatform.Api` folder), since it copies sibling projects (`Application`, `Domain`, `Infrastructure`, `Contracts`). Task 10's `docker build` command sets `--file src/Core/StoryPlatform.Api/Dockerfile .` accordingly.

- [ ] **Step 3: Verify the image builds locally**

Run: `docker build --file src/Core/StoryPlatform.Api/Dockerfile --tag storyplatform-core-api:local .`
Expected: build completes successfully (exit code 0).

- [ ] **Step 4: Verify the container starts and responds**

Run: `docker run --rm -d --name core-api-smoke-test -p 8080:8080 -e ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=x;Username=x;Password=x" -e JwtSettings__SecretKey="smoke-test-key-not-real" storyplatform-core-api:local`

Then: `curl -f http://localhost:8080/swagger/index.html`
Expected: HTTP 200 (Swagger UI loads even though the DB connection string is fake — only DB-touching endpoints will fail, which is expected at this stage).

Cleanup: `docker stop core-api-smoke-test`

- [ ] **Step 5: Commit**

```bash
git add src/Core/StoryPlatform.Api/Dockerfile .dockerignore
git commit -m "build: add multi-stage Dockerfile for Core API"
```

---

### Task 10: GitHub OIDC provider + CI IAM role

**Files:**
- Modify: `infra/aws-cdk/src/StoryPlatformCoreStack.cs`
- Modify: `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`

**Interfaces:**
- Consumes: `EcrRepository` (Task 4).
- Produces: `public Role CiRole { get; }` — its `RoleArn` is what the user pastes into the GitHub Actions workflow (Task 11) as `role-to-assume`. Also printed as a `CfnOutput` so `cdk deploy` output shows it directly.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void Stack_CreatesGitHubOidcProviderAndScopedCiRole()
    {
        var template = SynthTemplate();
        template.ResourceCountIs("AWS::IAM::OIDCProvider", 1);
        template.HasResourceProperties("AWS::IAM::Role", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["AssumeRolePolicyDocument"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
            {
                ["Statement"] = Match.ArrayWith(new object[]
                {
                    Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["Action"] = "sts:AssumeRoleWithWebIdentity"
                    })
                })
            })
        }));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: FAIL (no OIDC provider found)

- [ ] **Step 3: Implement the OIDC provider and CI role**

```csharp
using Amazon.CDK.AWS.IAM;
```

```csharp
    public Role CiRole { get; }
```

```csharp
        var githubOidcProvider = new OpenIdConnectProvider(this, "GitHubOidcProvider", new OpenIdConnectProviderProps
        {
            Url = "https://token.actions.githubusercontent.com",
            ClientIds = new[] { "sts.amazonaws.com" }
        });

        const string githubRepo = "FA26SE142-GFA26SE04-AI-Storytelling/AI_Storytelling_Backend"; // confirmed via `git remote -v` (origin)

        CiRole = new Role(this, "GitHubActionsCiRole", new RoleProps
        {
            RoleName = "storyplatform-core-api-ci",
            AssumedBy = new WebIdentityPrincipal(githubOidcProvider.OpenIdConnectProviderArn, new System.Collections.Generic.Dictionary<string, object>
            {
                ["StringEquals"] = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["token.actions.githubusercontent.com:aud"] = "sts.amazonaws.com"
                },
                ["StringLike"] = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["token.actions.githubusercontent.com:sub"] = new[]
                    {
                        $"repo:{githubRepo}:ref:refs/heads/dev",
                        $"repo:{githubRepo}:ref:refs/heads/main"
                    }
                }
            })
        });

        EcrRepository.GrantPullPush(CiRole);

        new CfnOutput(this, "CiRoleArnOutput", new CfnOutputProps
        {
            Value = CiRole.RoleArn,
            Description = "Paste this ARN into the GitHub Actions workflow's role-to-assume input"
        });
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add infra/aws-cdk/
git commit -m "infra: add GitHub OIDC provider and scoped CI IAM role"
```

---

### Task 11: GitHub Actions CI/CD workflow

**Files:**
- Create: `.github/workflows/deploy-core-api.yml`

**Interfaces:**
- Consumes: `CiRole`'s ARN — deterministic from Task 10 (`RoleName = "storyplatform-core-api-ci"` + known account `028718096070`), so it's hardcoded directly in the workflow below rather than left for later.
- Produces: nothing consumed by later tasks — this is the terminal CI/CD artifact.

- [ ] **Step 1: Create the workflow file**

```yaml
# .github/workflows/deploy-core-api.yml
name: Deploy Core API

on:
  push:
    branches: [dev, main]
    paths:
      - 'src/Core/**'
      - 'src/Shared/**'
      - '.github/workflows/deploy-core-api.yml'

permissions:
  id-token: write
  contents: read

env:
  AWS_REGION: ap-southeast-1
  ECR_REPOSITORY: storyplatform-core-api

jobs:
  build-and-push:
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
            --file src/Core/StoryPlatform.Api/Dockerfile \
            --tag "$ECR_REGISTRY/$ECR_REPOSITORY:$IMAGE_TAG" \
            --tag "$ECR_REGISTRY/$ECR_REPOSITORY:latest" \
            .
          docker push "$ECR_REGISTRY/$ECR_REPOSITORY:$IMAGE_TAG"
          docker push "$ECR_REGISTRY/$ECR_REPOSITORY:latest"
```

> The `role-to-assume` value (`arn:aws:iam::028718096070:role/storyplatform-core-api-ci`) matches the `RoleName` set in Task 10 (`storyplatform-core-api-ci`) combined with the known account ID — no placeholder needed here since both values are already fixed by earlier tasks. No explicit App Runner deploy step is needed: `AutoDeploymentsEnabled: true` (Task 8) makes App Runner redeploy automatically once it detects the new `latest` image digest in ECR.

- [ ] **Step 2: Validate YAML syntax**

Run: `python -c "import yaml; yaml.safe_load(open('.github/workflows/deploy-core-api.yml'))"`
Expected: no error printed (valid YAML).

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/deploy-core-api.yml
git commit -m "ci: add GitHub Actions workflow to build and push Core API image via OIDC"
```

---

### Task 12: `cdk synth` full-stack validation (no deploy)

**Files:**
- None created/modified — validation-only task.

**Interfaces:**
- Consumes: the complete stack from Tasks 1–10.
- Produces: confidence that `cdk deploy` (Task 13) will not fail on template synthesis errors.

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS (all tests from Tasks 1–10)

- [ ] **Step 2: Run `cdk synth` — phase 1 (no App Runner, matches the first real deploy)**

Run (from `infra/aws-cdk/`): `cdk synth --profile storyplatform`
Expected: prints a valid CloudFormation template to stdout with no errors. Review the output for: exactly one VPC, one RDS instance, one ECR repo, 5 Secrets Manager secrets, one VPC Connector, **zero** App Runner services (context flag defaults to false), one OIDC provider, one CI role.

- [ ] **Step 3: Run `cdk synth` — phase 2 (with App Runner, matches the second real deploy)**

Run (from `infra/aws-cdk/`): `cdk synth --profile storyplatform --context includeAppRunnerService=true`
Expected: same as phase 1, plus exactly one `AWS::AppRunner::Service` and its ECR access role.

- [ ] **Step 4: Report findings**

If `cdk synth` fails, fix the reported construct/property error in `StoryPlatformCoreStack.cs`, re-run the affected task's tests, then re-run `cdk synth` until clean. Do not proceed to Task 13 until `cdk synth` is clean.

(No commit — this task produces no file changes unless a fix was needed, in which case commit that fix with a message describing what `cdk synth` caught.)

---

### Task 13: `cdk bootstrap` + `cdk deploy` (real AWS resources — requires explicit user confirmation)

**Files:**
- Modify: `infra/aws-cdk/cdk.json` (Step 7 only, to persist the bootstrap flag).
- Otherwise: no repo files — this task provisions real AWS resources.

**Interfaces:**
- Consumes: the validated stack (Task 12).
- Produces: a running App Runner service, RDS instance, ECR repo, etc. in AWS account `028718096070`.

> **STOP: this task creates real, billed AWS resources and must not run without the user explicitly confirming immediately before the `cdk deploy` command.** Show the user the `cdk diff` output first and get an explicit "go ahead" before running `cdk deploy`.

- [ ] **Step 1: Bootstrap the CDK toolkit (one-time per account/region)**

Run: `cdk bootstrap aws://028718096070/ap-southeast-1 --profile storyplatform`
Expected: creates the CDK bootstrap stack (`CDKToolkit`) if not already present; safe to re-run (no-op if already bootstrapped).

- [ ] **Step 2: Preview phase-1 changes (no App Runner yet)**

Run (from `infra/aws-cdk/`): `cdk diff --profile storyplatform`
Show the full diff output to the user.

- [ ] **Step 3: Get explicit user confirmation**

Ask the user directly: "This will create RDS, VPC, ECR, Secrets Manager, and IAM resources as shown above (~$12–25+/month). App Runner itself is deployed in a second step after the first image is pushed. Confirm to proceed with `cdk deploy`?" Do not proceed without an explicit yes.

- [ ] **Step 4: Deploy — phase 1 (VPC, RDS, ECR, Secrets, IAM, OIDC — no App Runner)**

Run: `cdk deploy --profile storyplatform --require-approval broadcast`
Expected: stack creates successfully (App Runner Service is absent because `includeAppRunnerService` defaults to `false` — this is intentional, see Task 8). Note the `CiRoleArnOutput` value printed at the end (should match the ARN already hardcoded in Task 11's workflow — confirm they match; if the account ID or role name differs, update Task 11's YAML accordingly).

- [ ] **Step 5: Push the first image (ECR repo now exists from phase 1)**

Run (from repo root):
```bash
aws ecr get-login-password --region ap-southeast-1 --profile storyplatform | docker login --username AWS --password-stdin 028718096070.dkr.ecr.ap-southeast-1.amazonaws.com
docker build --file src/Core/StoryPlatform.Api/Dockerfile --tag 028718096070.dkr.ecr.ap-southeast-1.amazonaws.com/storyplatform-core-api:latest .
docker push 028718096070.dkr.ecr.ap-southeast-1.amazonaws.com/storyplatform-core-api:latest
```

- [ ] **Step 6: Deploy — phase 2 (add App Runner, now that a real image exists)**

Preview first: `cdk diff --profile storyplatform --context includeAppRunnerService=true`, show the user the diff (it should show only the new App Runner Service + its ECR access role being added), get confirmation, then:

Run: `cdk deploy --profile storyplatform --context includeAppRunnerService=true --require-approval broadcast`
Expected: App Runner Service creates successfully this time, since `{EcrRepository.RepositoryUri}:latest` now resolves to a real image.

- [ ] **Step 7: Persist the flag so future deploys don't need to remember `--context`**

Edit `infra/aws-cdk/cdk.json` to add the flag as a permanent default, so a future `cdk deploy` (without the CLI flag) doesn't accidentally omit — and therefore delete — the App Runner Service:

```json
{
  "app": "dotnet run --project src/StoryPlatform.Infra.csproj",
  "context": {
    "includeAppRunnerService": true
  }
}
```

Commit this change: `git add infra/aws-cdk/cdk.json && git commit -m "infra: persist includeAppRunnerService=true after successful bootstrap"`

- [ ] **Step 8: Verify the deployed service**

Get the App Runner service URL: `aws apprunner describe-service --service-arn <arn from cdk deploy output or aws apprunner list-services> --region ap-southeast-1 --profile storyplatform --query "Service.ServiceUrl" --output text`

Then: `curl -f https://<service-url>/swagger/index.html`
Expected: HTTP 200.

- [ ] **Step 9: Manually set the real Resend and SePay secret values**

Tell the user to run these themselves (Claude does not enter real third-party API keys):
```bash
aws secretsmanager put-secret-value --secret-id storyplatform/core/resend-api-key --secret-string "<real-resend-key>" --region ap-southeast-1 --profile storyplatform
aws secretsmanager put-secret-value --secret-id storyplatform/core/sepay-api-key --secret-string "<real-sepay-key>" --region ap-southeast-1 --profile storyplatform
```
After updating, restart the App Runner service so it picks up the new secret values: `aws apprunner start-deployment --service-arn <arn> --region ap-southeast-1 --profile storyplatform`

(The only repo commit in this task is Step 7's `cdk.json` update — the rest are AWS-side operations with no repo file changes.)

---

## Out of scope (do not implement in this plan)

- Applying EF Core migrations to RDS.
- Deploying `StoryPlatform.AI.Api`.
- Custom domain / Route 53 / ACM.
- S3 / CloudFront for media.

These are follow-up tasks per the spec's §14.
