using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClampStoryLengthOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phu huynh chi duoc ha do dai truyen (300 ... mac dinh). So cu cao hon mac dinh -> ve "theo muc doc"; duoi 300 -> 300.
            // Mac dinh (LearningBatchOptions): 1500 neu be 9-10 tuoi hoac muc doc >= 4, con lai 900.
            migrationBuilder.Sql(
                """
                UPDATE safety_policies sp
                SET "StoryLengthTarget" = NULL, "UpdatedAt" = NOW()
                FROM child_profiles cp
                LEFT JOIN learning_profiles lp ON lp."ChildProfileId" = cp."Id" AND lp."IsDeleted" = FALSE
                WHERE sp."ChildProfileId" = cp."Id"
                  AND sp."StoryLengthTarget" IS NOT NULL
                  AND sp."StoryLengthTarget" > CASE
                        WHEN cp."AgeBand" = 'Age_9_10' OR COALESCE(lp."ReadingLevel", 0) >= 4 THEN 1500
                        ELSE 900
                      END;

                UPDATE safety_policies
                SET "StoryLengthTarget" = 300, "UpdatedAt" = NOW()
                WHERE "StoryLengthTarget" IS NOT NULL AND "StoryLengthTarget" < 300;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Khong khoi phuc: so cu vuot quy tac "chi duoc ha do dai truyen".
        }
    }
}
