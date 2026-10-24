using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace LojaVirtual.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261024011000_EnsureCustomersTable")]
public partial class EnsureCustomersTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The DEV database records the historical migrations but is missing this
        // model table. Keep the repair safe for environments where it already exists.
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS `Customers` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `Name` longtext CHARACTER SET utf8mb4 NOT NULL,
                `Email` longtext CHARACTER SET utf8mb4 NOT NULL,
                `Phone` longtext CHARACTER SET utf8mb4 NOT NULL,
                `CreatedAt` datetime(6) NOT NULL,
                CONSTRAINT `PK_Customers` PRIMARY KEY (`Id`)
            ) CHARACTER SET=utf8mb4;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately leave the table in place: this repair migration must not
        // delete customer data if rolled back after the table was pre-existing.
    }
}
