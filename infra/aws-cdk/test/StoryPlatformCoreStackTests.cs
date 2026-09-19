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
                        ["Action"] = Match.ArrayWith(new object[] { "secretsmanager:GetSecretValue" })
                    })
                })
            })
        }));
    }

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

    [Fact]
    public void Stack_AppRunnerServiceIncludesPlainJwtSettingsEnvVars()
    {
        // appsettings.json is gitignored and not part of the image built from a clean checkout,
        // so JwtSettings (required by ServiceExtensions.AddJwtAuthentication at startup, or the
        // app throws InvalidOperationException and crash-loops) must come entirely from App
        // Runner's plain runtime env vars. These are non-secret config (issuer/audience/expiry
        // durations), not credentials, so they belong in RuntimeEnvironmentVariables rather than
        // RuntimeEnvironmentSecrets.
        var template = SynthTemplate(new System.Collections.Generic.Dictionary<string, object>
        {
            ["includeAppRunnerService"] = true
        });
        template.HasResourceProperties("AWS::AppRunner::Service", new System.Collections.Generic.Dictionary<string, object>
        {
            ["SourceConfiguration"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
            {
                ["ImageRepository"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                {
                    ["ImageConfiguration"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["RuntimeEnvironmentVariables"] = Match.ArrayWith(new object[]
                        {
                            Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                            {
                                ["Name"] = "JwtSettings__Issuer",
                                ["Value"] = "StoryPlatform"
                            }),
                            Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                            {
                                ["Name"] = "JwtSettings__Audience",
                                ["Value"] = "StoryPlatformClient"
                            }),
                            Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                            {
                                ["Name"] = "JwtSettings__ExpiryMinutes",
                                ["Value"] = "120"
                            }),
                            Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                            {
                                ["Name"] = "JwtSettings__RefreshTokenExpiryDays",
                                ["Value"] = "7"
                            }),
                            Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                            {
                                ["Name"] = "JwtSettings__ChildTokenExpiryMinutes",
                                ["Value"] = "240"
                            })
                        })
                    })
                })
            })
        });
    }

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
}
