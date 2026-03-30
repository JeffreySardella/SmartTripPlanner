using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTripPlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPreferenceSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "UserPreferences",
                type: "TEXT",
                nullable: false,
                defaultValue: "user");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "UserPreferences");
        }
    }
}
