using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartTripPlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationExtensionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "CachedLocations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "CachedLocations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TripId",
                table: "CachedLocations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CachedLocations_TripId",
                table: "CachedLocations",
                column: "TripId");

            migrationBuilder.AddForeignKey(
                name: "FK_CachedLocations_Trips_TripId",
                table: "CachedLocations",
                column: "TripId",
                principalTable: "Trips",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CachedLocations_Trips_TripId",
                table: "CachedLocations");

            migrationBuilder.DropIndex(
                name: "IX_CachedLocations_TripId",
                table: "CachedLocations");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "CachedLocations");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "CachedLocations");

            migrationBuilder.DropColumn(
                name: "TripId",
                table: "CachedLocations");
        }
    }
}
