using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProxyManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProxyId",
                table: "Accounts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OutboundProxies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "manual"),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Password = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Secret = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ResinPlatform = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ResinAdminUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ResinAdminToken = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    TestStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "unknown"),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    LastLatencyMs = table.Column<int>(type: "INTEGER", nullable: true),
                    EgressIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    EgressCountry = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EgressCity = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EgressIsp = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LastTestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FirstBoundAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundProxies", x => x.Id);
                    table.CheckConstraint("CK_OutboundProxies_Kind", "\"Kind\" IN ('manual', 'resin', 'warp')");
                    table.CheckConstraint("CK_OutboundProxies_LastLatencyMs", "\"LastLatencyMs\" IS NULL OR \"LastLatencyMs\" >= 0");
                    table.CheckConstraint("CK_OutboundProxies_Port", "\"Port\" BETWEEN 1 AND 65535");
                    table.CheckConstraint("CK_OutboundProxies_Protocol", "\"Protocol\" IN ('http', 'socks5', 'mtproto')");
                    table.CheckConstraint("CK_OutboundProxies_TestStatus", "\"TestStatus\" IN ('unknown', 'ok', 'fail')");
                });

            migrationBuilder.CreateTable(
                name: "WarpProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProfileId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RequestId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    OutboundProxyId = table.Column<int>(type: "INTEGER", nullable: true),
                    ContainerName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContainerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    VolumeName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    HostPort = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "pending"),
                    DesiredEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    EgressIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    WarpStatus = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarpProfiles", x => x.Id);
                    table.CheckConstraint("CK_WarpProfiles_HostPort", "\"HostPort\" BETWEEN 1 AND 65535");
                    table.ForeignKey(
                        name: "FK_WarpProfiles_OutboundProxies_OutboundProxyId",
                        column: x => x.OutboundProxyId,
                        principalTable: "OutboundProxies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ProxyId",
                table: "Accounts",
                column: "ProxyId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundProxies_EgressIp",
                table: "OutboundProxies",
                column: "EgressIp");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundProxies_IsEnabled_Kind",
                table: "OutboundProxies",
                columns: new[] { "IsEnabled", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundProxies_Name",
                table: "OutboundProxies",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundProxies_Protocol",
                table: "OutboundProxies",
                column: "Protocol");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundProxies_TestStatus",
                table: "OutboundProxies",
                column: "TestStatus");

            migrationBuilder.CreateIndex(
                name: "IX_WarpProfiles_ContainerId",
                table: "WarpProfiles",
                column: "ContainerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarpProfiles_ContainerName",
                table: "WarpProfiles",
                column: "ContainerName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarpProfiles_DesiredEnabled",
                table: "WarpProfiles",
                column: "DesiredEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_WarpProfiles_HostPort",
                table: "WarpProfiles",
                column: "HostPort",
                unique: true,
                filter: "\"Status\" NOT IN ('deleted', 'failed')");

            migrationBuilder.CreateIndex(
                name: "IX_WarpProfiles_OutboundProxyId",
                table: "WarpProfiles",
                column: "OutboundProxyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarpProfiles_ProfileId",
                table: "WarpProfiles",
                column: "ProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarpProfiles_RequestId",
                table: "WarpProfiles",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarpProfiles_Status",
                table: "WarpProfiles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WarpProfiles_VolumeName",
                table: "WarpProfiles",
                column: "VolumeName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_OutboundProxies_ProxyId",
                table: "Accounts",
                column: "ProxyId",
                principalTable: "OutboundProxies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_OutboundProxies_ProxyId",
                table: "Accounts");

            migrationBuilder.DropTable(
                name: "WarpProfiles");

            migrationBuilder.DropTable(
                name: "OutboundProxies");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_ProxyId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ProxyId",
                table: "Accounts");
        }
    }
}
