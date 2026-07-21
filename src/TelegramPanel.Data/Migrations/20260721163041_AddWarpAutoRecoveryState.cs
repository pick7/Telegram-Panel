using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWarpAutoRecoveryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                table: "WarpProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRecoveredAtUtc",
                table: "WarpProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRecoveryAttemptAtUtc",
                table: "WarpProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecoveryCount",
                table: "WarpProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                table: "WarpProfiles");

            migrationBuilder.DropColumn(
                name: "LastRecoveredAtUtc",
                table: "WarpProfiles");

            migrationBuilder.DropColumn(
                name: "LastRecoveryAttemptAtUtc",
                table: "WarpProfiles");

            migrationBuilder.DropColumn(
                name: "RecoveryCount",
                table: "WarpProfiles");
        }
    }
}
