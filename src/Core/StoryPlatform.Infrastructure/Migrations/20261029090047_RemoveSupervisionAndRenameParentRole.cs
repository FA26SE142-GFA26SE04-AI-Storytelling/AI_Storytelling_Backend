using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSupervisionAndRenameParentRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE user_accounts SET \"Role\" = 'User' WHERE \"Role\" = 'Parent';");
            migrationBuilder.Sql("UPDATE user_accounts SET \"RoleBeforeAdmin\" = 'User' WHERE \"RoleBeforeAdmin\" = 'Parent';");

            migrationBuilder.DropTable(
                name: "ownership_transfer_requests");

            migrationBuilder.DropTable(
                name: "supervision_permission_request_items");

            migrationBuilder.DropTable(
                name: "supervision_permissions");

            migrationBuilder.DropTable(
                name: "supervision_permission_requests");

            migrationBuilder.DropTable(
                name: "supervision_relationships");

            migrationBuilder.DropTable(
                name: "supervision_invitations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ownership_transfer_requests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    CurrentOwnerUserId = table.Column<int>(type: "integer", nullable: false),
                    TargetSupervisorUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                name: "supervision_invitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    InviteeUserId = table.Column<int>(type: "integer", nullable: true),
                    InviterUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InvitationCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InviteeEmail = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    OtpExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OtpFailedAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    OtpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OtpVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                name: "supervision_relationships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    RevokedByUserId = table.Column<int>(type: "integer", nullable: true),
                    SupervisionInvitationId = table.Column<int>(type: "integer", nullable: true),
                    SupervisorUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupervisorRole = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                name: "supervision_permission_requests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequesterUserId = table.Column<int>(type: "integer", nullable: false),
                    RespondedByUserId = table.Column<int>(type: "integer", nullable: true),
                    SupervisionRelationshipId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Permission = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                name: "supervision_permission_request_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupervisionPermissionRequestId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Permission = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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

            migrationBuilder.Sql("UPDATE user_accounts SET \"Role\" = 'Parent' WHERE \"Role\" = 'User';");
            migrationBuilder.Sql("UPDATE user_accounts SET \"RoleBeforeAdmin\" = 'Parent' WHERE \"RoleBeforeAdmin\" = 'User';");
        }
    }
}
