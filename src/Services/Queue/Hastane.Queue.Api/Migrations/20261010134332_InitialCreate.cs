using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hastane.Queue.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QueueEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PatientUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    QueueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CalledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_PatientUserId",
                table: "QueueEntries",
                column: "PatientUserId");

            migrationBuilder.CreateIndex(
                name: "UX_QueueEntries_Appointment",
                table: "QueueEntries",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_QueueEntries_Doctor_Day_Number",
                table: "QueueEntries",
                columns: new[] { "DoctorId", "QueueDate", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_QueueEntries_Doctor_Day_OneCalled",
                table: "QueueEntries",
                columns: new[] { "DoctorId", "QueueDate" },
                unique: true,
                filter: "[Status] = N'Called'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QueueEntries");
        }
    }
}
