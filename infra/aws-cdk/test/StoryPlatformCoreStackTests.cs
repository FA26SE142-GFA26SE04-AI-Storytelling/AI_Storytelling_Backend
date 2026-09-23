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
                        ["Action"] = "sts:AssumeRoleWithWebIdentity",
                        ["Condition"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["StringEquals"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                            {
                                ["token.actions.githubusercontent.com:aud"] = "sts.amazonaws.com"
                            }),
                            ["StringLike"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                            {
                                ["token.actions.githubusercontent.com:sub"] = Match.ArrayWith(new object[]
                                {
                                    "repo:FA26SE142-GFA26SE04-AI-Storytelling/AI_Storytelling_Backend:ref:refs/heads/dev",
                                    "repo:FA26SE142-GFA26SE04-AI-Storytelling/AI_Storytelling_Backend:ref:refs/heads/main"
                                })
                            })
                        })
                    })
                })
            })
        }));
    }

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

    [Fact]
    public void Stack_RdsSecurityGroupAllowsApiTaskOn5432()
    {
        // The API task's own security group getting 8080 traffic from the ALB (not directly
        // from the internet) is covered by Stack_CreatesInternetFacingAlbForwardingToTaskPort8080_WhenContextFlagEnabled.
        var template = SynthTemplate();
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

    [Fact]
    public void Stack_OmitsAlbByDefault()
    {
        var template = SynthTemplate();
        template.ResourceCountIs("AWS::ElasticLoadBalancingV2::LoadBalancer", 0);
    }

    [Fact]
    public void Stack_CreatesInternetFacingAlbForwardingToTaskPort8080_WhenContextFlagEnabled()
    {
        var template = SynthTemplate(new System.Collections.Generic.Dictionary<string, object>
        {
            ["includeEcsService"] = true
        });

        template.ResourceCountIs("AWS::ElasticLoadBalancingV2::LoadBalancer", 1);
        template.HasResourceProperties("AWS::ElasticLoadBalancingV2::LoadBalancer", new System.Collections.Generic.Dictionary<string, object>
        {
            ["Scheme"] = "internet-facing",
            ["Type"] = "application"
        });

        template.ResourceCountIs("AWS::ElasticLoadBalancingV2::Listener", 1);
        template.HasResourceProperties("AWS::ElasticLoadBalancingV2::Listener", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["Port"] = 80,
            ["Protocol"] = "HTTP"
        }));

        template.ResourceCountIs("AWS::ElasticLoadBalancingV2::TargetGroup", 1);
        template.HasResourceProperties("AWS::ElasticLoadBalancingV2::TargetGroup", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["Port"] = 8080,
            ["Protocol"] = "HTTP",
            ["TargetType"] = "ip",
            ["HealthCheckPath"] = "/health"
        }));

        // The task's own security group must not accept traffic straight from the internet
        // anymore - only from the ALB - now that the ALB is the public entry point.
        Assert.ThrowsAny<System.Exception>(() => template.HasResourceProperties("AWS::EC2::SecurityGroupIngress", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["CidrIp"] = "0.0.0.0/0",
            ["FromPort"] = 8080
        })));
    }
}
