using Hastane.Order.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hastane.Order.Api.Migrations
{
    // Model değişmedi (Designer/snapshot güncellemesi yok): yalnız karakter kümesi düzeltmesi.
    // MariaDB sunucusunun varsayılanı latin1 ise tablolar latin1 oluşuyor ve Türkçe karakterler ("ş", "ı") ya
    // kayboluyor ya da "Incorrect string value" hatası veriyordu. Sipariş ve outbox tablolarını utf8mb4 yapar.
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

            // MassTransit outbox tabloları (önceki migration) da: olay gövdesi/hata başlıkları Türkçe metin taşıyabilir.
            migrationBuilder.DropForeignKey(name: "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId", table: "OutboxMessage");
            migrationBuilder.DropForeignKey(name: "FK_OutboxMessage_OutboxState_OutboxId", table: "OutboxMessage");
            migrationBuilder.Sql("ALTER TABLE `InboxState` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            migrationBuilder.Sql("ALTER TABLE `OutboxState` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            migrationBuilder.Sql("ALTER TABLE `OutboxMessage` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            migrationBuilder.AddForeignKey(
                name: "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId",
                table: "OutboxMessage",
                columns: new[] { "InboxMessageId", "InboxConsumerId" },
                principalTable: "InboxState",
                principalColumns: new[] { "MessageId", "ConsumerId" });
            migrationBuilder.AddForeignKey(
                name: "FK_OutboxMessage_OutboxState_OutboxId",
                table: "OutboxMessage",
                column: "OutboxId",
                principalTable: "OutboxState",
                principalColumn: "OutboxId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Karakter kümesi geri alınmaz (veri kaybı olurdu).
        }
    }
}