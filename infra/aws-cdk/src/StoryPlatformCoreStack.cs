using Amazon.CDK;
using Amazon.CDK.AWS.AppRunner;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.SecretsManager;
using Constructs;

namespace StoryPlatform.Infra;

public sealed class StoryPlatformCoreStack : Stack
{
    public IVpc Vpc { get; }
    public DatabaseInstance Database { get; }
    public Repository EcrRepository { get; }
    public Secret DbConnectionSecret { get; }
    public Secret JwtSecret { get; }
    public Secret ResendApiKeySecret { get; }
    public Secret SePayApiKeySecret { get; }
    public Role AppRunnerInstanceRole { get; }
    public CfnVpcConnector VpcConnector { get; }
    public CfnService? AppRunnerService { get; private set; }
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

        AppRunnerInstanceRole = new Role(this, "AppRunnerInstanceRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("tasks.apprunner.amazonaws.com")
        });

        DbConnectionSecret.GrantRead(AppRunnerInstanceRole);
        JwtSecret.GrantRead(AppRunnerInstanceRole);
        ResendApiKeySecret.GrantRead(AppRunnerInstanceRole);
        SePayApiKeySecret.GrantRead(AppRunnerInstanceRole);

        var vpcConnectorSecurityGroup = new SecurityGroup(this, "VpcConnectorSecurityGroup", new SecurityGroupProps
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

        const string githubRepo = "FA26SE142-GFA26SE04-AI-Storytelling/AI_Storytelling_Backend"; // confirmed via `git remote -v` (origin)

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
                                new CfnService.KeyValuePairProperty { Name = "ASPNETCORE_ENVIRONMENT", Value = "Production" },
                                new CfnService.KeyValuePairProperty { Name = "JwtSettings__Issuer", Value = "StoryPlatform" },
                                new CfnService.KeyValuePairProperty { Name = "JwtSettings__Audience", Value = "StoryPlatformClient" },
                                new CfnService.KeyValuePairProperty { Name = "JwtSettings__ExpiryMinutes", Value = "120" },
                                new CfnService.KeyValuePairProperty { Name = "JwtSettings__RefreshTokenExpiryDays", Value = "7" },
                                new CfnService.KeyValuePairProperty { Name = "JwtSettings__ChildTokenExpiryMinutes", Value = "240" }
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
                HealthCheckConfiguration = new CfnService.HealthCheckConfigurationProperty
                {
                    Protocol = "HTTP",
                    Path = "/index.html",
                    Interval = 10,
                    Timeout = 5,
                    HealthyThreshold = 1,
                    UnhealthyThreshold = 5
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
    }
}
