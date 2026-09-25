using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_governance_metrics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MetricDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GenerationSuccessRate = table.Column<decimal>(type: "numeric(6,3)", nullable: true),
                    SafetyFlagRate = table.Column<decimal>(type: "numeric(6,3)", nullable: true),
                    RegenerationRate = table.Column<decimal>(type: "numeric(6,3)", nullable: true),
                    ApprovalRate = table.Column<decimal>(type: "numeric(6,3)", nullable: true),
                    AvgLatencyMs = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_governance_metrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "business_reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StoriesGenerated = table.Column<int>(type: "integer", nullable: false),
                    StoriesApproved = table.Column<int>(type: "integer", nullable: false),
                    StoriesRejected = table.Column<int>(type: "integer", nullable: false),
                    ReadingSessionsCompleted = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_plans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ApplicableScope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PriceVnd = table.Column<int>(type: "integer", nullable: false),
                    QuotaAmount = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ResetTokenHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ResetTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefreshTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RefreshTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EmailVerificationTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EmailVerificationTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedLoginAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MfaSecret = table.Column<string>(type: "text", nullable: true),
                    MfaEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TokenVersion = table.Column<int>(type: "integer", nullable: false),
                    RoleBeforeAdmin = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActorUserId = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: false),
                    BeforeState = table.Column<string>(type: "text", nullable: true),
                    AfterState = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_logs_user_accounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "content_categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByAdminId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_categories_user_accounts_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecipientUserId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notifications_user_accounts_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    VerificationStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    VerifiedByAdminId = table.Column<int>(type: "integer", nullable: true),
                    SuspendedByAdminId = table.Column<int>(type: "integer", nullable: true),
                    SuspendedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SuspensionReason = table.Column<string>(type: "text", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    ReactivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReactivatedByAdminId = table.Column<int>(type: "integer", nullable: true),
                    ClosureRequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_organizations_user_accounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_organizations_user_accounts_ReactivatedByAdminId",
                        column: x => x.ReactivatedByAdminId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_organizations_user_accounts_SuspendedByAdminId",
                        column: x => x.SuspendedByAdminId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_organizations_user_accounts_VerifiedByAdminId",
                        column: x => x.VerifiedByAdminId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prompt_catalog_versions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VersionNo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    GradeBand = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RestrictedKeywords = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByAdminId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_catalog_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_prompt_catalog_versions_user_accounts_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserAccountId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SessionScope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_user_accounts_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "child_profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerUserId = table.Column<int>(type: "integer", nullable: false),
                    Nickname = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    AgeBand = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Scope = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_child_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_child_profiles_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_child_profiles_user_accounts_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "class_groups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeacherUserId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    KnowledgeTreeExp = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_class_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_class_groups_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_class_groups_user_accounts_TeacherUserId",
                        column: x => x.TeacherUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "org_safety_policy_templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    MaxStoryLengthBaseline = table.Column<int>(type: "integer", nullable: true),
                    RequiredApprovalModeDefault = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_safety_policy_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_org_safety_policy_templates_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "organization_memberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OrgRole = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InvitedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_organization_memberships_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_organization_memberships_user_accounts_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_organization_memberships_user_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    PayerUserId = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: true),
                    TransactionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SepayTransactionId = table.Column<string>(type: "text", nullable: true),
                    QrCodeUrl = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_transactions_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_transactions_subscription_plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "subscription_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_transactions_user_accounts_PayerUserId",
                        column: x => x.PayerUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "achievements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    ExpTotal = table.Column<int>(type: "integer", nullable: false),
                    StreakDays = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_achievements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_achievements_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "badges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    BadgeCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EarnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_badges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_badges_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "child_access_credentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    AvatarId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PinHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EasyLoginCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EasyLoginExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EasyLoginUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_child_access_credentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_child_access_credentials_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_child_access_credentials_user_accounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "child_sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    SessionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_child_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_child_sessions_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "data_requests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: true),
                    RequestType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletionMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: "Anonymize"),
                    LegalBasisNote = table.Column<string>(type: "text", nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_data_requests_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_data_requests_user_accounts_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_data_requests_user_accounts_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "learning_insights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Observation = table.Column<string>(type: "text", nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_insights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_insights_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "learning_profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    ReadingLevel = table.Column<int>(type: "integer", nullable: false),
                    ComprehensionGoal = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_profiles_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "org_consent_records",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DecidedByUserId = table.Column<int>(type: "integer", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_consent_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_org_consent_records_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_org_consent_records_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_org_consent_records_user_accounts_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ownership_transfer_requests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    CurrentOwnerUserId = table.Column<int>(type: "integer", nullable: false),
                    TargetSupervisorUserId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ownership_transfer_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ownership_transfer_requests_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ownership_transfer_requests_user_accounts_CurrentOwnerUserId",
                        column: x => x.CurrentOwnerUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ownership_transfer_requests_user_accounts_TargetSupervisorU~",
                        column: x => x.TargetSupervisorUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "safety_policies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    MaxStoryLength = table.Column<int>(type: "integer", nullable: false),
                    RequiredApprovalMode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ParentalGateEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConsentRecorded = table.Column<bool>(type: "boolean", nullable: false),
                    ConsentRecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsentPolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    SafetyScoreThreshold = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    ReadabilityScoreThreshold = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    ComprehensionThresholdPercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 70m),
                    ComprehensionWindowSize = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_safety_policies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_safety_policies_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: true),
                    CoverImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Genre = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    MoralLesson = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AgeBand = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReadingLevel = table.Column<int>(type: "integer", nullable: true),
                    VocabularyLevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    ChildVisibleAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedReason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    AuthorUserId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stories_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stories_user_accounts_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supervision_invitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    InviterUserId = table.Column<int>(type: "integer", nullable: false),
                    InvitationCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InviteeEmail = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    InviteeUserId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supervision_invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supervision_invitations_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supervision_invitations_user_accounts_InviteeUserId",
                        column: x => x.InviteeUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supervision_invitations_user_accounts_InviterUserId",
                        column: x => x.InviterUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "token_quota_configs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: true),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    QuotaLimit = table.Column<int>(type: "integer", nullable: false),
                    QuotaUsed = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_token_quota_configs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_token_quota_configs_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_token_quota_configs_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_token_quota_configs_user_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "class_group_members",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClassGroupId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_class_group_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_class_group_members_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_class_group_members_class_groups_ClassGroupId",
                        column: x => x.ClassGroupId,
                        principalTable: "class_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "org_safety_policy_categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrgSafetyPolicyTemplateId = table.Column<int>(type: "integer", nullable: false),
                    ContentCategoryId = table.Column<int>(type: "integer", nullable: false),
                    Rule = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_safety_policy_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_org_safety_policy_categories_content_categories_ContentCate~",
                        column: x => x.ContentCategoryId,
                        principalTable: "content_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_org_safety_policy_categories_org_safety_policy_templates_Or~",
                        column: x => x.OrgSafetyPolicyTemplateId,
                        principalTable: "org_safety_policy_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "organization_permissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationMembershipId = table.Column<int>(type: "integer", nullable: false),
                    Permission = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    GrantedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_permissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_organization_permissions_organization_memberships_Organizat~",
                        column: x => x.OrganizationMembershipId,
                        principalTable: "organization_memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_organization_permissions_user_accounts_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recommendations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LearningInsightId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CurrentState = table.Column<string>(type: "text", nullable: false),
                    ProposedChange = table.Column<string>(type: "text", nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recommendations_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recommendations_learning_insights_LearningInsightId",
                        column: x => x.LearningInsightId,
                        principalTable: "learning_insights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "learning_profile_topics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LearningProfileId = table.Column<int>(type: "integer", nullable: false),
                    Topic = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Relation = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_profile_topics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_profile_topics_learning_profiles_LearningProfileId",
                        column: x => x.LearningProfileId,
                        principalTable: "learning_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "safety_policy_categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SafetyPolicyId = table.Column<int>(type: "integer", nullable: false),
                    ContentCategoryId = table.Column<int>(type: "integer", nullable: false),
                    Rule = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_safety_policy_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_safety_policy_categories_content_categories_ContentCategory~",
                        column: x => x.ContentCategoryId,
                        principalTable: "content_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_safety_policy_categories_safety_policies_SafetyPolicyId",
                        column: x => x.SafetyPolicyId,
                        principalTable: "safety_policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    AssignedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ClassGroupId = table.Column<int>(type: "integer", nullable: true),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_assignments_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assignments_class_groups_ClassGroupId",
                        column: x => x.ClassGroupId,
                        principalTable: "class_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assignments_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assignments_user_accounts_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "content_reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    ReporterUserId = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_reports_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_content_reports_user_accounts_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_content_reports_user_accounts_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reading_progress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    LastPageRead = table.Column<int>(type: "integer", nullable: false),
                    IsFavorited = table.Column<bool>(type: "boolean", nullable: false),
                    IsBookmarked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reading_progress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reading_progress_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reading_progress_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shared_stories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    SharedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ClassGroupId = table.Column<int>(type: "integer", nullable: false),
                    ShareMode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TeacherStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_stories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shared_stories_class_groups_ClassGroupId",
                        column: x => x.ClassGroupId,
                        principalTable: "class_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shared_stories_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shared_stories_user_accounts_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shared_stories_user_accounts_SharedByUserId",
                        column: x => x.SharedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    ContentCategoryId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_categories_content_categories_ContentCategoryId",
                        column: x => x.ContentCategoryId,
                        principalTable: "content_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_categories_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_versions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false),
                    EditType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EditorUserId = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OutlineOpening = table.Column<string>(type: "text", nullable: true),
                    OutlineDevelopment = table.Column<string>(type: "text", nullable: true),
                    OutlineEnding = table.Column<string>(type: "text", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: true),
                    Lesson = table.Column<string>(type: "text", nullable: true),
                    ReadabilityFkgl = table.Column<decimal>(type: "numeric(6,3)", nullable: true),
                    ReadabilityFre = table.Column<decimal>(type: "numeric(6,3)", nullable: true),
                    SafetyScore = table.Column<decimal>(type: "numeric(6,3)", nullable: true),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    OutlineApprovedByUserId = table.Column<int>(type: "integer", nullable: true),
                    OutlineApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_versions_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_versions_user_accounts_EditorUserId",
                        column: x => x.EditorUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_versions_user_accounts_OutlineApprovedByUserId",
                        column: x => x.OutlineApprovedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supervision_relationships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    SupervisorUserId = table.Column<int>(type: "integer", nullable: false),
                    SupervisionInvitationId = table.Column<int>(type: "integer", nullable: true),
                    SupervisorRole = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supervision_relationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supervision_relationships_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supervision_relationships_supervision_invitations_Supervisi~",
                        column: x => x.SupervisionInvitationId,
                        principalTable: "supervision_invitations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supervision_relationships_user_accounts_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supervision_relationships_user_accounts_SupervisorUserId",
                        column: x => x.SupervisorUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "child_profile_version_history",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    RecommendationId = table.Column<int>(type: "integer", nullable: true),
                    PreviousConfig = table.Column<string>(type: "text", nullable: false),
                    NewConfig = table.Column<string>(type: "text", nullable: false),
                    AppliedByUserId = table.Column<int>(type: "integer", nullable: true),
                    VersionStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_child_profile_version_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_child_profile_version_history_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_child_profile_version_history_recommendations_Recommendatio~",
                        column: x => x.RecommendationId,
                        principalTable: "recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_child_profile_version_history_user_accounts_AppliedByUserId",
                        column: x => x.AppliedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "intervention_cases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    RecommendationId = table.Column<int>(type: "integer", nullable: true),
                    TriggerType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SkillGapNotes = table.Column<string>(type: "text", nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TriggeringStoryId = table.Column<int>(type: "integer", nullable: true),
                    ResolutionType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotesAddedByUserId = table.Column<int>(type: "integer", nullable: true),
                    NotesAddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intervention_cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_intervention_cases_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_intervention_cases_recommendations_RecommendationId",
                        column: x => x.RecommendationId,
                        principalTable: "recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_intervention_cases_stories_TriggeringStoryId",
                        column: x => x.TriggeringStoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_intervention_cases_user_accounts_NotesAddedByUserId",
                        column: x => x.NotesAddedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_intervention_cases_user_accounts_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_reviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecommendationId = table.Column<int>(type: "integer", nullable: false),
                    ReviewerUserId = table.Column<int>(type: "integer", nullable: false),
                    Decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ModifiedValue = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    IsFinal = table.Column<bool>(type: "boolean", nullable: false),
                    HadFinalAuthority = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendation_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recommendation_reviews_recommendations_RecommendationId",
                        column: x => x.RecommendationId,
                        principalTable: "recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recommendation_reviews_user_accounts_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assignment_recipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssignmentId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<int>(type: "integer", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assignment_recipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_assignment_recipients_assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assignment_recipients_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assignment_recipients_user_accounts_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shared_story_recipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SharedStoryId = table.Column<int>(type: "integer", nullable: false),
                    RecipientUserId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_story_recipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shared_story_recipients_shared_stories_SharedStoryId",
                        column: x => x.SharedStoryId,
                        principalTable: "shared_stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shared_story_recipients_user_accounts_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "discussion_questions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    Question = table.Column<string>(type: "text", nullable: false),
                    IsMoralLesson = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discussion_questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_discussion_questions_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "media_contexts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_contexts", x => x.Id);
                    table.CheckConstraint("CK_media_contexts_revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_media_contexts_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quiz_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Question = table.Column<string>(type: "text", nullable: false),
                    CorrectAnswer = table.Column<string>(type: "text", nullable: true),
                    Choices = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quiz_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quiz_items_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_scenes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    SceneIndex = table.Column<int>(type: "integer", nullable: false),
                    TextRangeStart = table.Column<int>(type: "integer", nullable: false),
                    TextRangeEnd = table.Column<int>(type: "integer", nullable: false),
                    SceneText = table.Column<string>(type: "text", nullable: false),
                    VisualDescription = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_scenes", x => x.Id);
                    table.CheckConstraint("CK_story_scenes_scene_index", "\"SceneIndex\" >= 0");
                    table.CheckConstraint("CK_story_scenes_text_range", "\"TextRangeStart\" >= 0 AND \"TextRangeEnd\" > \"TextRangeStart\"");
                    table.ForeignKey(
                        name: "FK_story_scenes_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_vocabulary",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    Term = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Definition = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_vocabulary", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_vocabulary_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supervision_permission_requests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupervisionRelationshipId = table.Column<int>(type: "integer", nullable: false),
                    RequesterUserId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RespondedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supervision_permission_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supervision_permission_requests_supervision_relationships_S~",
                        column: x => x.SupervisionRelationshipId,
                        principalTable: "supervision_relationships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supervision_permission_requests_user_accounts_RequesterUser~",
                        column: x => x.RequesterUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supervision_permission_requests_user_accounts_RespondedByUs~",
                        column: x => x.RespondedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supervision_permissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupervisionRelationshipId = table.Column<int>(type: "integer", nullable: false),
                    Permission = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supervision_permissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supervision_permissions_supervision_relationships_Supervisi~",
                        column: x => x.SupervisionRelationshipId,
                        principalTable: "supervision_relationships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "o2o_assessments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssignmentRecipientId = table.Column<int>(type: "integer", nullable: false),
                    TeacherUserId = table.Column<int>(type: "integer", nullable: false),
                    BonusPoints = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    AssessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_o2o_assessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_o2o_assessments_assignment_recipients_AssignmentRecipientId",
                        column: x => x.AssignmentRecipientId,
                        principalTable: "assignment_recipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_o2o_assessments_user_accounts_TeacherUserId",
                        column: x => x.TeacherUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reading_sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    AssignmentRecipientId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: false),
                    PagesCompleted = table.Column<int>(type: "integer", nullable: false),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: true),
                    ChildAccessCredentialId = table.Column<int>(type: "integer", nullable: true),
                    SupervisorSessionId = table.Column<int>(type: "integer", nullable: true),
                    ForceExitRequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reading_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reading_sessions_assignment_recipients_AssignmentRecipientId",
                        column: x => x.AssignmentRecipientId,
                        principalTable: "assignment_recipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reading_sessions_child_access_credentials_ChildAccessCreden~",
                        column: x => x.ChildAccessCredentialId,
                        principalTable: "child_access_credentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reading_sessions_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reading_sessions_refresh_tokens_SupervisorSessionId",
                        column: x => x.SupervisorSessionId,
                        principalTable: "refresh_tokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reading_sessions_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reading_sessions_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_segments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StorySceneId = table.Column<int>(type: "integer", nullable: false),
                    SegmentOrder = table.Column<int>(type: "integer", nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false),
                    TextContent = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_segments", x => x.Id);
                    table.CheckConstraint("CK_story_segments_offsets", "\"StartOffset\" >= 0 AND \"EndOffset\" > \"StartOffset\"");
                    table.CheckConstraint("CK_story_segments_segment_order", "\"SegmentOrder\" >= 1");
                    table.ForeignKey(
                        name: "FK_story_segments_story_scenes_StorySceneId",
                        column: x => x.StorySceneId,
                        principalTable: "story_scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vocabulary_notebook_entries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    StoryVocabularyId = table.Column<int>(type: "integer", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vocabulary_notebook_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vocabulary_notebook_entries_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vocabulary_notebook_entries_story_vocabulary_StoryVocabular~",
                        column: x => x.StoryVocabularyId,
                        principalTable: "story_vocabulary",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supervision_permission_request_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupervisionPermissionRequestId = table.Column<int>(type: "integer", nullable: false),
                    Permission = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supervision_permission_request_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supervision_permission_request_items_supervision_permission~",
                        column: x => x.SupervisionPermissionRequestId,
                        principalTable: "supervision_permission_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quiz_attempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReadingSessionId = table.Column<int>(type: "integer", nullable: false),
                    QuizItemId = table.Column<int>(type: "integer", nullable: false),
                    AnswerGiven = table.Column<string>(type: "text", nullable: true),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: true),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quiz_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quiz_attempts_quiz_items_QuizItemId",
                        column: x => x.QuizItemId,
                        principalTable: "quiz_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quiz_attempts_reading_sessions_ReadingSessionId",
                        column: x => x.ReadingSessionId,
                        principalTable: "reading_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "telemetry_logs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReadingSessionId = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EventPayload = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_telemetry_logs_reading_sessions_ReadingSessionId",
                        column: x => x.ReadingSessionId,
                        principalTable: "reading_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "media_assets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    StorySceneId = table.Column<int>(type: "integer", nullable: true),
                    StorySegmentId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ValidationStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SceneIndex = table.Column<int>(type: "integer", nullable: true),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WordTimings = table.Column<string>(type: "text", nullable: true),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ProviderRequestId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ProviderAssetId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ValidationResultJson = table.Column<string>(type: "text", nullable: true),
                    ReferenceImageAssetId = table.Column<int>(type: "integer", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastValidationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.Id);
                    table.CheckConstraint("CK_media_assets_attempt_count", "\"AttemptCount\" >= 0");
                    table.CheckConstraint("CK_media_assets_segment_must_be_null_for_illustration", "\"Type\" <> 'Illustration' OR \"StorySegmentId\" IS NULL");
                    table.CheckConstraint("CK_media_assets_segment_required_for_audio", "\"Type\" <> 'TtsAudio' OR \"StorySegmentId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_media_assets_story_scenes_StorySceneId",
                        column: x => x.StorySceneId,
                        principalTable: "story_scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_media_assets_story_segments_StorySegmentId",
                        column: x => x.StorySegmentId,
                        principalTable: "story_segments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_media_assets_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_generation_jobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    PromptCatalogVersionId = table.Column<int>(type: "integer", nullable: true),
                    GenerationRequestId = table.Column<int>(type: "integer", nullable: true),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: true),
                    BaseStoryVersionId = table.Column<int>(type: "integer", nullable: true),
                    RequestedByUserId = table.Column<int>(type: "integer", nullable: true),
                    OperationKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Operation = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AttemptNo = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GuardrailResult = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    FallbackMessage = table.Column<string>(type: "text", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GenerationMetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_generation_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_generation_jobs_prompt_catalog_versions_PromptCatalog~",
                        column: x => x.PromptCatalogVersionId,
                        principalTable: "prompt_catalog_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_generation_jobs_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_generation_jobs_story_versions_BaseStoryVersionId",
                        column: x => x.BaseStoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_generation_jobs_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_generation_jobs_user_accounts_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_generation_requests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    SubmittedByUserId = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InputFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContextFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContextSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    AcceptedInputJson = table.Column<string>(type: "jsonb", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastRetryKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttemptStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GuardrailDecision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FallbackMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CanRetry = table.Column<bool>(type: "boolean", nullable: false),
                    GuardrailCheckVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GuardrailCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HandoffJobId = table.Column<int>(type: "integer", nullable: true),
                    HandoffCreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_generation_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_generation_requests_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_generation_requests_story_generation_jobs_HandoffJobId",
                        column: x => x.HandoffJobId,
                        principalTable: "story_generation_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_generation_requests_user_accounts_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "subscription_plans",
                columns: new[] { "Id", "ApplicableScope", "CreatedAt", "IsActive", "IsDeleted", "Name", "PriceVnd", "QuotaAmount", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, "Personal", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, "Personal", 49000, 50, null },
                    { 2, "Organization", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, "Organization", 99000, 150, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_achievements_ChildProfileId",
                table: "achievements",
                column: "ChildProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assignment_recipients_AssignmentId_ChildProfileId",
                table: "assignment_recipients",
                columns: new[] { "AssignmentId", "ChildProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assignment_recipients_CancelledByUserId",
                table: "assignment_recipients",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_assignment_recipients_ChildProfileId",
                table: "assignment_recipients",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_AssignedByUserId",
                table: "assignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_ChildProfileId",
                table: "assignments",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_ClassGroupId",
                table: "assignments",
                column: "ClassGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_StoryId",
                table: "assignments",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_ActorUserId",
                table: "audit_logs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_badges_ChildProfileId",
                table: "badges",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_child_access_credentials_ChildProfileId",
                table: "child_access_credentials",
                column: "ChildProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_child_access_credentials_CreatedByUserId",
                table: "child_access_credentials",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_child_access_credentials_EasyLoginCode",
                table: "child_access_credentials",
                column: "EasyLoginCode",
                unique: true,
                filter: "\"EasyLoginCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_child_profile_version_history_AppliedByUserId",
                table: "child_profile_version_history",
                column: "AppliedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_child_profile_version_history_ChildProfileId",
                table: "child_profile_version_history",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_child_profile_version_history_RecommendationId",
                table: "child_profile_version_history",
                column: "RecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_child_profiles_OrganizationId",
                table: "child_profiles",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_child_profiles_OwnerUserId",
                table: "child_profiles",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_child_sessions_ChildProfileId",
                table: "child_sessions",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_child_sessions_SessionKey",
                table: "child_sessions",
                column: "SessionKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_class_group_members_ChildProfileId",
                table: "class_group_members",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_class_group_members_ClassGroupId_ChildProfileId",
                table: "class_group_members",
                columns: new[] { "ClassGroupId", "ChildProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_class_groups_OrganizationId",
                table: "class_groups",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_class_groups_TeacherUserId",
                table: "class_groups",
                column: "TeacherUserId");

            migrationBuilder.CreateIndex(
                name: "IX_content_categories_Code",
                table: "content_categories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_categories_CreatedByAdminId",
                table: "content_categories",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_content_reports_ReporterUserId",
                table: "content_reports",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_content_reports_ReviewedByUserId",
                table: "content_reports",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_content_reports_StoryId",
                table: "content_reports",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_data_requests_ChildProfileId",
                table: "data_requests",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_data_requests_RequestedByUserId",
                table: "data_requests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_data_requests_ResolvedByUserId",
                table: "data_requests",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_discussion_questions_StoryVersionId",
                table: "discussion_questions",
                column: "StoryVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_intervention_cases_ChildProfileId",
                table: "intervention_cases",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_intervention_cases_NotesAddedByUserId",
                table: "intervention_cases",
                column: "NotesAddedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_intervention_cases_RecommendationId",
                table: "intervention_cases",
                column: "RecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_intervention_cases_ResolvedByUserId",
                table: "intervention_cases",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_intervention_cases_TriggeringStoryId",
                table: "intervention_cases",
                column: "TriggeringStoryId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_insights_ChildProfileId",
                table: "learning_insights",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_profile_topics_LearningProfileId",
                table: "learning_profile_topics",
                column: "LearningProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_profiles_ChildProfileId",
                table: "learning_profiles",
                column: "ChildProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StorySceneId_Type",
                table: "media_assets",
                columns: new[] { "StorySceneId", "Type" },
                unique: true,
                filter: "\"StorySceneId\" IS NOT NULL AND \"StorySegmentId\" IS NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StorySegmentId_Type",
                table: "media_assets",
                columns: new[] { "StorySegmentId", "Type" },
                unique: true,
                filter: "\"StorySegmentId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StoryVersionId",
                table: "media_assets",
                column: "StoryVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_media_contexts_StoryVersionId_Revision",
                table: "media_contexts",
                columns: new[] { "StoryVersionId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_RecipientUserId",
                table: "notifications",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_o2o_assessments_AssignmentRecipientId",
                table: "o2o_assessments",
                column: "AssignmentRecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_o2o_assessments_TeacherUserId",
                table: "o2o_assessments",
                column: "TeacherUserId");

            migrationBuilder.CreateIndex(
                name: "IX_org_consent_records_ChildProfileId",
                table: "org_consent_records",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_org_consent_records_DecidedByUserId",
                table: "org_consent_records",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_org_consent_records_OrganizationId",
                table: "org_consent_records",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_org_safety_policy_categories_ContentCategoryId",
                table: "org_safety_policy_categories",
                column: "ContentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_org_safety_policy_categories_OrgSafetyPolicyTemplateId_Cont~",
                table: "org_safety_policy_categories",
                columns: new[] { "OrgSafetyPolicyTemplateId", "ContentCategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_org_safety_policy_templates_OrganizationId",
                table: "org_safety_policy_templates",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_memberships_InvitedByUserId",
                table: "organization_memberships",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_memberships_OrganizationId_UserId",
                table: "organization_memberships",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_memberships_UserId",
                table: "organization_memberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_permissions_GrantedByUserId",
                table: "organization_permissions",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_permissions_OrganizationMembershipId_Permission",
                table: "organization_permissions",
                columns: new[] { "OrganizationMembershipId", "Permission" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organizations_CreatedByUserId",
                table: "organizations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_organizations_ReactivatedByAdminId",
                table: "organizations",
                column: "ReactivatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_organizations_SuspendedByAdminId",
                table: "organizations",
                column: "SuspendedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_organizations_VerifiedByAdminId",
                table: "organizations",
                column: "VerifiedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_ownership_transfer_requests_ChildProfileId",
                table: "ownership_transfer_requests",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ownership_transfer_requests_CurrentOwnerUserId",
                table: "ownership_transfer_requests",
                column: "CurrentOwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ownership_transfer_requests_TargetSupervisorUserId",
                table: "ownership_transfer_requests",
                column: "TargetSupervisorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_OrganizationId",
                table: "payment_transactions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_PayerUserId",
                table: "payment_transactions",
                column: "PayerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_PlanId",
                table: "payment_transactions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_TransactionCode",
                table: "payment_transactions",
                column: "TransactionCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prompt_catalog_versions_CreatedByAdminId",
                table: "prompt_catalog_versions",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_quiz_attempts_QuizItemId",
                table: "quiz_attempts",
                column: "QuizItemId");

            migrationBuilder.CreateIndex(
                name: "IX_quiz_attempts_ReadingSessionId",
                table: "quiz_attempts",
                column: "ReadingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_quiz_items_StoryVersionId",
                table: "quiz_items",
                column: "StoryVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_progress_ChildProfileId_StoryId",
                table: "reading_progress",
                columns: new[] { "ChildProfileId", "StoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reading_progress_StoryId",
                table: "reading_progress",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_sessions_AssignmentRecipientId",
                table: "reading_sessions",
                column: "AssignmentRecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_sessions_ChildAccessCredentialId",
                table: "reading_sessions",
                column: "ChildAccessCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_sessions_ChildProfileId",
                table: "reading_sessions",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_sessions_StoryId",
                table: "reading_sessions",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_sessions_StoryVersionId",
                table: "reading_sessions",
                column: "StoryVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_sessions_SupervisorSessionId",
                table: "reading_sessions",
                column: "SupervisorSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_reviews_RecommendationId",
                table: "recommendation_reviews",
                column: "RecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_reviews_ReviewerUserId",
                table: "recommendation_reviews",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_recommendations_ChildProfileId",
                table: "recommendations",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_recommendations_LearningInsightId",
                table: "recommendations",
                column: "LearningInsightId");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserAccountId",
                table: "refresh_tokens",
                column: "UserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_safety_policies_ChildProfileId",
                table: "safety_policies",
                column: "ChildProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_safety_policy_categories_ContentCategoryId",
                table: "safety_policy_categories",
                column: "ContentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_safety_policy_categories_SafetyPolicyId_ContentCategoryId",
                table: "safety_policy_categories",
                columns: new[] { "SafetyPolicyId", "ContentCategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shared_stories_ClassGroupId",
                table: "shared_stories",
                column: "ClassGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_shared_stories_ReviewedByUserId",
                table: "shared_stories",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_shared_stories_SharedByUserId",
                table: "shared_stories",
                column: "SharedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_shared_stories_StoryId_ClassGroupId",
                table: "shared_stories",
                columns: new[] { "StoryId", "ClassGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shared_story_recipients_RecipientUserId",
                table: "shared_story_recipients",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_shared_story_recipients_SharedStoryId",
                table: "shared_story_recipients",
                column: "SharedStoryId");

            migrationBuilder.CreateIndex(
                name: "IX_stories_AuthorUserId",
                table: "stories",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_stories_ChildProfileId",
                table: "stories",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_story_categories_ContentCategoryId",
                table: "story_categories",
                column: "ContentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_story_categories_StoryId_ContentCategoryId",
                table: "story_categories",
                columns: new[] { "StoryId", "ContentCategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_BaseStoryVersionId",
                table: "story_generation_jobs",
                column: "BaseStoryVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_GenerationRequestId",
                table: "story_generation_jobs",
                column: "GenerationRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_Operation_StoryVersionId",
                table: "story_generation_jobs",
                columns: new[] { "Operation", "StoryVersionId" },
                unique: true,
                filter: "\"StoryVersionId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_PromptCatalogVersionId",
                table: "story_generation_jobs",
                column: "PromptCatalogVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_RequestedByUserId_Operation_Operation~",
                table: "story_generation_jobs",
                columns: new[] { "RequestedByUserId", "Operation", "OperationKey" },
                unique: true,
                filter: "\"RequestedByUserId\" IS NOT NULL AND \"OperationKey\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_Status_Stage_LeaseExpiresAt",
                table: "story_generation_jobs",
                columns: new[] { "Status", "Stage", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_StoryId",
                table: "story_generation_jobs",
                column: "StoryId",
                unique: true,
                filter: "\"Operation\" IN ('GenerateOutline', 'RegenerateOutline') AND \"Status\" IN ('Pending', 'Processing') AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_StoryVersionId",
                table: "story_generation_jobs",
                column: "StoryVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_requests_HandoffJobId",
                table: "story_generation_requests",
                column: "HandoffJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_requests_StoryId",
                table: "story_generation_requests",
                column: "StoryId",
                unique: true,
                filter: "\"Status\" IN ('PendingInput', 'CheckingInput', 'InputAccepted') AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_requests_SubmittedByUserId_IdempotencyKey",
                table: "story_generation_requests",
                columns: new[] { "SubmittedByUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_scenes_StoryVersionId_SceneIndex",
                table: "story_scenes",
                columns: new[] { "StoryVersionId", "SceneIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_segments_StorySceneId_SegmentOrder",
                table: "story_segments",
                columns: new[] { "StorySceneId", "SegmentOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_EditorUserId",
                table: "story_versions",
                column: "EditorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_OutlineApprovedByUserId",
                table: "story_versions",
                column: "OutlineApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_StoryId",
                table: "story_versions",
                column: "StoryId",
                unique: true,
                filter: "\"IsCurrent\" = true AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_StoryId_VersionNo",
                table: "story_versions",
                columns: new[] { "StoryId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_vocabulary_StoryVersionId",
                table: "story_vocabulary",
                column: "StoryVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_supervision_invitations_ChildProfileId",
                table: "supervision_invitations",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_supervision_invitations_InvitationCode",
                table: "supervision_invitations",
                column: "InvitationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supervision_invitations_InviteeUserId",
                table: "supervision_invitations",
                column: "InviteeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_supervision_invitations_InviterUserId",
                table: "supervision_invitations",
                column: "InviterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_supervision_permission_request_items_SupervisionPermissionR~",
                table: "supervision_permission_request_items",
                column: "SupervisionPermissionRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_supervision_permission_requests_RequesterUserId",
                table: "supervision_permission_requests",
                column: "RequesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_supervision_permission_requests_RespondedByUserId",
                table: "supervision_permission_requests",
                column: "RespondedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_supervision_permission_requests_SupervisionRelationshipId",
                table: "supervision_permission_requests",
                column: "SupervisionRelationshipId");

            migrationBuilder.CreateIndex(
                name: "IX_supervision_permissions_SupervisionRelationshipId",
                table: "supervision_permissions",
                column: "SupervisionRelationshipId");

            migrationBuilder.CreateIndex(
                name: "IX_supervision_relationships_ChildProfileId",
                table: "supervision_relationships",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_supervision_relationships_RevokedByUserId",
                table: "supervision_relationships",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_supervision_relationships_SupervisionInvitationId",
                table: "supervision_relationships",
                column: "SupervisionInvitationId");

            migrationBuilder.CreateIndex(
                name: "IX_supervision_relationships_SupervisorUserId",
                table: "supervision_relationships",
                column: "SupervisorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_logs_ReadingSessionId",
                table: "telemetry_logs",
                column: "ReadingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_token_quota_configs_ChildProfileId",
                table: "token_quota_configs",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_token_quota_configs_OrganizationId",
                table: "token_quota_configs",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_token_quota_configs_UserId",
                table: "token_quota_configs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_Email",
                table: "user_accounts",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_Username",
                table: "user_accounts",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vocabulary_notebook_entries_ChildProfileId_StoryVocabularyId",
                table: "vocabulary_notebook_entries",
                columns: new[] { "ChildProfileId", "StoryVocabularyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vocabulary_notebook_entries_StoryVocabularyId",
                table: "vocabulary_notebook_entries",
                column: "StoryVocabularyId");

            migrationBuilder.AddForeignKey(
                name: "FK_story_generation_jobs_story_generation_requests_GenerationR~",
                table: "story_generation_jobs",
                column: "GenerationRequestId",
                principalTable: "story_generation_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_stories_child_profiles_ChildProfileId",
                table: "stories");

            migrationBuilder.DropForeignKey(
                name: "FK_prompt_catalog_versions_user_accounts_CreatedByAdminId",
                table: "prompt_catalog_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_stories_user_accounts_AuthorUserId",
                table: "stories");

            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_jobs_user_accounts_RequestedByUserId",
                table: "story_generation_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_requests_user_accounts_SubmittedByUserId",
                table: "story_generation_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_story_versions_user_accounts_EditorUserId",
                table: "story_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_story_versions_user_accounts_OutlineApprovedByUserId",
                table: "story_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_jobs_stories_StoryId",
                table: "story_generation_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_requests_stories_StoryId",
                table: "story_generation_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_story_versions_stories_StoryId",
                table: "story_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_jobs_story_versions_BaseStoryVersionId",
                table: "story_generation_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_jobs_story_versions_StoryVersionId",
                table: "story_generation_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_jobs_prompt_catalog_versions_PromptCatalog~",
                table: "story_generation_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_jobs_story_generation_requests_GenerationR~",
                table: "story_generation_jobs");

            migrationBuilder.DropTable(
                name: "achievements");

            migrationBuilder.DropTable(
                name: "ai_governance_metrics");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "badges");

            migrationBuilder.DropTable(
                name: "business_reports");

            migrationBuilder.DropTable(
                name: "child_profile_version_history");

            migrationBuilder.DropTable(
                name: "child_sessions");

            migrationBuilder.DropTable(
                name: "class_group_members");

            migrationBuilder.DropTable(
                name: "content_reports");

            migrationBuilder.DropTable(
                name: "data_requests");

            migrationBuilder.DropTable(
                name: "discussion_questions");

            migrationBuilder.DropTable(
                name: "intervention_cases");

            migrationBuilder.DropTable(
                name: "learning_profile_topics");

            migrationBuilder.DropTable(
                name: "media_assets");

            migrationBuilder.DropTable(
                name: "media_contexts");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "o2o_assessments");

            migrationBuilder.DropTable(
                name: "org_consent_records");

            migrationBuilder.DropTable(
                name: "org_safety_policy_categories");

            migrationBuilder.DropTable(
                name: "organization_permissions");

            migrationBuilder.DropTable(
                name: "ownership_transfer_requests");

            migrationBuilder.DropTable(
                name: "payment_transactions");

            migrationBuilder.DropTable(
                name: "quiz_attempts");

            migrationBuilder.DropTable(
                name: "reading_progress");

            migrationBuilder.DropTable(
                name: "recommendation_reviews");

            migrationBuilder.DropTable(
                name: "safety_policy_categories");

            migrationBuilder.DropTable(
                name: "shared_story_recipients");

            migrationBuilder.DropTable(
                name: "story_categories");

            migrationBuilder.DropTable(
                name: "supervision_permission_request_items");

            migrationBuilder.DropTable(
                name: "supervision_permissions");

            migrationBuilder.DropTable(
                name: "telemetry_logs");

            migrationBuilder.DropTable(
                name: "token_quota_configs");

            migrationBuilder.DropTable(
                name: "vocabulary_notebook_entries");

            migrationBuilder.DropTable(
                name: "learning_profiles");

            migrationBuilder.DropTable(
                name: "story_segments");

            migrationBuilder.DropTable(
                name: "org_safety_policy_templates");

            migrationBuilder.DropTable(
                name: "organization_memberships");

            migrationBuilder.DropTable(
                name: "subscription_plans");

            migrationBuilder.DropTable(
                name: "quiz_items");

            migrationBuilder.DropTable(
                name: "recommendations");

            migrationBuilder.DropTable(
                name: "safety_policies");

            migrationBuilder.DropTable(
                name: "shared_stories");

            migrationBuilder.DropTable(
                name: "content_categories");

            migrationBuilder.DropTable(
                name: "supervision_permission_requests");

            migrationBuilder.DropTable(
                name: "reading_sessions");

            migrationBuilder.DropTable(
                name: "story_vocabulary");

            migrationBuilder.DropTable(
                name: "story_scenes");

            migrationBuilder.DropTable(
                name: "learning_insights");

            migrationBuilder.DropTable(
                name: "supervision_relationships");

            migrationBuilder.DropTable(
                name: "assignment_recipients");

            migrationBuilder.DropTable(
                name: "child_access_credentials");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "supervision_invitations");

            migrationBuilder.DropTable(
                name: "assignments");

            migrationBuilder.DropTable(
                name: "class_groups");

            migrationBuilder.DropTable(
                name: "child_profiles");

            migrationBuilder.DropTable(
                name: "organizations");

            migrationBuilder.DropTable(
                name: "user_accounts");

            migrationBuilder.DropTable(
                name: "stories");

            migrationBuilder.DropTable(
                name: "story_versions");

            migrationBuilder.DropTable(
                name: "prompt_catalog_versions");

            migrationBuilder.DropTable(
                name: "story_generation_requests");

            migrationBuilder.DropTable(
                name: "story_generation_jobs");
        }
    }
}
