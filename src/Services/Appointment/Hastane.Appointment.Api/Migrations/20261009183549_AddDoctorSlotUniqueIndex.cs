using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hastane.Appointment.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorSlotUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_Appointments_Doctor_Slot_Active",
                table: "Appointments",
                columns: new[] { "DoctorId", "AppointmentDate" },
                unique: true,
                filter: "[Status] <> N'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Appointments_Doctor_Slot_Active",
                table: "Appointments");
        }
    }
}
