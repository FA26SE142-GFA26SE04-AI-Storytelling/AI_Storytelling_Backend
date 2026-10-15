using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.Persistence;

public sealed class UnitOfWorkConcurrencyTests
{
    private sealed class ConflictingContext : ApplicationDbContext
    {
        public ConflictingContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateConcurrencyException("row was changed by someone else");
    }

    [Fact]
    public async Task SaveChangesAsync_ConcurrencyConflict_ThrowsConflictException()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused")
            .Options;
        await using var context = new ConflictingContext(options);
        var sut = new UnitOfWork(context);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => sut.SaveChangesAsync());

        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task CommitTransactionAsync_ConcurrencyConflict_ThrowsConflictException()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused")
            .Options;
        await using var context = new ConflictingContext(options);
        await using var sut = new UnitOfWork(context);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => sut.CommitTransactionAsync());

        Assert.Equal(409, exception.StatusCode);
    }
}
