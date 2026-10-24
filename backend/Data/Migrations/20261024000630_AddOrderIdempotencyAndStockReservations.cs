using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace LojaVirtual.Data.Migrations;

public partial class AddOrderIdempotencyAndStockReservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "IdempotencyKey", table: "Orders", type: "varchar(255)", maxLength: 255, nullable: true).Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.AddColumn<string>(name: "IdempotencyFingerprint", table: "Orders", type: "longtext", nullable: true).Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "StockReservations",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                OrderId = table.Column<int>(type: "int", nullable: false),
                ProductId = table.Column<int>(type: "int", nullable: false),
                Quantity = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false).Annotation("MySql:CharSet", "utf8mb4"),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                ReleasedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockReservations", x => x.Id);
                table.ForeignKey("FK_StockReservations_Orders_OrderId", x => x.OrderId, "Orders", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_StockReservations_Products_ProductId", x => x.ProductId, "Products", "Id", onDelete: ReferentialAction.Restrict);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(name: "IX_Orders_IdempotencyKey", table: "Orders", column: "IdempotencyKey", unique: true);
        migrationBuilder.CreateIndex(name: "IX_StockReservations_OrderId", table: "StockReservations", column: "OrderId");
        migrationBuilder.CreateIndex(name: "IX_StockReservations_ProductId", table: "StockReservations", column: "ProductId");
        migrationBuilder.CreateIndex(name: "IX_StockReservations_ProductId_Status_ExpiresAt", table: "StockReservations", columns: new[] { "ProductId", "Status", "ExpiresAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "StockReservations");
        migrationBuilder.DropIndex(name: "IX_Orders_IdempotencyKey", table: "Orders");
        migrationBuilder.DropColumn(name: "IdempotencyFingerprint", table: "Orders");
        migrationBuilder.DropColumn(name: "IdempotencyKey", table: "Orders");
    }
}
