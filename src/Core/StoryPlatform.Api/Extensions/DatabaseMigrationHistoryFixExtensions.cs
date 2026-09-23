using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StoryPlatform.Api.Extensions;

public static class DatabaseMigrationHistoryFixExtensions
{
    // One-time fix for the 2026-09-22 migration squash ("fix(migrations): remove superseded
    // migration chain"): production's schema was already fully created under the old, now
    // deleted migration classes, but __EFMigrationsHistory never got a row for the new squashed
    // Init migration's id. Every startup since then tried (and failed) to recreate tables that
    // already existed - CREATE TABLE has no "IF NOT EXISTS" in EF's generated SQL - crashing
    // before the app could ever answer /health. Marks the new id as applied, without running any
    // of its SQL, only when it isn't already recorded - safe to run repeatedly, and a no-op on a
    // genuinely fresh database (nothing to fix; ApplyPendingMigrations proceeds normally).
    private const string SquashedInitMigrationId = "20260922113625_Init";
    private const string SquashedInitProductVersion = "8.0.13"; // Migrations/20260922113625_Init.Designer.cs

    public static void FixMigrationHistoryIfRequested<TContext>(this IHost host, IConfiguration configuration)
        where TContext : DbContext
    {
        if (!configuration.GetValue<bool>("FixMigrationHistory:RunOnStartup"))
        {
            return;
        }

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigrationHistoryFix");

        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                );

                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES (@migrationId, @productVersion)
                ON CONFLICT ("MigrationId") DO NOTHING;
                """;

            var migrationIdParam = command.CreateParameter();
            migrationIdParam.ParameterName = "migrationId";
            migrationIdParam.Value = SquashedInitMigrationId;
            command.Parameters.Add(migrationIdParam);

            var productVersionParam = command.CreateParameter();
            productVersionParam.ParameterName = "productVersion";
            productVersionParam.Value = SquashedInitProductVersion;
            command.Parameters.Add(productVersionParam);

            var rowsInserted = command.ExecuteNonQuery();
            logger.LogInformation(
                "Migration history fix ran for '{MigrationId}' ({RowsInserted} row(s) inserted; 0 means it was already recorded).",
                SquashedInitMigrationId,
                rowsInserted);
        }
        catch (Exception ex)
        {
            // Must not block startup - if this fails, ApplyPendingMigrations right after
            // surfaces whatever error it always did, which is no worse than before this fix.
            logger.LogError(ex, "Failed to fix migration history for '{MigrationId}'.", SquashedInitMigrationId);
        }
    }
}
