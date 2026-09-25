using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingSessionEntrySourceCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Written by hand: the model snapshot already carries this constraint (PR #54 folded it
            // into a regenerated Init), so `migrations add` produced an empty diff.
            // NOT VALID enforces the rule for new/updated rows without failing startup on any
            // pre-existing reading_sessions row that predates the constraint.
            migrationBuilder.Sql("""
                ALTER TABLE reading_sessions
                ADD CONSTRAINT "CK_reading_sessions_exactly_one_entry_source"
                CHECK (("ChildAccessCredentialId" IS NOT NULL AND "SupervisorSessionId" IS NULL) OR ("ChildAccessCredentialId" IS NULL AND "SupervisorSessionId" IS NOT NULL))
                NOT VALID;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_reading_sessions_exactly_one_entry_source",
                table: "reading_sessions");
        }
    }
}
