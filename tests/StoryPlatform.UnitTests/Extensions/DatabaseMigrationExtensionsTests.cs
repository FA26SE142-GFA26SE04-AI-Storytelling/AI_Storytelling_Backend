using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StoryPlatform.Api.Extensions;
using Xunit;

namespace StoryPlatform.UnitTests.Extensions;

public sealed class ProbeDbContext : DbContext
{
    public ProbeDbContext(DbContextOptions<ProbeDbContext> options) : base(options) { }
    public DbSet<ProbeWidget> Widgets => Set<ProbeWidget>();
}

// Table name pinned to match what CreateProbeWidgets.Up() physically creates below
// (EF Core's default convention would otherwise expect "Widgets", the DbSet property name).
[Table("ProbeWidgets")]
public sealed class ProbeWidget
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

[DbContext(typeof(ProbeDbContext))]
[Migration("20260101000000_CreateProbeWidgets")]
public sealed class CreateProbeWidgets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProbeWidgets",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ProbeWidgets", x => x.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProbeWidgets");
    }
}

public class DatabaseMigrationExtensionsTests
{
    [Fact]
    public void ApplyPendingMigrations_RunsPendingMigrationAgainstResolvedContext()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<ProbeDbContext>(options => options.UseSqlite(connection));
            })
            .Build();

        host.ApplyPendingMigrations<ProbeDbContext>();

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();

        Assert.Contains("20260101000000_CreateProbeWidgets", context.Database.GetAppliedMigrations());
        Assert.Empty(context.Widgets.ToList());
    }
}
