using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hastane.Appointment.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Appointments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "Appointments");
        }
    }
}
