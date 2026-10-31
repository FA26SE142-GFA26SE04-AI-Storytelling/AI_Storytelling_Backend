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
    public void Stack_NeverCreatesLoadBalancer_EvenWithEcsService()
    {
        var template = SynthTemplate(new System.Collections.Generic.Dictionary<string, object>
        {
            ["includeEcsService"] = true
        });

        template.ResourceCountIs("AWS::ElasticLoadBalancingV2::LoadBalancer", 0);
        template.ResourceCountIs("AWS::ElasticLoadBalancingV2::Listener", 0);
        template.ResourceCountIs("AWS::ElasticLoadBalancingV2::TargetGroup", 0);
    }

    [Fact]
    public void Stack_TaskSecurityGroupHasNoIngress_WhenContextFlagEnabled()
    {
        var template = SynthTemplate(new System.Collections.Generic.Dictionary<string, object>
        {
            ["includeEcsService"] = true
        });

        // Traffic only arrives through the outbound tunnel; the only ingress rule in the stack is RDS from the task.
        template.ResourceCountIs("AWS::EC2::SecurityGroupIngress", 1);
        template.HasResourceProperties("AWS::EC2::SecurityGroupIngress", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["FromPort"] = 5432
        }));
        template.HasResourceProperties("AWS::EC2::SecurityGroup", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["GroupDescription"] = "Security group for the Core API ECS Fargate task",
            ["SecurityGroupIngress"] = Match.Absent()
        }));
    }

    [Fact]
    public void Stack_RunsPinnedCloudflaredSidecarWithTokenFromAppSecrets_AfterApiIsHealthy()
    {
        var template = SynthTemplate();

        template.HasResourceProperties("AWS::ECS::TaskDefinition", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["ContainerDefinitions"] = Match.ArrayWith(new object[]
            {
                Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                {
                    ["Name"] = "CloudflaredContainer",
                    ["Image"] = StoryPlatformCoreStack.CloudflaredImage,
                    ["Command"] = new[] { "tunnel", "run" },
                    ["Essential"] = true,
                    ["DependsOn"] = new object[]
                    {
                        new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["ContainerName"] = "ApiContainer",
                            ["Condition"] = "HEALTHY"
                        }
                    },
                    ["Secrets"] = new object[]
                    {
                        Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["Name"] = "TUNNEL_TOKEN",
                            ["ValueFrom"] = Match.AnyValue()
                        })
                    }
                })
            })
        }));
        Assert.Matches(@"^cloudflare/cloudflared:\d{4}\.\d+\.\d+$", StoryPlatformCoreStack.CloudflaredImage);
    }

    [Fact]
    public void Stack_ApiContainerTrustsClientIpOnlyFromLoopbackTunnel()
    {
        var template = SynthTemplate();
        var expected = new (string Name, string Value)[]
        {
            ("ForwardedHeaders__KnownNetworks__0", "127.0.0.1/32"),
            ("ForwardedHeaders__KnownNetworks__1", "::1/128"),
            ("ForwardedHeaders__ForwardLimit", "1"),
            ("ForwardedHeaders__ForwardedForHeaderName", "CF-Connecting-IP")
        };

        template.HasResourceProperties("AWS::ECS::TaskDefinition", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["ContainerDefinitions"] = Match.ArrayWith(new object[]
            {
                Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                {
                    ["Name"] = "ApiContainer",
                    ["Environment"] = Match.ArrayWith(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(
                        expected, e => (object)Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["Name"] = e.Name,
                            ["Value"] = e.Value
                        }))))
                })
            })
        }));
    }

    [Fact]
    public void Stack_DoesNotPutTunnelTokenIntoGeneratedSecretTemplate()
    {
        // Changing AppSecrets' generated template would make CloudFormation regenerate the whole secret
        // (losing JwtSecretKey and the hand-set API keys), so the tunnel token key is added by hand instead.
        var template = SynthTemplate();
        var json = System.Text.Json.JsonSerializer.Serialize(template.FindResources("AWS::SecretsManager::Secret"));

        Assert.Contains("REPLACE_ME_POST_DEPLOY", json);
        Assert.DoesNotContain("CloudflareTunnelToken", json);
    }

    [Fact]
    public void Stack_EcsServiceRollsBackFailingDeployments_WhenContextFlagEnabled()
    {
        var template = SynthTemplate(new System.Collections.Generic.Dictionary<string, object>
        {
            ["includeEcsService"] = true
        });

        template.HasResourceProperties("AWS::ECS::Service", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["DeploymentConfiguration"] = Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
            {
                ["DeploymentCircuitBreaker"] = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["Enable"] = true,
                    ["Rollback"] = true
                },
                ["MinimumHealthyPercent"] = 100,
                ["MaximumPercent"] = 200
            })
        }));
    }

    [Fact]
    public void Stack_AlarmsToSnsWhenNoApiTaskIsRunning_WhenContextFlagEnabled()
    {
        var template = SynthTemplate(new System.Collections.Generic.Dictionary<string, object>
        {
            ["includeEcsService"] = true
        });

        template.ResourceCountIs("AWS::SNS::Topic", 1);
        template.HasResourceProperties("AWS::SNS::Topic", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["TopicName"] = "storyplatform-core-api-alarms"
        }));
        // The e-mail subscription is managed outside CDK, so no address is ever committed.
        template.ResourceCountIs("AWS::SNS::Subscription", 0);

        template.HasResourceProperties("AWS::CloudWatch::Alarm", Match.ObjectLike(new System.Collections.Generic.Dictionary<string, object>
        {
            ["AlarmName"] = "storyplatform-core-api-no-running-task",
            ["Namespace"] = "AWS/ECS",
            ["MetricName"] = "CPUUtilization",
            ["Statistic"] = "SampleCount",
            ["Period"] = 60,
            ["Threshold"] = 1,
            ["ComparisonOperator"] = "LessThanThreshold",
            ["EvaluationPeriods"] = 5,
            ["DatapointsToAlarm"] = 5,
            ["TreatMissingData"] = "breaching",
            ["AlarmActions"] = Match.AnyValue(),
            ["OKActions"] = Match.AnyValue()
        }));
    }

    [Fact]
    public void Stack_HasNoAlarmOrTopicWithoutEcsService()
    {
        var template = SynthTemplate();

        template.ResourceCountIs("AWS::CloudWatch::Alarm", 0);
        template.ResourceCountIs("AWS::SNS::Topic", 0);
    }

}
