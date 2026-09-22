using Amazon.CDK;
using Amazon.CDK.AWS.AppRunner;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.SecretsManager;
using Constructs;

namespace StoryPlatform.Infra;

public sealed class StoryPlatformCoreStack : Stack
{
    public IVpc Vpc { get; }
    public DatabaseInstance Database { get; }
    public Repository EcrRepository { get; }
    public Amazon.CDK.AWS.SecretsManager.Secret AppSecrets { get; }
    public Cluster Cluster { get; }
    public Role TaskExecutionRole { get; }
    public LogGroup ApiLogGroup { get; }
    public FargateTaskDefinition ApiTaskDefinition { get; }
    public Role AppRunnerInstanceRole { get; }
    public CfnVpcConnector VpcConnector { get; }
    public Amazon.CDK.AWS.AppRunner.CfnService? AppRunnerService { get; private set; }
    public Role CiRole { get; }

    public StoryPlatformCoreStack(Construct scope, string id, IStackProps? props = null)
        : base(scope, id, props)
    {
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

        Database = new DatabaseInstance(this, "CoreDatabase", new DatabaseInstanceProps
        {
            Engine = DatabaseInstanceEngine.Postgres(new PostgresInstanceEngineProps
            {
                Version = PostgresEngineVersion.Of("16.9", "16")
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

        AppSecrets = new Amazon.CDK.AWS.SecretsManager.Secret(this, "AppSecrets", new SecretProps
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
                ["JwtSettings__Issuer"] = "StoryPlatform",
                ["JwtSettings__Audience"] = "StoryPlatformClient",
                ["JwtSettings__ExpiryMinutes"] = "120",
                ["JwtSettings__RefreshTokenExpiryDays"] = "7",
                ["JwtSettings__ChildTokenExpiryMinutes"] = "240",
                ["Logging__LogLevel__Default"] = "Warning"
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

        // Note: deliberately using the L1 CfnOIDCProvider (not the L2 OpenIdConnectProvider) so the
        // synthesized template contains a native AWS::IAM::OIDCProvider resource. In aws-cdk-lib 2.170.0
        // (the version pinned by this project), the L2 construct provisions the provider via a
        // Lambda-backed custom resource (Custom::AWSCDKOpenIdConnectProvider) instead of the native
        // CloudFormation resource type, which the test for this task asserts on directly.
        // ThumbprintList is intentionally omitted: it is optional on AWS::IAM::OIDCProvider, and when
        // omitted, IAM automatically retrieves and uses the correct thumbprint from the provider's TLS
        // certificate chain — avoiding a hardcoded value that could be malformed or go stale.
        var githubOidcProvider = new CfnOIDCProvider(this, "GitHubOidcProvider", new CfnOIDCProviderProps
        {
            Url = "https://token.actions.githubusercontent.com",
            ClientIdList = new[] { "sts.amazonaws.com" }
        });

        const string githubRepoOwner = "FA26SE142-GFA26SE04-AI-Storytelling"; // confirmed via `git remote -v` (origin)
        const string githubRepoName = "AI_Storytelling_Backend";
        const string githubRepo = $"{githubRepoOwner}/{githubRepoName}";

        CiRole = new Role(this, "GitHubActionsCiRole", new RoleProps
        {
            RoleName = "storyplatform-core-api-ci",
            AssumedBy = new WebIdentityPrincipal(githubOidcProvider.Ref, new System.Collections.Generic.Dictionary<string, object>
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
                        $"repo:{githubRepo}:ref:refs/heads/main",
                        // GitHub emits an immutable-ID-qualified subject (owner@ownerId/repo@repoId)
                        // instead of the plain owner/repo form once an org or repo has been renamed;
                        // match both so CI keeps working (confirmed via CloudTrail on the live AccessDenied).
                        $"repo:{githubRepoOwner}@*/{githubRepoName}@*:ref:refs/heads/dev",
                        $"repo:{githubRepoOwner}@*/{githubRepoName}@*:ref:refs/heads/main"
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

        // Bootstrap gating: on the very first `cdk deploy`, the ECR repo above is empty
        // (no image has been pushed yet, since you can't push before the repo exists).
        // Creating the App Runner Service unconditionally in that same deploy would make
        // CloudFormation fail to find the image and roll back the ENTIRE stack, deleting
        // the VPC/RDS/ECR/Secrets that succeeded too. So App Runner Service creation is
        // gated behind this context flag (default false); Task 13 deploys twice: once
        // with the flag off, pushes the first image, then deploys again with it on.
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

            // Construct ID bumped to V2 on 2026-09-21: every deploy of the app-with-auto-migration
            // code deterministically failed its App Runner health check (HTTP and TCP both tried;
            // VPC Connector on and off both tried) despite the exact image proving 100% healthy when
            // run identically outside App Runner — pointing at stuck internal state on the original
            // service rather than anything fixable via configuration. Changing the logical ID forces
            // CloudFormation to create a brand-new service (and delete the old one) instead of
            // updating in place, to rule out — or clear — that stuck state. This changes the
            // service's ARN and public URL; both need updating wherever they're hardcoded
            // (deploy-core-api.yml, RUNBOOK.md).
            AppRunnerService = new Amazon.CDK.AWS.AppRunner.CfnService(this, "CoreApiServiceV2", new Amazon.CDK.AWS.AppRunner.CfnServiceProps
            {
                // Renamed from "storyplatform-core-api": App Runner service names must be unique
                // per account/region, and CloudFormation creates the new resource before deleting
                // the old one (safe-by-default ordering), so keeping the old literal name here
                // collided with the still-existing old service and failed with "Service with the
                // provided name already exists" (confirmed 2026-09-21, stack rolled back cleanly,
                // old service untouched).
                ServiceName = "storyplatform-core-api-v2",
                SourceConfiguration = new Amazon.CDK.AWS.AppRunner.CfnService.SourceConfigurationProperty
                {
                    AutoDeploymentsEnabled = false,
                    AuthenticationConfiguration = new Amazon.CDK.AWS.AppRunner.CfnService.AuthenticationConfigurationProperty
                    {
                        AccessRoleArn = appRunnerEcrAccessRole.RoleArn
                    },
                    ImageRepository = new Amazon.CDK.AWS.AppRunner.CfnService.ImageRepositoryProperty
                    {
                        ImageIdentifier = $"{EcrRepository.RepositoryUri}:latest",
                        ImageRepositoryType = "ECR",
                        ImageConfiguration = new Amazon.CDK.AWS.AppRunner.CfnService.ImageConfigurationProperty
                        {
                            Port = "8080",
                            RuntimeEnvironmentVariables = new[]
                            {
                                new Amazon.CDK.AWS.AppRunner.CfnService.KeyValuePairProperty { Name = "ASPNETCORE_ENVIRONMENT", Value = "Production" },
                                new Amazon.CDK.AWS.AppRunner.CfnService.KeyValuePairProperty { Name = "Swagger__Enabled", Value = "true" },
                                new Amazon.CDK.AWS.AppRunner.CfnService.KeyValuePairProperty { Name = "JwtSettings__Issuer", Value = "StoryPlatform" },
                                new Amazon.CDK.AWS.AppRunner.CfnService.KeyValuePairProperty { Name = "JwtSettings__Audience", Value = "StoryPlatformClient" },
                                new Amazon.CDK.AWS.AppRunner.CfnService.KeyValuePairProperty { Name = "JwtSettings__ExpiryMinutes", Value = "120" },
                                new Amazon.CDK.AWS.AppRunner.CfnService.KeyValuePairProperty { Name = "JwtSettings__RefreshTokenExpiryDays", Value = "7" },
                                new Amazon.CDK.AWS.AppRunner.CfnService.KeyValuePairProperty { Name = "JwtSettings__ChildTokenExpiryMinutes", Value = "240" }
                            },
                            RuntimeEnvironmentSecrets = new[]
                            {
                                new Amazon.CDK.AWS.AppRunner.CfnService.KeyValuePairProperty { Name = "ConnectionStrings__DefaultConnection", Value = $"{AppSecrets.SecretArn}:DbConnectionString::" },
                                new Amazon.CDK.AWS.AppRunner.CfnService.KeyValuePairProperty { Name = "JwtSettings__SecretKey", Value = $"{AppSecrets.SecretArn}:JwtSecretKey::" },
                                new Amazon.CDK.AWS.AppRunner.CfnService.KeyValuePairProperty { Name = "ResendSettings__ApiKey", Value = $"{AppSecrets.SecretArn}:ResendApiKey::" },
                                new Amazon.CDK.AWS.AppRunner.CfnService.KeyValuePairProperty { Name = "SePaySettings__ApiKey", Value = $"{AppSecrets.SecretArn}:SePayApiKey::" }
                            }
                        }
                    }
                },
                InstanceConfiguration = new Amazon.CDK.AWS.AppRunner.CfnService.InstanceConfigurationProperty
                {
                    Cpu = "1024",
                    Memory = "2048",
                    InstanceRoleArn = AppRunnerInstanceRole.RoleArn
                },
                // Protocol=TCP (not HTTP): confirmed on 2026-09-21 that the exact deployed image,
                // pulled straight from this service's own ECR repo and run locally with the same
                // env vars App Runner uses, returns 200 "Healthy" on GET /health instantly and
                // consistently — proving the code and image are correct. Every live App Runner
                // deploy nonetheless got a deterministic health-check failure (HTTP: 404 on /health;
                // TCP: port check itself failing) regardless of VPC Connector being attached or not
                // (both tried and ruled out) — see the CoreApiServiceV2 construct-id-bump note above
                // for the resulting decision to recreate the service. TCP kept as the simplest,
                // lowest-risk check going forward; Path is not applicable to TCP and is omitted.
                HealthCheckConfiguration = new Amazon.CDK.AWS.AppRunner.CfnService.HealthCheckConfigurationProperty
                {
                    Protocol = "TCP",
                    Interval = 10,
                    Timeout = 5,
                    HealthyThreshold = 1,
                    UnhealthyThreshold = 15
                },
                NetworkConfiguration = new Amazon.CDK.AWS.AppRunner.CfnService.NetworkConfigurationProperty
                {
                    EgressConfiguration = new Amazon.CDK.AWS.AppRunner.CfnService.EgressConfigurationProperty
                    {
                        EgressType = "VPC",
                        VpcConnectorArn = VpcConnector.AttrVpcConnectorArn
                    }
                }
            });

            CiRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[] { "apprunner:StartDeployment", "apprunner:DescribeService", "apprunner:ListOperations" },
                Resources = new[] { AppRunnerService.AttrServiceArn }
            }));

            new CfnOutput(this, "AppRunnerServiceArnOutput", new CfnOutputProps
            {
                Value = AppRunnerService.AttrServiceArn,
                Description = "Paste this into deploy-core-api.yml's APP_RUNNER_SERVICE_ARN env var"
            });

            new CfnOutput(this, "AppRunnerServiceUrlOutput", new CfnOutputProps
            {
                Value = AppRunnerService.AttrServiceUrl,
                Description = "Public HTTPS URL of the App Runner service"
            });
        }
    }
}
