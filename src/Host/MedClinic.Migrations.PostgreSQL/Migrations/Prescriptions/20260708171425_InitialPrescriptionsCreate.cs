using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedClinic.Migrations.PostgreSQL.Migrations.Prescriptions
{
    /// <inheritdoc />
    public partial class InitialPrescriptionsCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "prescriptions");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "prescriptions",
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
                name: "closed_encounter_records",
                schema: "prescriptions",
                columns: table => new
                {
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_closed_encounter_records", x => x.EncounterId);
                });

            migrationBuilder.CreateTable(
                name: "prescriptions",
                schema: "prescriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DrugName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DosageInstructions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    QuantityDays = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DispensedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prescriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_TenantId",
                schema: "prescriptions",
                table: "audit_entries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_closed_encounter_records_TenantId",
                schema: "prescriptions",
                table: "closed_encounter_records",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_prescriptions_EncounterId",
                schema: "prescriptions",
                table: "prescriptions",
                column: "EncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_prescriptions_PatientId",
                schema: "prescriptions",
                table: "prescriptions",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_prescriptions_TenantId",
                schema: "prescriptions",
                table: "prescriptions",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "prescriptions");

            migrationBuilder.DropTable(
                name: "closed_encounter_records",
                schema: "prescriptions");

            migrationBuilder.DropTable(
                name: "prescriptions",
                schema: "prescriptions");
        }
    }
}
