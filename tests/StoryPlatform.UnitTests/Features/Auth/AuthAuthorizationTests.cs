using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Security;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Auth;

public class AuthAuthorizationTests
{
    private const string TestJwtSecretKey = "UnitTestJwtSecretKeyValue1234567890ABCDE";

    [Fact]
    public void GenerateAccessToken_ContainsCurrentTokenVersion()
    {
        var generator = new JwtTokenGenerator(BuildValidJwtConfiguration());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(generator.GenerateAccessToken(
            new UserAccount { Id = 1, TokenVersion = 7, Role = UserRole.Teacher }));

        Assert.Equal("7", jwt.Claims.Single(c => c.Type == "token_version").Value);
    }

    [Fact]
    public void GenerateChildAccessToken_ContainsProfileIdAndTokenTypeWithoutAdultClaims()
    {
        var generator = new JwtTokenGenerator(BuildValidJwtConfiguration());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(generator.GenerateChildAccessToken(42));

        Assert.Equal("42", jwt.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.NameId).Value);
        Assert.Equal("child", jwt.Claims.Single(claim => claim.Type == "token_type").Value);
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type == ClaimTypes.Role);
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type == "token_version");
    }

    [Theory]
    [InlineData("1", "2", AccountStatus.LoggedIn, false, true)]
    [InlineData("1", "1", AccountStatus.LoggedIn, false, false)]
    [InlineData("1", null, AccountStatus.LoggedIn, false, false)]
    [InlineData("1", "invalid", AccountStatus.LoggedIn, false, false)]
    [InlineData("invalid", "2", AccountStatus.LoggedIn, false, false)]
    [InlineData("1", "2", AccountStatus.Suspended, false, false)]
    [InlineData("1", "2", AccountStatus.Registered, false, false)]
    [InlineData("1", "2", AccountStatus.LoggedIn, true, false)]
    [InlineData("1", "2", AccountStatus.LoggedOut, false, true)]
    public async Task TokenValidation_RejectsInvalidOrRevokedCredentials(
        string userId, string? version, AccountStatus status, bool deleted, bool accepted)
    {
        var user = new UserAccount { Id = 1, TokenVersion = 2, Status = status, IsDeleted = deleted };
        await AssertValidationAsync(user, userId, version, UserRole.Parent, accepted);
    }

    [Fact]
    public async Task TokenValidation_MissingUser_RejectsToken()
    {
        await AssertValidationAsync(null, "1", "2", UserRole.Parent, false);
    }

    [Fact]
    public async Task TokenValidation_RoleChanged_RejectsOldRoleClaim()
    {
        var user = new UserAccount { Id = 1, TokenVersion = 2, Status = AccountStatus.LoggedIn, Role = UserRole.Teacher };
        await AssertValidationAsync(user, "1", "2", UserRole.Parent, false);
    }

    [Theory]
    [InlineData(ChildProfileStatus.Active, false, true)]
    [InlineData(ChildProfileStatus.Archived, false, false)]
    [InlineData(ChildProfileStatus.Active, true, false)]
    public async Task TokenValidation_ChildToken_ValidatesAgainstChildProfileNotUserAccount(
        ChildProfileStatus status, bool deleted, bool accepted)
    {
        var childProfile = new ChildProfile { Id = 42, Status = status, IsDeleted = deleted };
        var childProfileRepository = new Mock<IGenericRepository<ChildProfile>>();
        childProfileRepository.Setup(repository => repository.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ChildProfile, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(childProfile);
        var userRepository = new Mock<IGenericRepository<UserAccount>>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(work => work.Repository<ChildProfile>()).Returns(childProfileRepository.Object);
        unitOfWork.Setup(work => work.Repository<UserAccount>()).Returns(userRepository.Object);

        var context = CreateTokenValidatedContext(unitOfWork.Object, new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "42"),
            new("token_type", "child")
        });

        await context.Options.Events.TokenValidated(context);

        Assert.Equal(accepted, context.Result?.Failure == null);
        userRepository.Verify(repository => repository.FirstOrDefaultAsync(
            It.IsAny<Expression<Func<UserAccount, bool>>>(), null,
            It.IsAny<CancellationToken>()), Times.Never);
        (context.HttpContext.RequestServices as IDisposable)?.Dispose();
    }

    [Fact]
    public async Task TokenValidation_ChildToken_MissingProfile_RejectsToken()
    {
        var childProfileRepository = new Mock<IGenericRepository<ChildProfile>>();
        childProfileRepository.Setup(repository => repository.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ChildProfile, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChildProfile?)null);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(work => work.Repository<ChildProfile>()).Returns(childProfileRepository.Object);

        var context = CreateTokenValidatedContext(unitOfWork.Object, new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "999"),
            new("token_type", "child")
        });

        await context.Options.Events.TokenValidated(context);

        Assert.NotNull(context.Result?.Failure);
        (context.HttpContext.RequestServices as IDisposable)?.Dispose();
    }

    private static TokenValidatedContext CreateTokenValidatedContext(
        IUnitOfWork unitOfWork, IEnumerable<Claim> claims)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(unitOfWork);
        services.AddJwtAuthentication(BuildValidJwtConfiguration());
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        return new TokenValidatedContext(
            new DefaultHttpContext { RequestServices = provider },
            new AuthenticationScheme(
                JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            options)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
        };
    }

    private static async Task AssertValidationAsync(UserAccount? user, string userId, string? version, UserRole role, bool accepted)
    {
        var repository = new Mock<IGenericRepository<UserAccount>>();
        repository.Setup(r => r.FirstOrDefaultAsync(
            It.IsAny<System.Linq.Expressions.Expression<Func<UserAccount, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<UserAccount>()).Returns(repository.Object);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(unitOfWork.Object);
        services.AddJwtAuthentication(BuildValidJwtConfiguration());
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId), new(ClaimTypes.Role, role.ToString()) };
        if (version != null) claims.Add(new Claim("token_version", version));
        var context = new TokenValidatedContext(
            new DefaultHttpContext { RequestServices = provider },
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)), options)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
        };

        await options.Events.TokenValidated(context);

        Assert.Equal(accepted, context.Result?.Failure == null);
    }

    [Theory]
    [InlineData(UserRole.Parent, true)]
    [InlineData(UserRole.Teacher, true)]
    [InlineData(UserRole.Administrator, false)]
    public async Task GuardianRoutes_AllowOnlySupervisorRoles(UserRole role, bool expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role.ToString()) }, "Bearer"));
        var protectedMembers = new System.Reflection.MemberInfo[]
        {
            typeof(AIStoryInputController),
            typeof(ChildProfileController).GetMethod("CreateChildProfile")!,
            typeof(StoryController).GetMethod("CreateStory")!,
            typeof(StoryController).GetMethod("UpdateStory")!,
            typeof(StoryController).GetMethod("DeleteStory")!,
            typeof(StoryController).GetMethod("PublishStory")!
        };
        foreach (var member in protectedMembers)
        {
            var attributes = member.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<IAuthorizeData>();
            var policy = await AuthorizationPolicy.CombineAsync(policyProvider, attributes);
            Assert.NotNull(policy);
            var result = await authorization.AuthorizeAsync(user, null, policy!);
            Assert.Equal(expected, result.Succeeded);
        }
        foreach (var action in new[] { "GetStories", "GetStoryById" })
        {
            Assert.NotEmpty(typeof(StoryController).GetMethod(action)!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        }
    }

    private static IConfiguration BuildValidJwtConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = TestJwtSecretKey,
                ["JwtSettings:Issuer"] = "StoryPlatform",
                ["JwtSettings:Audience"] = "StoryPlatformClient",
                ["JwtSettings:ExpiryMinutes"] = "120",
                ["JwtSettings:RefreshTokenExpiryDays"] = "7",
                ["JwtSettings:ChildTokenExpiryMinutes"] = "240"
            })
            .Build();
}
