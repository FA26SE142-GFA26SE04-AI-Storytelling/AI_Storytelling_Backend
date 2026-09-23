using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StoryPlatform.Api.Extensions;

public static class DatabaseSeedExtensions
{
    /// <summary>
    /// Runs the SQL files under the "Seed" directory (next to the app binaries) once,
    /// mirroring Database/Seed-Database.ps1, when SeedData:RunOnStartup is true. No-op
    /// otherwise, so this is safe to leave wired in permanently.
    /// </summary>
    public static void SeedDataIfRequested<TContext>(this IHost host, IConfiguration configuration)
        where TContext : DbContext
    {
        if (!configuration.GetValue<bool>("SeedData:RunOnStartup"))
        {
            return;
        }

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseSeed");

        var seedDirectory = Path.Combine(AppContext.BaseDirectory, "Seed");
        if (!Directory.Exists(seedDirectory))
        {
            logger.LogWarning("SeedData:RunOnStartup is true but seed directory '{SeedDirectory}' was not found.", seedDirectory);
            return;
        }

        var seedFiles = Directory.GetFiles(seedDirectory, "*.sql")
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (seedFiles.Length == 0)
        {
            logger.LogWarning("SeedData:RunOnStartup is true but no .sql files were found in '{SeedDirectory}'.", seedDirectory);
            return;
        }

        var script = new StringBuilder("BEGIN;\n\n");
        foreach (var file in seedFiles)
        {
            script.AppendLine(File.ReadAllText(file));
            script.AppendLine();
        }
        script.Append(ResetSequencesSql);
        script.Append("\nCOMMIT;");

        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            using var command = connection.CreateCommand();
            command.CommandText = script.ToString();
            command.CommandTimeout = 300;
            command.ExecuteNonQuery();

            logger.LogInformation(
                "Seed data applied successfully from {FileCount} file(s) in '{SeedDirectory}'.",
                seedFiles.Length,
                seedDirectory);
        }
        catch (Exception ex)
        {
            // A seed failure must not take down the whole app (unlike a failed migration) -
            // seeding is a one-time bootstrap step, not something the running app depends on.
            logger.LogError(ex, "Failed to apply seed data from '{SeedDirectory}'.", seedDirectory);
        }
    }

    // Resets every "Id" sequence to the current max value so app-generated inserts made
    // after seeding never collide with the fixed Ids baked into the seed .sql files.
    // Walks pg_class instead of a hardcoded table list so it can't drift from Database/Seed/.
    private const string ResetSequencesSql = """

        DO $$
        DECLARE
            rec record;
            seq text;
        BEGIN
            FOR rec IN
                -- Restrict to tables that actually have an "Id" column: pg_get_serial_sequence
                -- raises a hard error (not NULL) for a nonexistent column, which every table
                -- lacking one would hit - e.g. EF's own "__EFMigrationsHistory".
                SELECT c.relname AS table_name
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE c.relkind = 'r' AND n.nspname = 'public'
                  AND EXISTS (
                      SELECT 1 FROM pg_attribute a
                      WHERE a.attrelid = c.oid AND a.attname = 'Id' AND NOT a.attisdropped
                  )
            LOOP
                -- quote_ident: pg_get_serial_sequence lowercase-folds an unquoted table name,
                -- which fails for mixed-case tables like EF's own "__EFMigrationsHistory".
                seq := pg_get_serial_sequence(quote_ident(rec.table_name), 'Id');
                IF seq IS NOT NULL THEN
                    EXECUTE format('SELECT setval(%L, COALESCE((SELECT MAX("Id") FROM %I), 1))', seq, rec.table_name);
                END IF;
            END LOOP;
        END $$;

        """;
}
