using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledTaskName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "ScheduledTasks",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // 旧版没有名称字段，用任务类型回填，升级后可直接在界面改成更易辨识的名称。
            migrationBuilder.Sql(
                """
                UPDATE "ScheduledTasks"
                SET "Name" = CASE
                    WHEN TRIM(COALESCE("TaskType", '')) <> '' THEN "TaskType"
                    ELSE '计划任务'
                END
                WHERE TRIM(COALESCE("Name", '')) = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "ScheduledTasks");
        }
    }
}
