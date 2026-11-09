using LojaVirtual.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace LojaVirtual.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261108120000_AddOrderNotificationOutbox")]
public partial class AddOrderNotificationOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OrderNotificationOutbox",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                OrderId = table.Column<int>(type: "int", nullable: false),
                EventType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                ProcessedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                AttemptCount = table.Column<int>(type: "int", nullable: false),
                NextAttemptAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                LeaseToken = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                LastError = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderNotificationOutbox", x => x.Id);
                table.ForeignKey(
                    "FK_OrderNotificationOutbox_Orders_OrderId",
                    x => x.OrderId,
                    "Orders",
                    "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OrderNotificationOutbox_OrderId_EventType",
            table: "OrderNotificationOutbox",
            columns: new[] { "OrderId", "EventType" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrderNotificationOutbox_ProcessedAtUtc_NextAttemptAtUtc_LeaseExpiresAtUtc",
            table: "OrderNotificationOutbox",
            columns: new[] { "ProcessedAtUtc", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "OrderNotificationOutbox");
}
