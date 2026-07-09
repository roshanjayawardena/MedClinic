using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedClinic.Migrations.PostgreSQL.Migrations.Notifications
{
    /// <inheritdoc />
    public partial class AddNotificationHangfireJobId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HangfireJobId",
                schema: "notifications",
                table: "notifications",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HangfireJobId",
                schema: "notifications",
                table: "notifications");
        }
    }
}
