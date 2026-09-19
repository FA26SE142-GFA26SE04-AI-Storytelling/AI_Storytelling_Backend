using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Constructs;

namespace StoryPlatform.Infra;

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
