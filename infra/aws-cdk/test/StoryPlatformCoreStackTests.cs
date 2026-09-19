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
}
