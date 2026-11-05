using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LojaVirtual.Data.Migrations;

public partial class AddRealShippingQuoteFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ShippingCost",
            table: "Orders",
            type: "decimal(18,2)",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ShippingDeliveryTime",
            table: "Orders",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShippingProvider",
            table: "Orders",
            type: "longtext",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShippingQuoteId",
            table: "Orders",
            type: "varchar(80)",
            maxLength: 80,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ShippingQuotedAtUtc",
            table: "Orders",
            type: "datetime(6)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShippingServiceId",
            table: "Orders",
            type: "varchar(80)",
            maxLength: 80,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShippingServiceName",
            table: "Orders",
            type: "longtext",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ShippingCost", table: "Orders");
        migrationBuilder.DropColumn(name: "ShippingDeliveryTime", table: "Orders");
        migrationBuilder.DropColumn(name: "ShippingProvider", table: "Orders");
        migrationBuilder.DropColumn(name: "ShippingQuoteId", table: "Orders");
        migrationBuilder.DropColumn(name: "ShippingQuotedAtUtc", table: "Orders");
        migrationBuilder.DropColumn(name: "ShippingServiceId", table: "Orders");
        migrationBuilder.DropColumn(name: "ShippingServiceName", table: "Orders");
    }
}
