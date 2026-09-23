# ECS Fargate Cost-Optimized Migration (+ External Redis Cache) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Core API's AWS App Runner compute layer with a cost-optimized ECS Fargate service (ARM64, no NAT Gateway, no ALB), consolidate Secrets Manager to one secret, and wire in an external Redis cache — cutting the account's real AWS spend from ~$66/month to ~$36.95/month to fit a $190/5-month budget.

**Architecture:** One CDK stack (`StoryPlatformCoreStack`) swaps App Runner + VPC Connector + NAT Gateway for an ECS Fargate Cluster/TaskDefinition/Service running in a public subnet with its own public IP; RDS stays private and unchanged; a new `AddRedisCache` extension wires `IDistributedCache` against an externally-hosted Redis provider reached over TLS, never an AWS resource.

**Tech Stack:** AWS CDK (C#, aws-cdk-lib 2.170.0), Amazon ECS on Fargate, .NET 10 / ASP.NET Core, StackExchange.Redis, GitHub Actions, xUnit + CDK `Amazon.CDK.Assertions`.

**Spec:** `docs/superpowers/specs/2026-09-22-ecs-fargate-cost-optimized-migration-design.md`

## Global Constraints

- Region/account: `ap-southeast-1` / `028718096070`, AWS CLI profile `storyplatform` (spec §3, RUNBOOK.md).
- API Fargate task: 0.25 vCPU / 1GB, ARM64 (`RuntimePlatform.CpuArchitecture = ARM64`) — spec §5.6.
- No NAT Gateway, no ALB anywhere in this stack — spec §1, §5.1.
- RDS (`db.t4g.micro`, Single-AZ, 20GB, `PRIVATE_ISOLATED`) is structurally unchanged — spec §5.2.
- Secrets Manager: exactly 1 consolidated secret (`storyplatform/core/app-secrets`) with 5 JSON keys (`DbConnectionString`, `JwtSecretKey`, `ResendApiKey`, `SePayApiKey`, `RedisConnectionString`) — spec §5.4; keep Secrets Manager, not SSM, per spec §13 decision log.
- Redis is an external managed provider (e.g. Upstash/Redis Cloud free tier), never an AWS-hosted resource — spec §5.8.
- CloudWatch log retention: 7 days; log level `Warning` in Production — spec §5.6.
- Container health check required, targeting `/health` — spec §5.6.
- `IDistributedCache`/Redis calls must fail fast (`AbortOnConnectFail=false`, short timeouts) — spec §8.
- The production build uses the **root** `Dockerfile` (`--target core-api`), not `src/Core/StoryPlatform.Api/Dockerfile` — confirmed by reading `.github/workflows/deploy-core-api.yml` and the RUNBOOK. It already installs `curl` in the `runtime` stage, so no Dockerfile content change is needed for the container health check.

---

## Task 1: Consolidate Secrets Manager into one secret

**Files:**
- Modify: `infra/aws-cdk/src/StoryPlatformCoreStack.cs`
- Test: `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`

**Interfaces:**
- Produces: `public Secret AppSecrets { get; }` on `StoryPlatformCoreStack`, replacing the four separate secret properties (`DbConnectionSecret`, `JwtSecret`, `ResendApiKeySecret`, `SePayApiKeySecret`). Later tasks reference `AppSecrets` (e.g. `AppSecrets.GrantRead(...)`, `Secret.FromSecretsManager(AppSecrets, "FieldName")`).

- [ ] **Step 1: Write the failing test**

Replace the existing `Stack_CreatesFourApplicationSecrets` test with:

```csharp
    [Fact]
    public void Stack_CreatesOneConsolidatedAppSecret()
    {
        var template = SynthTemplate();
        // 2 total: 1 auto-created by Database's Credentials.FromGeneratedSecret (unchanged)
        // + 1 consolidated app secret (was 4 separate ones) created in this task.
        template.ResourceCountIs("AWS::SecretsManager::Secret", 2);
        template.HasResourceProperties("AWS::SecretsManager::Secret", new System.Collections.Generic.Dictionary<string, object>
        {
            ["Name"] = "storyplatform/core/app-secrets",
            ["GenerateSecretString"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
            {
                ["GenerateStringKey"] = "JwtSecretKey"
            })
        });
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj --filter Stack_CreatesOneConsolidatedAppSecret`
Expected: FAIL (old code still creates 4 app secrets + 1 RDS secret = 5, named differently).

- [ ] **Step 3: Replace the 4-secret block with 1 consolidated secret**

In `infra/aws-cdk/src/StoryPlatformCoreStack.cs`, replace the property declarations:

```csharp
    public Secret DbConnectionSecret { get; }
    public Secret JwtSecret { get; }
    public Secret ResendApiKeySecret { get; }
    public Secret SePayApiKeySecret { get; }
```

with:

```csharp
    public Secret AppSecrets { get; }
```

Replace the secret-construction block (currently right after `EcrRepository = new Repository(...)`):

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
            GenerateSecretString = new SecretStringGenerator
            {
                PasswordLength = 64,
                ExcludePunctuation = true
            },
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

with:

```csharp
        var dbUsername = Database.Secret!.SecretValueFromJson("username").UnsafeUnwrap();
        var dbPassword = Database.Secret!.SecretValueFromJson("password").UnsafeUnwrap();
        var connectionString =
            $"Host={Database.DbInstanceEndpointAddress};Port={Database.DbInstanceEndpointPort};" +
            $"Database=storyplatform;Username={dbUsername};Password={dbPassword}";

        // Consolidated into 1 secret (was 4 separate ones) to cut Secrets Manager cost from
        // ~$1.60/month to ~$0.40/month — Secrets Manager bills per secret, not per JSON key.
        // GenerateStringKey lets Secrets Manager generate JwtSecretKey server-side (it never
        // appears in the synthesized CloudFormation template) and merge it into this JSON
        // template, so DbConnectionString stays composed from RDS's own generated credentials
        // exactly as before; only Resend/SePay/Redis need a manual
        // `aws secretsmanager put-secret-value` after deploy. RDS's generated password is
        // excluded from quote/backslash characters by Secrets Manager's own default
        // ExcludeCharacters, so embedding it in this hand-built JSON string is safe without
        // extra escaping.
        var appSecretsTemplate =
            "{" +
            $"\"DbConnectionString\":\"{connectionString}\"," +
            "\"ResendApiKey\":\"REPLACE_ME_POST_DEPLOY\"," +
            "\"SePayApiKey\":\"REPLACE_ME_POST_DEPLOY\"," +
            "\"RedisConnectionString\":\"REPLACE_ME_POST_DEPLOY\"" +
            "}";

        AppSecrets = new Secret(this, "AppSecrets", new SecretProps
        {
            SecretName = "storyplatform/core/app-secrets",
            GenerateSecretString = new SecretStringGenerator
            {
                SecretStringTemplate = appSecretsTemplate,
                GenerateStringKey = "JwtSecretKey",
                ExcludePunctuation = true,
                PasswordLength = 64
            },
            RemovalPolicy = RemovalPolicy.DESTROY
        });
```

Then update the two places later in the file that still reference the old 4 properties (App Runner is still active until Task 4, so it must keep compiling against the new secret):

Replace:

```csharp
        DbConnectionSecret.GrantRead(AppRunnerInstanceRole);
        JwtSecret.GrantRead(AppRunnerInstanceRole);
        ResendApiKeySecret.GrantRead(AppRunnerInstanceRole);
        SePayApiKeySecret.GrantRead(AppRunnerInstanceRole);
```

with:

```csharp
        AppSecrets.GrantRead(AppRunnerInstanceRole);
```

Replace:

```csharp
                            RuntimeEnvironmentSecrets = new[]
                            {
                                new CfnService.KeyValuePairProperty { Name = "ConnectionStrings__DefaultConnection", Value = DbConnectionSecret.SecretArn },
                                new CfnService.KeyValuePairProperty { Name = "JwtSettings__SecretKey", Value = JwtSecret.SecretArn },
                                new CfnService.KeyValuePairProperty { Name = "ResendSettings__ApiKey", Value = ResendApiKeySecret.SecretArn },
                                new CfnService.KeyValuePairProperty { Name = "SePaySettings__ApiKey", Value = SePayApiKeySecret.SecretArn }
                            }
```

with:

```csharp
                            RuntimeEnvironmentSecrets = new[]
                            {
                                new CfnService.KeyValuePairProperty { Name = "ConnectionStrings__DefaultConnection", Value = $"{AppSecrets.SecretArn}:DbConnectionString::" },
                                new CfnService.KeyValuePairProperty { Name = "JwtSettings__SecretKey", Value = $"{AppSecrets.SecretArn}:JwtSecretKey::" },
                                new CfnService.KeyValuePairProperty { Name = "ResendSettings__ApiKey", Value = $"{AppSecrets.SecretArn}:ResendApiKey::" },
                                new CfnService.KeyValuePairProperty { Name = "SePaySettings__ApiKey", Value = $"{AppSecrets.SecretArn}:SePayApiKey::" }
                            }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj --filter Stack_CreatesOneConsolidatedAppSecret`
Expected: PASS

- [ ] **Step 5: Run the full CDK test project to confirm nothing else broke**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS (all tests, including `Stack_CreatesAppRunnerInstanceRoleScopedToSecrets`, which should still pass unchanged since `AppSecrets.GrantRead(...)` still produces a `secretsmanager:GetSecretValue` policy statement)

- [ ] **Step 6: Commit**

```bash
git add infra/aws-cdk/src/StoryPlatformCoreStack.cs infra/aws-cdk/test/StoryPlatformCoreStackTests.cs
git commit -m "refactor(infra): consolidate 4 app secrets into 1 Secrets Manager secret"
```

---

## Task 2: Add ECS Cluster, Task Execution Role, and API Task Definition (ARM64)

**Files:**
- Modify: `infra/aws-cdk/src/StoryPlatformCoreStack.cs`
- Test: `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`

**Interfaces:**
- Consumes: `AppSecrets` (Task 1), `EcrRepository` (existing).
- Produces: `public Cluster Cluster { get; }`, `public Role TaskExecutionRole { get; }`, `public LogGroup ApiLogGroup { get; }`, `public FargateTaskDefinition ApiTaskDefinition { get; }`. Task 3 consumes `Cluster` and `ApiTaskDefinition` to build the `FargateService`.

This task adds new resources alongside the still-active App Runner service — nothing is removed yet, so App Runner keeps working in production while this synths and is reviewable independently.

- [ ] **Step 1: Write the failing tests**

Add to `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`:

```csharp
    [Fact]
    public void Stack_CreatesEcsClusterInVpc()
    {
        var template = SynthTemplate();
        template.ResourceCountIs("AWS::ECS::Cluster", 1);
    }

    [Fact]
    public void Stack_CreatesApiTaskDefinitionWithArm64AndCorrectSizing()
    {
        var template = SynthTemplate();
        template.ResourceCountIs("AWS::ECS::TaskDefinition", 1);
        template.HasResourceProperties("AWS::ECS::TaskDefinition", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["Cpu"] = "256",
            ["Memory"] = "1024",
            ["RuntimePlatform"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
            {
                ["CpuArchitecture"] = "ARM64",
                ["OperatingSystemFamily"] = "LINUX"
            })
        }));
    }

    [Fact]
    public void Stack_ApiContainerListensOn8080WithHealthCheck()
    {
        var template = SynthTemplate();
        template.HasResourceProperties("AWS::ECS::TaskDefinition", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["ContainerDefinitions"] = Match.ArrayWith(new object[]
            {
                Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                {
                    ["PortMappings"] = Match.ArrayWith(new object[]
                    {
                        Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["ContainerPort"] = 8080
                        })
                    }),
                    ["HealthCheck"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["Command"] = Match.ArrayWith(new object[] { "CMD-SHELL", "curl -f http://localhost:8080/health || exit 1" })
                    })
                })
            })
        }));
    }

    [Fact]
    public void Stack_ApiContainerIncludesPlainSettingsEnvVars()
    {
        var template = SynthTemplate();
        template.HasResourceProperties("AWS::ECS::TaskDefinition", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["ContainerDefinitions"] = Match.ArrayWith(new object[]
            {
                Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                {
                    ["Environment"] = Match.ArrayWith(new object[]
                    {
                        Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["Name"] = "JwtSettings__Issuer",
                            ["Value"] = "StoryPlatform"
                        }),
                        Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["Name"] = "Logging__LogLevel__Default",
                            ["Value"] = "Warning"
                        })
                    })
                })
            })
        }));
    }

    [Fact]
    public void Stack_ApiContainerReadsAllFiveSecretsFromConsolidatedSecret()
    {
        var template = SynthTemplate();
        template.HasResourceProperties("AWS::ECS::TaskDefinition", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["ContainerDefinitions"] = Match.ArrayWith(new object[]
            {
                Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                {
                    ["Secrets"] = Match.ArrayWith(new object[]
                    {
                        Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["Name"] = "RedisSettings__ConnectionString"
                        })
                    })
                })
            })
        }));
    }

    [Fact]
    public void Stack_GrantsTaskExecutionRoleAssumedByEcsTasksAndSecretsAccess()
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
                            ["Service"] = "ecs-tasks.amazonaws.com"
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
                        ["Action"] = Match.ArrayWith(new object[] { "secretsmanager:GetSecretValue" })
                    })
                })
            })
        }));
    }

    [Fact]
    public void Stack_CreatesApiLogGroupWithOneWeekRetention()
    {
        var template = SynthTemplate();
        template.HasResourceProperties("AWS::Logs::LogGroup", new System.Collections.Generic.Dictionary<string, object>
        {
            ["LogGroupName"] = "/ecs/storyplatform-core-api",
            ["RetentionInDays"] = 7
        });
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj --filter "Stack_CreatesEcsClusterInVpc|Stack_CreatesApiTaskDefinitionWithArm64AndCorrectSizing|Stack_ApiContainerListensOn8080WithHealthCheck|Stack_ApiContainerIncludesPlainSettingsEnvVars|Stack_ApiContainerReadsAllFiveSecretsFromConsolidatedSecret|Stack_GrantsTaskExecutionRoleAssumedByEcsTasksAndSecretsAccess|Stack_CreatesApiLogGroupWithOneWeekRetention"`
Expected: FAIL (none of these resources exist yet)

- [ ] **Step 3: Add the `using` statements**

At the top of `infra/aws-cdk/src/StoryPlatformCoreStack.cs`, add two new usings (keep the existing ones, including `Amazon.CDK.AWS.AppRunner` — still needed until Task 4):

```csharp
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.Logs;
```

- [ ] **Step 4: Add the new properties**

Add alongside the existing property declarations (near `public Repository EcrRepository { get; }`):

```csharp
    public Cluster Cluster { get; }
    public Role TaskExecutionRole { get; }
    public LogGroup ApiLogGroup { get; }
    public FargateTaskDefinition ApiTaskDefinition { get; }
```

- [ ] **Step 5: Construct the Cluster, execution role, log group, and task definition**

Insert this block right after the `AppSecrets` construction from Task 1, and before the `AppRunnerInstanceRole` block:

```csharp
        Cluster = new Cluster(this, "CoreApiCluster", new ClusterProps
        {
            Vpc = Vpc,
            ClusterName = "storyplatform-core-api-cluster"
        });

        TaskExecutionRole = new Role(this, "ApiTaskExecutionRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            ManagedPolicies = new IManagedPolicy[]
            {
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AmazonECSTaskExecutionRolePolicy")
            }
        });
        AppSecrets.GrantRead(TaskExecutionRole);

        ApiLogGroup = new LogGroup(this, "ApiLogGroup", new LogGroupProps
        {
            LogGroupName = "/ecs/storyplatform-core-api",
            Retention = RetentionDays.ONE_WEEK,
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        ApiTaskDefinition = new FargateTaskDefinition(this, "ApiTaskDefinition", new FargateTaskDefinitionProps
        {
            Cpu = 256,
            MemoryLimitMiB = 1024,
            RuntimePlatform = new RuntimePlatform
            {
                CpuArchitecture = CpuArchitecture.ARM64,
                OperatingSystemFamily = OperatingSystemFamily.LINUX
            },
            ExecutionRole = TaskExecutionRole
        });

        var apiContainer = ApiTaskDefinition.AddContainer("ApiContainer", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromEcrRepository(EcrRepository, "latest"),
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps { StreamPrefix = "api", LogGroup = ApiLogGroup }),
            Environment = new System.Collections.Generic.Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["Swagger__Enabled"] = "true",
                ["Logging__LogLevel__Default"] = "Warning",
                ["JwtSettings__Issuer"] = "StoryPlatform",
                ["JwtSettings__Audience"] = "StoryPlatformClient",
                ["JwtSettings__ExpiryMinutes"] = "120",
                ["JwtSettings__RefreshTokenExpiryDays"] = "7",
                ["JwtSettings__ChildTokenExpiryMinutes"] = "240"
            },
            Secrets = new System.Collections.Generic.Dictionary<string, Amazon.CDK.AWS.ECS.Secret>
            {
                ["ConnectionStrings__DefaultConnection"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(AppSecrets, "DbConnectionString"),
                ["JwtSettings__SecretKey"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(AppSecrets, "JwtSecretKey"),
                ["ResendSettings__ApiKey"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(AppSecrets, "ResendApiKey"),
                ["SePaySettings__ApiKey"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(AppSecrets, "SePayApiKey"),
                ["RedisSettings__ConnectionString"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(AppSecrets, "RedisConnectionString")
            },
            // Root Dockerfile's runtime stage already installs curl (confirmed by inspection),
            // so this works without any Dockerfile change.
            HealthCheck = new Amazon.CDK.AWS.ECS.HealthCheck
            {
                Command = new[] { "CMD-SHELL", "curl -f http://localhost:8080/health || exit 1" },
                Interval = Duration.Seconds(30),
                Timeout = Duration.Seconds(5),
                Retries = 3,
                StartPeriod = Duration.Seconds(30)
            }
        });
        apiContainer.AddPortMappings(new PortMapping { ContainerPort = 8080, Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP });
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj --filter "Stack_CreatesEcsClusterInVpc|Stack_CreatesApiTaskDefinitionWithArm64AndCorrectSizing|Stack_ApiContainerListensOn8080WithHealthCheck|Stack_ApiContainerIncludesPlainSettingsEnvVars|Stack_ApiContainerReadsAllFiveSecretsFromConsolidatedSecret|Stack_GrantsTaskExecutionRoleAssumedByEcsTasksAndSecretsAccess|Stack_CreatesApiLogGroupWithOneWeekRetention"`
Expected: PASS

- [ ] **Step 7: Run the full CDK test project to confirm nothing else broke**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS (App Runner tests still pass — App Runner itself is untouched in this task)

- [ ] **Step 8: Commit**

```bash
git add infra/aws-cdk/src/StoryPlatformCoreStack.cs infra/aws-cdk/test/StoryPlatformCoreStackTests.cs
git commit -m "feat(infra): add ECS cluster, task execution role, and ARM64 API task definition"
```

---

## Task 3: Add gated ECS Service, API task security group, and RDS ingress

**Files:**
- Modify: `infra/aws-cdk/src/StoryPlatformCoreStack.cs`
- Test: `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`

**Interfaces:**
- Consumes: `Cluster`, `ApiTaskDefinition` (Task 2), `Database` (existing), `Vpc` (existing).
- Produces: `public SecurityGroup ApiTaskSecurityGroup { get; }`, `public FargateService? ApiService { get; private set; }`. Task 4's CI role permissions and CfnOutputs reference `ApiService`.

The VPC still has its original `PUBLIC` subnet group at this point (Task 4 removes `PRIVATE_WITH_EGRESS` and the NAT Gateway, not `PUBLIC`), so this task can use `SubnetType.PUBLIC` immediately without waiting for Task 4.

- [ ] **Step 1: Write the failing tests**

Add to `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`:

```csharp
    [Fact]
    public void Stack_CreatesApiTaskSecurityGroupAllowingPublicHttpAndRdsAccess()
    {
        var template = SynthTemplate();
        template.HasResourceProperties("AWS::EC2::SecurityGroup", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["SecurityGroupIngress"] = Match.ArrayWith(new object[]
            {
                Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                {
                    ["FromPort"] = 8080,
                    ["ToPort"] = 8080,
                    ["CidrIp"] = "0.0.0.0/0"
                })
            })
        }));
        template.HasResourceProperties("AWS::EC2::SecurityGroupIngress", new System.Collections.Generic.Dictionary<string, object>
        {
            ["FromPort"] = 5432,
            ["ToPort"] = 5432
        });
    }

    [Fact]
    public void Stack_OmitsEcsServiceByDefault()
    {
        var template = SynthTemplate();
        template.ResourceCountIs("AWS::ECS::Service", 0);
    }

    [Fact]
    public void Stack_CreatesEcsServiceWithPublicIp_WhenContextFlagEnabled()
    {
        var template = SynthTemplate(new System.Collections.Generic.Dictionary<string, object>
        {
            ["includeEcsService"] = true
        });
        template.ResourceCountIs("AWS::ECS::Service", 1);
        template.HasResourceProperties("AWS::ECS::Service", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["DesiredCount"] = 1,
            ["NetworkConfiguration"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
            {
                ["AwsvpcConfiguration"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                {
                    ["AssignPublicIp"] = "ENABLED"
                })
            })
        }));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj --filter "Stack_CreatesApiTaskSecurityGroupAllowingPublicHttpAndRdsAccess|Stack_OmitsEcsServiceByDefault|Stack_CreatesEcsServiceWithPublicIp_WhenContextFlagEnabled"`
Expected: FAIL (`Stack_OmitsEcsServiceByDefault` passes vacuously today since no ECS Service exists at all yet — but the other two fail; that's fine, confirm the two that must fail actually fail)

- [ ] **Step 3: Add the new property declarations**

```csharp
    public SecurityGroup ApiTaskSecurityGroup { get; }
    public FargateService? ApiService { get; private set; }
```

- [ ] **Step 4: Add the security group and RDS ingress rule (unconditional)**

Insert right after Task 2's task definition block, before the existing `AppRunnerInstanceRole` block:

```csharp
        ApiTaskSecurityGroup = new SecurityGroup(this, "ApiTaskSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            Description = "Security group for the Core API's ECS Fargate task",
            AllowAllOutbound = true
        });
        ApiTaskSecurityGroup.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(8080), "Public HTTP access to Core API");

        Database.Connections.AllowFrom(ApiTaskSecurityGroup, Port.Tcp(5432), "Allow ECS API task to reach RDS");
```

- [ ] **Step 5: Add the gated ECS Service**

Insert this near the end of the constructor, after the existing (still-present) App Runner gated block:

```csharp
        var includeEcsServiceContext = Node.TryGetContext("includeEcsService");
        var includeEcsService = includeEcsServiceContext switch
        {
            bool b => b,
            string s => bool.Parse(s),
            _ => false
        };

        if (includeEcsService)
        {
            ApiService = new FargateService(this, "ApiService", new FargateServiceProps
            {
                Cluster = Cluster,
                TaskDefinition = ApiTaskDefinition,
                ServiceName = "storyplatform-core-api-svc",
                DesiredCount = 1,
                AssignPublicIp = true,
                VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PUBLIC },
                SecurityGroups = new[] { ApiTaskSecurityGroup }
            });

            new CfnOutput(this, "EcsClusterNameOutput", new CfnOutputProps
            {
                Value = Cluster.ClusterName,
                Description = "Paste this into deploy-core-api.yml's ECS_CLUSTER env var"
            });

            new CfnOutput(this, "EcsServiceNameOutput", new CfnOutputProps
            {
                Value = ApiService.ServiceName,
                Description = "Paste this into deploy-core-api.yml's ECS_SERVICE env var"
            });

            CiRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[] { "ecs:UpdateService", "ecs:DescribeServices" },
                Resources = new[] { ApiService.ServiceArn }
            }));
        }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj --filter "Stack_CreatesApiTaskSecurityGroupAllowingPublicHttpAndRdsAccess|Stack_OmitsEcsServiceByDefault|Stack_CreatesEcsServiceWithPublicIp_WhenContextFlagEnabled"`
Expected: PASS

- [ ] **Step 7: Run the full CDK test project**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS — the stack now synthesizes with **both** App Runner (still gated behind `includeAppRunnerService`) and ECS (gated behind `includeEcsService`) present simultaneously; this is intentional and temporary, resolved in Task 4.

- [ ] **Step 8: Commit**

```bash
git add infra/aws-cdk/src/StoryPlatformCoreStack.cs infra/aws-cdk/test/StoryPlatformCoreStackTests.cs
git commit -m "feat(infra): add gated ECS service, task security group, and RDS ingress rule"
```

---

## Task 4: Cutover — remove App Runner, VPC Connector, and NAT Gateway

**Files:**
- Modify: `infra/aws-cdk/src/StoryPlatformCoreStack.cs`
- Modify: `infra/aws-cdk/cdk.json`
- Test: `infra/aws-cdk/test/StoryPlatformCoreStackTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing new — this task only removes resources and renames the persisted context flag.

This is the cutover: after this task, the stack has exactly one compute path (ECS), matching the spec.

- [ ] **Step 1: Write the failing tests**

Replace `Stack_CreatesVpcWithSingleNatGatewayForEgress` and `Stack_VpcHasPublicPrivateAndIsolatedSubnets` with:

```csharp
    [Fact]
    public void Stack_CreatesVpcWithNoNatGateway()
    {
        var template = SynthTemplate();
        template.ResourceCountIs("AWS::EC2::VPC", 1);
        template.ResourceCountIs("AWS::EC2::NatGateway", 0);
        template.ResourceCountIs("AWS::EC2::InternetGateway", 1);
    }

    [Fact]
    public void Stack_VpcHasOnlyPublicAndIsolatedSubnets()
    {
        var template = SynthTemplate();
        // 2 AZs x 2 subnet groups (Public, Isolated) = 4 subnets — PRIVATE_WITH_EGRESS removed.
        template.ResourceCountIs("AWS::EC2::Subnet", 4);
    }
```

Delete these now-obsolete tests entirely (App Runner and its VPC Connector no longer exist): `Stack_CreatesAppRunnerInstanceRoleScopedToSecrets`, `Stack_CreatesVpcConnectorAllowedIntoRds`, `Stack_OmitsAppRunnerServiceByDefault`, `Stack_CreatesAppRunnerServiceWithAutoDeployDisabled_WhenContextFlagEnabled`, `Stack_AppRunnerServiceIncludesPlainJwtSettingsEnvVars`, `Stack_AppRunnerServiceHasHealthCheckConfiguration`, `Stack_GrantsCiRoleAppRunnerDeployPermissions_WhenContextFlagEnabled`.

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj --filter "Stack_CreatesVpcWithNoNatGateway|Stack_VpcHasOnlyPublicAndIsolatedSubnets"`
Expected: FAIL (VPC still has 1 NAT Gateway and 3 subnet groups)

- [ ] **Step 3: Update the VPC subnet configuration**

Replace:

```csharp
        Vpc = new Vpc(this, "CoreVpc", new VpcProps
        {
            MaxAzs = 2,
            NatGateways = 1,
            SubnetConfiguration = new[]
            {
                new SubnetConfiguration
                {
                    Name = "Isolated",
                    SubnetType = SubnetType.PRIVATE_ISOLATED,
                    CidrMask = 24
                },
                new SubnetConfiguration
                {
                    Name = "Public",
                    SubnetType = SubnetType.PUBLIC,
                    CidrMask = 24
                },
                new SubnetConfiguration
                {
                    Name = "Private",
                    SubnetType = SubnetType.PRIVATE_WITH_EGRESS,
                    CidrMask = 24
                }
            }
        });
```

with:

```csharp
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
                },
                new SubnetConfiguration
                {
                    Name = "Public",
                    SubnetType = SubnetType.PUBLIC,
                    CidrMask = 24
                }
            }
        });
```

- [ ] **Step 4: Remove the App Runner instance role, VPC connector, and ECR access role**

Delete these blocks entirely:

```csharp
        AppRunnerInstanceRole = new Role(this, "AppRunnerInstanceRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("tasks.apprunner.amazonaws.com")
        });

        AppSecrets.GrantRead(AppRunnerInstanceRole);

        var vpcConnectorSecurityGroup = new SecurityGroup(this, "VpcConnectorSecurityGroupV2", new SecurityGroupProps
        {
            Vpc = Vpc,
            Description = "Security group for the App Runner VPC Connector",
            AllowAllOutbound = true
        });

        Database.Connections.AllowFrom(vpcConnectorSecurityGroup, Port.Tcp(5432), "Allow App Runner VPC Connector to reach RDS");

        VpcConnector = new CfnVpcConnector(this, "AppRunnerVpcConnector", new CfnVpcConnectorProps
        {
            Subnets = Vpc.SelectSubnets(new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS }).SubnetIds,
            SecurityGroups = new[] { vpcConnectorSecurityGroup.SecurityGroupId }
        });
```

and remove their property declarations:

```csharp
    public Role AppRunnerInstanceRole { get; }
    public CfnVpcConnector VpcConnector { get; }
```

- [ ] **Step 5: Remove the entire App Runner gated block**

Delete the whole `if (includeAppRunnerService) { ... }` block (from `var includeAppRunnerServiceContext = ...` through its closing brace, including the nested `AppRunnerEcrAccessRole`, `CfnService` construction, `CiRole.AddToPolicy(...apprunner:StartDeployment...)`, and the two `AppRunnerServiceArnOutput`/`AppRunnerServiceUrlOutput` outputs). Also remove its property declaration:

```csharp
    public CfnService? AppRunnerService { get; private set; }
```

- [ ] **Step 6: Remove the now-unneeded `Amazon.CDK.AWS.AppRunner` using**

```csharp
using Amazon.CDK.AWS.AppRunner;
```

- [ ] **Step 7: Update `infra/aws-cdk/cdk.json`**

Replace:

```json
{
  "app": "dotnet run --project src/StoryPlatform.Infra.csproj",
  "context": {
    "includeAppRunnerService": true
  }
}
```

with:

```json
{
  "app": "dotnet run --project src/StoryPlatform.Infra.csproj",
  "context": {
    "includeEcsService": true
  }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj --filter "Stack_CreatesVpcWithNoNatGateway|Stack_VpcHasOnlyPublicAndIsolatedSubnets"`
Expected: PASS

- [ ] **Step 9: Run the full CDK test project**

Run: `dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj`
Expected: PASS — no App Runner tests remain (deleted in Step 1); all ECS tests from Tasks 2-3 pass; `cdk synth` produces a template with no App Runner, VPC Connector, or NAT Gateway resources.

- [ ] **Step 10: `cdk synth` sanity check**

Run: `cd infra/aws-cdk && cdk synth`
Expected: succeeds with no errors, using the persisted `includeEcsService: true` context.

- [ ] **Step 11: Commit**

```bash
git add infra/aws-cdk/src/StoryPlatformCoreStack.cs infra/aws-cdk/test/StoryPlatformCoreStackTests.cs infra/aws-cdk/cdk.json
git commit -m "refactor(infra): remove App Runner, VPC Connector, and NAT Gateway — ECS is now the only compute path"
```

---

## Task 5: Update CI/CD workflow for ECS deploy and ARM64 build

**Files:**
- Modify: `.github/workflows/deploy-core-api.yml`

**Interfaces:**
- Consumes: `ECS_CLUSTER`/`ECS_SERVICE` names (`storyplatform-core-api-cluster` / `storyplatform-core-api-svc`, fixed by Task 2/3's CDK code — also emitted as CfnOutputs for operator reference).

This is a YAML-only change; there is no local unit test for GitHub Actions workflows, so validation happens via `actionlint`/YAML syntax check and the "test" job already in the workflow (unchanged, still runs `dotnet test` against both the solution and the CDK test project).

- [ ] **Step 1: Replace the workflow file**

Replace the full contents of `.github/workflows/deploy-core-api.yml` with:

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
      - 'infra/aws-cdk/**'
      - '.github/workflows/deploy-core-api.yml'

permissions:
  id-token: write
  contents: read

env:
  AWS_REGION: ap-southeast-1
  ECR_REPOSITORY: storyplatform-core-api
  ECS_CLUSTER: storyplatform-core-api-cluster
  ECS_SERVICE: storyplatform-core-api-svc

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

      - name: Test CDK stack
        run: dotnet test infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj --configuration Release

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

      - name: Set up QEMU (for ARM64 cross-build)
        uses: docker/setup-qemu-action@v3

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Build and push image (ARM64)
        env:
          ECR_REGISTRY: ${{ steps.ecr-login.outputs.registry }}
          IMAGE_TAG: ${{ github.sha }}
        run: |
          docker buildx build \
            --platform linux/arm64 \
            --file Dockerfile \
            --target core-api \
            --tag "$ECR_REGISTRY/$ECR_REPOSITORY:$IMAGE_TAG" \
            --tag "$ECR_REGISTRY/$ECR_REPOSITORY:latest" \
            --push \
            .

      - name: Force new ECS deployment
        run: |
          aws ecs update-service \
            --cluster "$ECS_CLUSTER" \
            --service "$ECS_SERVICE" \
            --force-new-deployment \
            --region "$AWS_REGION" > /dev/null

      - name: Wait for ECS service to stabilize
        run: |
          aws ecs wait services-stable \
            --cluster "$ECS_CLUSTER" \
            --services "$ECS_SERVICE" \
            --region "$AWS_REGION"
```

- [ ] **Step 2: Validate YAML syntax locally**

Run: `python -c "import yaml, sys; yaml.safe_load(open('.github/workflows/deploy-core-api.yml'))" && echo "valid YAML"`
Expected: prints `valid YAML` with no exception

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/deploy-core-api.yml
git commit -m "ci(deploy): switch Core API deploy from App Runner to ECS, build for ARM64"
```

---

## Task 6: Wire an external Redis distributed cache (fail-fast configuration)

**Files:**
- Modify: `src/Core/StoryPlatform.Infrastructure/StoryPlatform.Infrastructure.csproj`
- Create: `src/Core/StoryPlatform.Infrastructure/Caching/RedisOptions.cs`
- Create: `src/Core/StoryPlatform.Infrastructure/Caching/RedisCacheExtensions.cs`
- Modify: `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs`
- Test: `tests/StoryPlatform.UnitTests/Infrastructure/Caching/RedisCacheExtensionsTests.cs` (new)

**Interfaces:**
- Produces: `RedisCacheExtensions.AddRedisCache(this IServiceCollection, IConfiguration)` — registers `IDistributedCache` (via `AddStackExchangeRedisCache`) configured to read `RedisSettings:ConnectionString` and fail fast (`AbortOnConnectFail = false`, short timeouts) rather than block or crash the app when the external Redis provider is unreachable. Also produces the internal `RedisCacheExtensions.BuildRedisConfiguration(string)` helper, unit-tested directly.
- Deciding what the app actually caches (which endpoints call `IDistributedCache`) is out of scope for this task (spec §2 non-goal) — this task only makes `IDistributedCache` available and safe to call, with no consumer yet.

- [ ] **Step 1: Add the NuGet package**

In `src/Core/StoryPlatform.Infrastructure/StoryPlatform.Infrastructure.csproj`, add to the existing `<ItemGroup>` with the other `Microsoft.Extensions.*` packages:

```xml
    <PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="8.0.11" />
```

- [ ] **Step 2: Write the failing tests**

Create `tests/StoryPlatform.UnitTests/Infrastructure/Caching/RedisCacheExtensionsTests.cs`:

```csharp
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Infrastructure.Caching;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.Caching;

public class RedisCacheExtensionsTests
{
    private static IConfiguration BuildConfiguration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["RedisSettings:ConnectionString"] = connectionString
            })
            .Build();

    [Fact]
    public void AddRedisCache_RegistersIDistributedCache()
    {
        var services = new ServiceCollection();

        services.AddRedisCache(BuildConfiguration("localhost:6379"));
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IDistributedCache>());
    }

    [Fact]
    public void BuildRedisConfiguration_DisablesAbortOnConnectFail()
    {
        var options = RedisCacheExtensions.BuildRedisConfiguration("localhost:6379");

        Assert.False(options.AbortOnConnectFail);
    }

    [Fact]
    public void BuildRedisConfiguration_UsesShortConnectTimeout()
    {
        var options = RedisCacheExtensions.BuildRedisConfiguration("localhost:6379");

        Assert.True(options.ConnectTimeout <= 2000);
    }

    [Fact]
    public void BuildRedisConfiguration_RetriesConnectionAtMostOnce()
    {
        var options = RedisCacheExtensions.BuildRedisConfiguration("localhost:6379");

        Assert.Equal(1, options.ConnectRetry);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~RedisCacheExtensionsTests"`
Expected: FAIL with "the type or namespace name 'Caching' does not exist" (nothing implemented yet)

- [ ] **Step 4: Create `RedisOptions`**

Create `src/Core/StoryPlatform.Infrastructure/Caching/RedisOptions.cs`:

```csharp
namespace StoryPlatform.Infrastructure.Caching;

/// <summary>
/// Cấu hình cho Redis cache bên ngoài AWS (Upstash/Redis Cloud, ...) — đọc từ configuration
/// section "RedisSettings". ConnectionString là biến môi trường RedisSettings__ConnectionString
/// (production, tiêm qua ECS task secret) hoặc appsettings.Development.json (dev, đã bị
/// .gitignore chặn).
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "RedisSettings";
    public string ConnectionString { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Create `RedisCacheExtensions`**

Create `src/Core/StoryPlatform.Infrastructure/Caching/RedisCacheExtensions.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace StoryPlatform.Infrastructure.Caching;

public static class RedisCacheExtensions
{
    public static IServiceCollection AddRedisCache(this IServiceCollection services, IConfiguration configuration)
    {
        var redisOptions = new RedisOptions();
        configuration.GetSection(RedisOptions.SectionName).Bind(redisOptions);
        services.AddSingleton(redisOptions);

        services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = BuildRedisConfiguration(redisOptions.ConnectionString);
        });

        return services;
    }

    /// <summary>
    /// Redis is an external, best-effort cache provider (spec §5.8) — never a hard dependency
    /// for correctness. These settings make any future IDistributedCache call fail fast instead
    /// of hanging or crashing the app when the provider is briefly unreachable (network blip,
    /// provider-side maintenance), so callers can safely treat a failure as a cache miss.
    /// </summary>
    internal static ConfigurationOptions BuildRedisConfiguration(string connectionString)
    {
        var configurationOptions = string.IsNullOrWhiteSpace(connectionString)
            ? new ConfigurationOptions()
            : ConfigurationOptions.Parse(connectionString);

        configurationOptions.AbortOnConnectFail = false;
        configurationOptions.ConnectTimeout = 2000;
        configurationOptions.SyncTimeout = 1000;
        configurationOptions.ConnectRetry = 1;

        return configurationOptions;
    }
}
```

- [ ] **Step 6: Wire it into `AddInfrastructure`**

In `src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs`, add the using:

```csharp
using StoryPlatform.Infrastructure.Caching;
```

and add this line right after `var connectionString = ConnectionStringHelper.GetConnectionString(configuration);`:

```csharp
        services.AddRedisCache(configuration);
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/StoryPlatform.UnitTests --filter "FullyQualifiedName~RedisCacheExtensionsTests"`
Expected: PASS

- [ ] **Step 8: Run the full unit test project to confirm nothing else broke**

Run: `dotnet test tests/StoryPlatform.UnitTests`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add src/Core/StoryPlatform.Infrastructure/StoryPlatform.Infrastructure.csproj src/Core/StoryPlatform.Infrastructure/Caching/ src/Core/StoryPlatform.Infrastructure/DependencyInjection.cs tests/StoryPlatform.UnitTests/Infrastructure/Caching/
git commit -m "feat(infrastructure): wire external Redis distributed cache with fail-fast configuration"
```

---

## Task 7: Update the operational RUNBOOK

**Files:**
- Modify: `infra/aws-cdk/RUNBOOK.md`

This task is documentation-only — there is no test to run, but every command given must be copy-paste-correct against the resources this plan actually creates.

- [ ] **Step 1: Replace the "Two-phase deploy" section**

Replace:

```markdown
## Two-phase deploy (why it exists)

App Runner's `CfnService` references an image tag (`storyplatform-core-api:latest`) in
the stack's own ECR repository. On a **brand-new** stack that repo is empty — no image
has ever been pushed — so creating the App Runner service in the same deploy that
creates the ECR repo would fail to find the image and roll back the *entire* stack
(VPC, RDS, ECR, Secrets included). To avoid this, App Runner Service creation is
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
```

with:

```markdown
## Two-phase deploy (why it exists)

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
```

- [ ] **Step 2: Replace the "CI/CD deploy flow" section**

Replace:

```markdown
## CI/CD deploy flow (as of 2026-09-21)

Every push to `dev` or `main` touching `src/Core/**`, `src/Shared/**`, `Dockerfile`, or the workflow
file itself runs `.github/workflows/deploy-core-api.yml`:

1. **`test` job** — `dotnet build`/`dotnet test` on `StoryPlatform.sln`, plus a separate
   `dotnet test` run against `infra/aws-cdk/test/StoryPlatform.Infra.Tests.csproj` (the CDK stack
   is not part of the main solution, so it needs its own step). A failing test stops the pipeline
   here; nothing is built or deployed.
2. **`build-and-push` job** (`needs: test`) — builds the image from the root `Dockerfile`
   (`--target core-api`, the same target `docker compose` uses locally), pushes `:sha` and `:latest`
   to ECR, then explicitly calls `aws apprunner start-deployment`, captures its `OperationId`, and
   polls `aws apprunner list-operations` by that ID until it reports `SUCCEEDED` (success) or a
   `FAILED`/`ROLLBACK_*` status (failure — the GitHub Actions job fails too, so a bad deploy is
   never silent). Polling by operation ID (not just the service's overall status) avoids a race
   where the very first poll could read the *previous* deployment's already-`RUNNING` status before
   App Runner has even started processing the new one.

App Runner's `AutoDeploymentsEnabled` is **disabled** — pushing a new `:latest` tag to ECR no longer
triggers anything by itself. The `start-deployment` call above is the only way a deploy happens.
```

with:

```markdown
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
```

- [ ] **Step 3: Replace the "Setting real Resend / SePay secret values" section**

Replace:

```markdown
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
```

with:

```markdown
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

`RedisConnectionString` is the `rediss://` URL (including its auth token/password) issued by the
external provider (Upstash or Redis Cloud, spec §5.8) — created outside AWS, the same way the
Resend/SePay keys already are.

Then force a new deployment so the running task picks up the new values (ECS injects secrets at
task start, not live):

```bash
aws ecs update-service --cluster storyplatform-core-api-cluster --service storyplatform-core-api-svc \
  --force-new-deployment --region ap-southeast-1 --profile storyplatform
```
```

- [ ] **Step 4: Commit**

```bash
git add infra/aws-cdk/RUNBOOK.md
git commit -m "docs(infra): update RUNBOOK for ECS two-phase deploy, ARM64 build, and consolidated secret"
```

---

## Task 8: Decommission the old App Runner resources (manual, post-verification)

**This task is run by a human, not automated, and only after Tasks 1-7 are deployed and the new ECS service is confirmed healthy in production.** Deleting a running service is irreversible — per this project's file-change and destructive-action policy, do not run these commands until the human operator has verified the ECS deployment works and explicitly says to proceed.

**Files:** none (AWS CLI operations only)

- [ ] **Step 1: Verify the new ECS service is healthy before touching App Runner**

```bash
aws ecs describe-services --cluster storyplatform-core-api-cluster --services storyplatform-core-api-svc \
  --region ap-southeast-1 --profile storyplatform --query 'services[0].{status:status,running:runningCount,desired:desiredCount}'
```

Expected: `"status": "ACTIVE"`, `"running": 1`, `"desired": 1`. Also hit the task's public IP (RUNBOOK's new "Finding the running task's public IP" section) on port 8080's `/health` and `/swagger` (if `Swagger__Enabled=true`) to confirm the app itself responds, not just the ECS control plane.

- [ ] **Step 2: List the old App Runner service's ARN**

```bash
aws apprunner list-services --region ap-southeast-1 --profile storyplatform \
  --query "ServiceSummaryList[?ServiceName=='storyplatform-core-api-v2'].ServiceArn" --output text
```

- [ ] **Step 3: Delete the old App Runner service**

```bash
aws apprunner delete-service --service-arn <arn-from-step-2> --region ap-southeast-1 --profile storyplatform
```

- [ ] **Step 4: Confirm the underlying stack no longer references any App Runner resources**

Since Task 4 already removed `AppRunnerInstanceRole`, `CfnVpcConnector`, and the `CfnService` construct from the CDK code, `cdk deploy` from here on will not attempt to recreate them. No further stack action is needed — this step is just a sanity check that `cdk diff` shows no pending App Runner-related changes:

```bash
cd infra/aws-cdk && cdk diff --profile storyplatform
```

Expected: no `AWS::AppRunner::*` or `AWS::AppRunner::VpcConnector` resources appear in the diff (they were already removed from the deployed stack by Task 4's `cdk deploy`).

---

## Self-Review Notes

**Spec coverage:** §5.1 (VPC/NAT) → Task 4. §5.2 (RDS) → unchanged, ingress updated in Task 3/4. §5.3 (ECR) → unchanged. §5.4 (Secrets) → Task 1. §5.5-5.7 (Cluster/TaskDef/Service) → Tasks 2-3. §5.8 (External Redis) → Task 6. §5.10 (CI role) → Tasks 3-4. §6 (Dockerfile) → confirmed no change needed (Task 2 note). §7 (CI/CD) → Task 5. §8 (fallback behavior requirement) → Task 6's `BuildRedisConfiguration`. §12 (testing plan) → covered by each task's own test steps plus Task 8's manual verification. §13 decision log items (ARM64, health check, Public IPv4 charge awareness) → Task 2 (ARM64 + health check); the Public IPv4 charge itself is a billing fact, not a resource to create — nothing to implement, already reflected in the spec's cost table.

**Placeholder scan:** no TBD/TODO; every code block is complete and copy-pasteable; every test has real assertions.

**Type consistency:** `AppSecrets` (Task 1) is referenced identically in Tasks 2-4; `ApiTaskDefinition`/`Cluster` (Task 2) match the types Task 3's `FargateService` consumes; `RedisCacheExtensions.AddRedisCache`/`BuildRedisConfiguration` names match between their definition (Task 6, Step 5) and their test references (Task 6, Step 2).
