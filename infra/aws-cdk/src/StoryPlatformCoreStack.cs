using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
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
    public SecurityGroup ApiTaskSecurityGroup { get; }
    public FargateService? ApiService { get; private set; }
    public Role CiRole { get; }

    public StoryPlatformCoreStack(Construct scope, string id, IStackProps? props = null)
        : base(scope, id, props)
    {
        // One-time bootstrap flag: when true, the container runs Database/Seed/*.sql once at
        // startup (see DatabaseSeedExtensions in StoryPlatform.Api). Only pass
        // --context seedDatabaseOnStart=true for the single deploy that should seed; unlike
        // includeEcsService this isn't meant to be persisted in cdk.json, since leaving it true
        // would re-seed on every deploy.
        var seedDatabaseOnStartContext = Node.TryGetContext("seedDatabaseOnStart");
        var seedDatabaseOnStart = seedDatabaseOnStartContext switch
        {
            bool b => b,
            string s => bool.Parse(s),
            _ => false
        };

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
                ["Logging__LogLevel__Default"] = "Warning",
                ["SeedData__RunOnStartup"] = seedDatabaseOnStart ? "true" : "false"
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

        ApiTaskSecurityGroup = new SecurityGroup(this, "ApiTaskSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            Description = "Security group for the Core API ECS Fargate task",
            AllowAllOutbound = true
        });
        // No direct-from-internet ingress rule here: once includeEcsService is on, only the ALB
        // (below) may reach the task on 8080 - tightened from the previous "Peer.AnyIpv4()" rule
        // now that the ALB, not the task's own ephemeral public IP, is the public entry point.

        Database.Connections.AllowFrom(ApiTaskSecurityGroup, Port.Tcp(5432), "Allow ECS API task to reach RDS");

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
                SecurityGroups = new[] { ApiTaskSecurityGroup },
                // Pin explicitly rather than rely on aws-cdk-lib's own default (documented as 50%
                // for a non-daemon service, which varies by CDK version). At DesiredCount = 1, a
                // 50% floor rounds down to 0 healthy tasks required, which would let ECS stop the
                // old (healthy) task before the new one passes its health check. 100/200 keeps the
                // previous task running until the new one is healthy (see design spec §9).
                MinHealthyPercent = 100,
                MaxHealthyPercent = 200
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

            // Stable public entry point for a custom domain. A Fargate awsvpc task gets a brand
            // new ENI (and public IP) on every deploy/restart, so a domain can't point at the
            // task directly; the ALB's DNS name never changes. (An EIP re-associated onto the
            // task's ENI by a Lambda was tried first and would have been cheaper, but this
            // account's org-level SCP blocks ec2:AssociateAddress outright - confirmed via
            // AuthFailure on that call even with an admin-privileged IAM principal - so the ALB
            // is the only viable option here despite its ~$16-20/month fixed cost.)
            var apiAlb = new ApplicationLoadBalancer(this, "ApiAlb", new ApplicationLoadBalancerProps
            {
                Vpc = Vpc,
                InternetFacing = true,
                VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PUBLIC }
            });

            ApiTaskSecurityGroup.Connections.AllowFrom(apiAlb, Port.Tcp(8080), "Allow ALB to reach Core API");

            var apiListener = apiAlb.AddListener("HttpListener", new BaseApplicationListenerProps
            {
                Port = 80,
                Open = true
            });

            apiListener.AddTargets("ApiTargets", new AddApplicationTargetsProps
            {
                Port = 8080,
                Protocol = ApplicationProtocol.HTTP,
                Targets = new IApplicationLoadBalancerTarget[]
                {
                    ApiService.LoadBalancerTarget(new LoadBalancerTargetOptions
                    {
                        ContainerName = "ApiContainer",
                        ContainerPort = 8080
                    })
                },
                HealthCheck = new Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck
                {
                    Path = "/health",
                    Interval = Duration.Seconds(30),
                    Timeout = Duration.Seconds(5),
                    HealthyThresholdCount = 2,
                    UnhealthyThresholdCount = 5
                }
            });

            new CfnOutput(this, "ApiAlbDnsNameOutput", new CfnOutputProps
            {
                Value = apiAlb.LoadBalancerDnsName,
                Description = "Point your domain's DNS record (CNAME/ALIAS) here"
            });
        }
    }
}
