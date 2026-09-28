-- =============================================================================
-- Migration Verification Script
-- Purpose: Verify that EF Core migration history in database matches the
-- migration files in the codebase.
--
-- Usage:
--   1. Run this script to check migration status
--   2. If there are discrepancies, use the REMEDIATION section below
-- =============================================================================

-- =============================================================================
-- SECTION 1: CHECK CURRENT MIGRATION HISTORY IN DATABASE
-- =============================================================================
\echo '=========================================='
\echo 'CURRENT MIGRATION HISTORY IN DATABASE'
\echo '=========================================='
\echo ''

SELECT "MigrationId", "ProductVersion", 'Recorded in DB' AS Source
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId";

-- =============================================================================
-- SECTION 2: EXPECTED MIGRATIONS (from codebase)
-- =============================================================================
\echo ''
\echo '=========================================='
\echo 'EXPECTED MIGRATIONS (from EF Core files)'
\echo '=========================================='
\echo ''

-- List all expected migration IDs based on files in Migrations directory
-- Update this list whenever a new migration is added

\echo 'Expected migrations:'
\echo '  1. 20260924010248_Init'
\echo '  2. 20260925102343_AddReadingSessionEntrySourceCheck'
\echo '  3. 20260928005435_AddIllustrationBeatsPhase5'
\echo ''

-- =============================================================================
-- SECTION 3: CHECK FOR MISSING MIGRATIONS IN DATABASE
-- =============================================================================
\echo '=========================================='
\echo 'MISSING MIGRATIONS IN DATABASE'
\echo '=========================================='
\echo 'These migrations exist in files but NOT in database history:'
\echo ''

DO $$
DECLARE
    expected_migrations TEXT[] := ARRAY[
        '20260924010248_Init',
        '20260925102343_AddReadingSessionEntrySourceCheck',
        '20260928005435_AddIllustrationBeatsPhase5'
    ];
    migration_id TEXT;
    recorded BOOLEAN;
BEGIN
    FOREACH migration_id IN ARRAY expected_migrations
    LOOP
        SELECT EXISTS (
            SELECT 1 FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = migration_id
        ) INTO recorded;

        IF NOT recorded THEN
            RAISE NOTICE 'MISSING: %', migration_id;
        ELSE
            RAISE NOTICE 'FOUND:   %', migration_id;
        END IF;
    END LOOP;
END $$;

-- =============================================================================
-- SECTION 4: CHECK FOR ORPHANED DATABASE RECORDS
-- =============================================================================
\echo ''
\echo '=========================================='
\echo 'ORPHANED DATABASE RECORDS'
\echo '=========================================='
\echo 'These migrations exist in database but NOT as files:'
\echo ''

DO $$
DECLARE
    db_migration RECORD;
    expected_migrations TEXT[] := ARRAY[
        '20260924010248_Init',
        '20260925102343_AddReadingSessionEntrySourceCheck',
        '20260928005435_AddIllustrationBeatsPhase5'
    ];
BEGIN
    FOR db_migration IN
        SELECT "MigrationId" FROM "__EFMigrationsHistory"
    LOOP
        IF db_migration."MigrationId" = ANY(expected_migrations) THEN
            -- Migration is expected, do nothing
        ELSE
            RAISE NOTICE 'ORPHAN:  %', db_migration."MigrationId";
        END IF;
    END LOOP;
END $$;

-- =============================================================================
-- SECTION 5: SCHEMA VALIDATION
-- =============================================================================
\echo ''
\echo '=========================================='
\echo 'SCHEMA VALIDATION'
\echo '=========================================='
\echo 'Checking if expected tables/columns exist:'
\echo ''

-- Check illustration_beats table
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_name = 'illustration_beats'
    ) THEN
        RAISE NOTICE 'TABLE: illustration_beats - EXISTS';
    ELSE
        RAISE WARNING 'TABLE: illustration_beats - MISSING!';
    END IF;
END $$;

-- Check IllustrationBeatId column in media_assets
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'media_assets' AND column_name = 'IllustrationBeatId'
    ) THEN
        RAISE NOTICE 'COLUMN: media_assets.IllustrationBeatId - EXISTS';
    ELSE
        RAISE WARNING 'COLUMN: media_assets.IllustrationBeatId - MISSING!';
    END IF;
END $$;

-- =============================================================================
-- SECTION 6: REMEDIATION COMMANDS (run ONLY if needed)
-- =============================================================================
\echo ''
\echo '=========================================='
\echo 'REMEDIATION COMMANDS'
\echo '=========================================='
\echo 'UNCOMMENT AND RUN ONLY IF NEEDED:'
\echo ''

-- To mark a migration as applied (if file exists but DB doesn't have it):
-- INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
-- VALUES ('20260928005435_AddIllustrationBeatsPhase5', '8.0.13')
-- ON CONFLICT ("MigrationId") DO NOTHING;

-- To remove an orphaned migration record (if DB has it but file doesn't exist):
-- DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = 'MigrationIdHere';

-- To re-run the latest migration (if it failed partially):
-- DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928005435_AddIllustrationBeatsPhase5';
-- -- Then re-run: dotnet ef database update

\echo ''
\echo '=========================================='
\echo 'VERIFICATION COMPLETE'
\echo '=========================================='
