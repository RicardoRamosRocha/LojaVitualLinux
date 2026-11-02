using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using LojaVirtual.Data;

#nullable disable

namespace LojaVirtual.Data.Migrations;

[Migration("20261102023156_AddMercadoPagoPaymentAttempts")]
[DbContext(typeof(AppDbContext))]
public partial class AddMercadoPagoPaymentAttempts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OrderPaymentAttempts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                OrderId = table.Column<int>(type: "int", nullable: false),
                Provider = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                AttemptNumber = table.Column<int>(type: "int", nullable: false),
                PreferenceId = table.Column<string>(type: "varchar(255)", nullable: true),
                CheckoutUrl = table.Column<string>(type: "longtext", nullable: true),
                PaymentId = table.Column<string>(type: "varchar(255)", nullable: true),
                Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                IdempotencyKey = table.Column<string>(type: "varchar(220)", maxLength: 220, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderPaymentAttempts", x => x.Id);
                table.ForeignKey("FK_OrderPaymentAttempts_Orders_OrderId", x => x.OrderId, "Orders", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_OrderPaymentAttempts_OrderId_AttemptNumber", "OrderPaymentAttempts", new[] { "OrderId", "AttemptNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_OrderPaymentAttempts_PaymentId", "OrderPaymentAttempts", "PaymentId", unique: true);
        migrationBuilder.CreateIndex("IX_OrderPaymentAttempts_PreferenceId", "OrderPaymentAttempts", "PreferenceId", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("OrderPaymentAttempts");
}
