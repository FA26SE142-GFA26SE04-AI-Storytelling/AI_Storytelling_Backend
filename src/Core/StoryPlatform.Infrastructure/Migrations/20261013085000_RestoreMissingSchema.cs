using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StoryPlatform.Infrastructure.Persistence;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations;

/// <summary>
/// Restores schema changes present in the moderator snapshot but missing from the
/// migration chain. Must precede AgeBand9To10, which updates the category age array.
/// Existing installations may already contain these changes; preserve their data.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261013085000_RestoreMissingSchema")]
public partial class RestoreMissingSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE public.content_categories ADD COLUMN IF NOT EXISTS "DefaultBlockedAgeBands" text[] NOT NULL DEFAULT '{}';
            ALTER TABLE public.user_accounts ADD COLUMN IF NOT EXISTS "EmailVerificationFailedAttempts" integer NOT NULL DEFAULT 0;
            ALTER TABLE public.user_accounts ADD COLUMN IF NOT EXISTS "EmailVerifiedAt" timestamp with time zone;
            ALTER TABLE public.user_accounts ADD COLUMN IF NOT EXISTS "PasswordChangedAt" timestamp with time zone;
            ALTER TABLE public.user_accounts ADD COLUMN IF NOT EXISTS "PendingEmail" character varying(150);
            ALTER TABLE public.user_accounts ADD COLUMN IF NOT EXISTS "PendingEmailOtpExpiresAt" timestamp with time zone;
            ALTER TABLE public.user_accounts ADD COLUMN IF NOT EXISTS "PendingEmailOtpFailedAttempts" integer NOT NULL DEFAULT 0;
            ALTER TABLE public.user_accounts ADD COLUMN IF NOT EXISTS "PendingEmailOtpHash" character varying(64);
            ALTER TABLE public.supervision_invitations ADD COLUMN IF NOT EXISTS "OtpExpiresAt" timestamp with time zone;
            ALTER TABLE public.supervision_invitations ADD COLUMN IF NOT EXISTS "OtpFailedAttempts" integer NOT NULL DEFAULT 0;
            ALTER TABLE public.supervision_invitations ADD COLUMN IF NOT EXISTS "OtpHash" character varying(64);
            ALTER TABLE public.supervision_invitations ADD COLUMN IF NOT EXISTS "OtpVerifiedAt" timestamp with time zone;
            ALTER TABLE public.safety_policies ADD COLUMN IF NOT EXISTS "AllowInteractiveStories" boolean NOT NULL DEFAULT TRUE;
            ALTER TABLE public.safety_policies ADD COLUMN IF NOT EXISTS "ChildCreationEnabled" boolean NOT NULL DEFAULT TRUE;
            ALTER TABLE public.safety_policies ADD COLUMN IF NOT EXISTS "ChildCreationMode" character varying(30) NOT NULL DEFAULT 'ReviewFirst';
            ALTER TABLE public.safety_policies ADD COLUMN IF NOT EXISTS "ConsentedByUserId" integer;
            ALTER TABLE public.safety_policies ADD COLUMN IF NOT EXISTS "StoryWishesEnabled" boolean NOT NULL DEFAULT TRUE;
            ALTER TABLE public.safety_policies ADD COLUMN IF NOT EXISTS "VoiceAnswersEnabled" boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE public.safety_policies ADD COLUMN IF NOT EXISTS "VoiceConsentAt" timestamp with time zone;
            ALTER TABLE public.safety_policies ADD COLUMN IF NOT EXISTS "VoiceEnableRequestedAt" timestamp with time zone;
            ALTER TABLE public.notifications ADD COLUMN IF NOT EXISTS "ChildProfileId" integer;
            ALTER TABLE public.learning_profiles ADD COLUMN IF NOT EXISTS "ReadingMode" character varying(30) NOT NULL DEFAULT 'ListenFirst';
            ALTER TABLE public.data_requests ADD COLUMN IF NOT EXISTS "RejectionReason" character varying(500);
            ALTER TABLE public.child_sessions ADD COLUMN IF NOT EXISTS "EntryMethod" character varying(30) NOT NULL DEFAULT 'ProfileSwitcher';
            ALTER TABLE public.child_profiles ADD COLUMN IF NOT EXISTS "AvatarId" character varying(100) NOT NULL DEFAULT 'avatar-default';
            ALTER TABLE public.child_profile_version_history ADD COLUMN IF NOT EXISTS "Source" character varying(30) NOT NULL DEFAULT 'Recommendation';

            -- These are new credential concepts, not renames of the old PIN,
            -- expiring code or temporary lock. Never treat an old code as a hash.
            ALTER TABLE public.child_access_credentials ADD COLUMN IF NOT EXISTS "RevokedAt" timestamp with time zone;
            ALTER TABLE public.child_access_credentials ADD COLUMN IF NOT EXISTS "EasyLoginCreatedAt" timestamp with time zone;
            ALTER TABLE public.child_access_credentials ADD COLUMN IF NOT EXISTS "EasyLoginSecretHash" character varying(64);

            -- Retain legacy fields for audit, but allow the current entity to insert
            -- without sending properties that no longer exist in its model.
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'child_access_credentials' AND column_name = 'PinHash') THEN
                    ALTER TABLE public.child_access_credentials ALTER COLUMN "PinHash" DROP NOT NULL;
                END IF;
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'child_access_credentials' AND column_name = 'FailedAttempts') THEN
                    ALTER TABLE public.child_access_credentials ALTER COLUMN "FailedAttempts" SET DEFAULT 0;
                END IF;
            END $$;

            CREATE INDEX IF NOT EXISTS "IX_safety_policies_ConsentedByUserId" ON public.safety_policies ("ConsentedByUserId");
            CREATE INDEX IF NOT EXISTS "IX_notifications_ChildProfileId" ON public.notifications ("ChildProfileId");
            DROP INDEX IF EXISTS public."IX_child_access_credentials_ChildProfileId";
            CREATE UNIQUE INDEX "IX_child_access_credentials_ChildProfileId" ON public.child_access_credentials ("ChildProfileId") WHERE "RevokedAt" IS NULL AND "IsDeleted" = false;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_child_access_credentials_EasyLoginSecretHash" ON public.child_access_credentials ("EasyLoginSecretHash") WHERE "EasyLoginSecretHash" IS NOT NULL;

            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.notifications'::regclass AND conname = 'FK_notifications_child_profiles_ChildProfileId') THEN
                    ALTER TABLE public.notifications ADD CONSTRAINT "FK_notifications_child_profiles_ChildProfileId" FOREIGN KEY ("ChildProfileId") REFERENCES public.child_profiles ("Id") ON DELETE RESTRICT;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.safety_policies'::regclass AND conname = 'FK_safety_policies_user_accounts_ConsentedByUserId') THEN
                    ALTER TABLE public.safety_policies ADD CONSTRAINT "FK_safety_policies_user_accounts_ConsentedByUserId" FOREIGN KEY ("ConsentedByUserId") REFERENCES public.user_accounts ("Id") ON DELETE RESTRICT;
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Some installations already had these fields before this repair was
        // recorded. Dropping them here could erase pre-existing production data.
        throw new System.NotSupportedException(
            "RestoreMissingSchema cannot be rolled back automatically. Restore a database backup for a full rollback.");
    }
}
