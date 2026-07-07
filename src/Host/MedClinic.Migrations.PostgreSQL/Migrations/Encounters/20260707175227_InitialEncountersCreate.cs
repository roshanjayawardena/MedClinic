using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MedClinic.Migrations.PostgreSQL.Migrations.Encounters
{
    /// <inheritdoc />
    public partial class InitialEncountersCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "encounters");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PerformedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "encounters",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClinicalNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    vitals_systolic_bp = table.Column<int>(type: "integer", nullable: true),
                    vitals_diastolic_bp = table.Column<int>(type: "integer", nullable: true),
                    vitals_heart_rate_bpm = table.Column<int>(type: "integer", nullable: true),
                    vitals_temperature_celsius = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    vitals_respiratory_rate = table.Column<int>(type: "integer", nullable: true),
                    vitals_spo2_percent = table.Column<int>(type: "integer", nullable: true),
                    vitals_weight_kg = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_encounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "encounter_diagnoses",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Icd10Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_encounter_diagnoses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_encounter_diagnoses_encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalSchema: "encounters",
                        principalTable: "encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_TenantId",
                schema: "encounters",
                table: "audit_entries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_encounter_diagnoses_EncounterId",
                schema: "encounters",
                table: "encounter_diagnoses",
                column: "EncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_encounters_TenantId",
                schema: "encounters",
                table: "encounters",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_encounters_TenantId_PatientId",
                schema: "encounters",
                table: "encounters",
                columns: new[] { "TenantId", "PatientId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "encounters");

            migrationBuilder.DropTable(
                name: "encounter_diagnoses",
                schema: "encounters");

            migrationBuilder.DropTable(
                name: "encounters",
                schema: "encounters");
        }
    }
}
