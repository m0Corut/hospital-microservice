using Hastane.Order.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hastane.Order.Api.Migrations
{
    // Model değişmedi (Designer/snapshot güncellemesi yok): yalnız karakter kümesi düzeltmesi.
    // MariaDB sunucusunun varsayılanı latin1 ise tablolar latin1 oluşuyor ve Türkçe karakterler ("ş", "ı") ya
    // kayboluyor ya da "Incorrect string value" hatası veriyordu. Üç tabloyu utf8mb4 yapar.
    [DbContext(typeof(OrderDbContext))]
    [Migration("20261010150000_EnsureUtf8mb4")]
    public partial class EnsureUtf8mb4 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // char(36) Id sütunları FK ile bağlı: karakter kümesi değişirken FK kaldırılıp sonra aynen geri konur.
            migrationBuilder.DropForeignKey(name: "FK_OrderItems_Orders_OrderId", table: "OrderItems");
            migrationBuilder.Sql("ALTER TABLE `Orders` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            migrationBuilder.Sql("ALTER TABLE `OrderItems` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            migrationBuilder.Sql("ALTER TABLE `OrderCounters` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Orders_OrderId",
                table: "OrderItems",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Karakter kümesi geri alınmaz (veri kaybı olurdu).
        }
    }
}