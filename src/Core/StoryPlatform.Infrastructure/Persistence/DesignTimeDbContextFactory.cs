using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace StoryPlatform.Infrastructure.Persistence;

/// <summary>
/// Factory dùng cho công cụ Entity Framework Core CLI (Design-time Migrations) khi chạy lệnh add-migration / database-update.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var currentDir = Directory.GetCurrentDirectory();

        // Tìm kiếm appsettings.json tại project API hoặc thư mục hiện tại
        var configuration = new ConfigurationBuilder()
            .SetBasePath(currentDir)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("../StoryPlatform.Api/appsettings.json", optional: true)
            .AddJsonFile("../../StoryPlatform.Api/appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = ConnectionStringHelper.GetConnectionString(configuration);

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString, b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
