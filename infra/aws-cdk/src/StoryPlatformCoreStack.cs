using System;
using System.Security.Cryptography;
using Amazon.CDK;
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

        AppRunnerInstanceRole = new Role(this, "AppRunnerInstanceRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("tasks.apprunner.amazonaws.com")
        });

        DbConnectionSecret.GrantRead(AppRunnerInstanceRole);
        JwtSecret.GrantRead(AppRunnerInstanceRole);
        ResendApiKeySecret.GrantRead(AppRunnerInstanceRole);
        SePayApiKeySecret.GrantRead(AppRunnerInstanceRole);
    }

    private static string GenerateRandomSecret(int byteLength = 48)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes);
    }
}
