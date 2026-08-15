using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedClinic.Migrations.PostgreSQL.Migrations.Patients
{
    /// <inheritdoc />
    public partial class AddCreatedByModifiedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedById",
                schema: "patients",
                table: "patients",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedById",
                schema: "patients",
                table: "patients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedById",
                schema: "patients",
                table: "allergies",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedById",
                schema: "patients",
                table: "allergies",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedById",
                schema: "patients",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "ModifiedById",
                schema: "patients",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                schema: "patients",
                table: "allergies");

            migrationBuilder.DropColumn(
                name: "ModifiedById",
                schema: "patients",
                table: "allergies");
        }
    }
}
