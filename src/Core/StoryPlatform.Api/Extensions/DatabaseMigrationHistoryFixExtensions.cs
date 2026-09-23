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
    //
    // Split into 3 separate commands (create / check / insert) rather than one parameterized
    // multi-statement string, and logged at Warning (not Information, which production's
    // Logging:LogLevel:Default filters out) - an earlier version combined everything into one
    // command and never proved whether it actually ran in production, since nothing about it
    // ever showed up in CloudWatch even at the ERROR level.
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

        logger.LogWarning("Migration history fix starting for '{MigrationId}'.", SquashedInitMigrationId);

        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            using (var createCommand = connection.CreateCommand())
            {
                createCommand.CommandText = """
                    CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                        "MigrationId" character varying(150) NOT NULL,
                        "ProductVersion" character varying(32) NOT NULL,
                        CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                    );
                    """;
                createCommand.ExecuteNonQuery();
            }

            bool alreadyApplied;
            using (var checkCommand = connection.CreateCommand())
            {
                checkCommand.CommandText = """
                    SELECT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = @migrationId);
                    """;
                var checkParam = checkCommand.CreateParameter();
                checkParam.ParameterName = "migrationId";
                checkParam.Value = SquashedInitMigrationId;
                checkCommand.Parameters.Add(checkParam);
                alreadyApplied = (bool)checkCommand.ExecuteScalar()!;
            }

            logger.LogWarning(
                "Migration history check for '{MigrationId}': already recorded = {AlreadyApplied}.",
                SquashedInitMigrationId,
                alreadyApplied);

            if (!alreadyApplied)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.CommandText = """
                    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                    VALUES (@migrationId, @productVersion);
                    """;

                var migrationIdParam = insertCommand.CreateParameter();
                migrationIdParam.ParameterName = "migrationId";
                migrationIdParam.Value = SquashedInitMigrationId;
                insertCommand.Parameters.Add(migrationIdParam);

                var productVersionParam = insertCommand.CreateParameter();
                productVersionParam.ParameterName = "productVersion";
                productVersionParam.Value = SquashedInitProductVersion;
                insertCommand.Parameters.Add(productVersionParam);

                var rowsInserted = insertCommand.ExecuteNonQuery();
                logger.LogWarning(
                    "Inserted migration history row for '{MigrationId}': {RowsInserted} row(s).",
                    SquashedInitMigrationId,
                    rowsInserted);
            }
        }
        catch (Exception ex)
        {
            // Must not block startup - if this fails, ApplyPendingMigrations right after
            // surfaces whatever error it always did, which is no worse than before this fix.
            logger.LogError(ex, "Failed to fix migration history for '{MigrationId}'.", SquashedInitMigrationId);
        }
    }
}
