using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LojaVirtual.Data.Migrations;

/// <summary>
/// Baseline idempotente do schema de domínio legado.
///
/// O banco local já possuía estas tabelas antes do histórico EF Core atual.
/// CREATE TABLE IF NOT EXISTS permite registrar a baseline em bancos existentes
/// sem tentar recriar tabelas, enquanto um banco vazio é criado completamente.
/// </summary>
public partial class InitialDomainSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS `Addresses` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `ZipCode` longtext CHARACTER SET utf8mb4 NOT NULL,
                `Street` longtext CHARACTER SET utf8mb4 NOT NULL,
                `Number` longtext CHARACTER SET utf8mb4 NOT NULL,
                `Neighborhood` longtext CHARACTER SET utf8mb4 NOT NULL,
                `City` longtext CHARACTER SET utf8mb4 NOT NULL,
                `State` longtext CHARACTER SET utf8mb4 NOT NULL,
                CONSTRAINT `PK_Addresses` PRIMARY KEY (`Id`)
            ) CHARACTER SET=utf8mb4;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS `Categories` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `Name` longtext CHARACTER SET utf8mb4 NOT NULL,
                CONSTRAINT `PK_Categories` PRIMARY KEY (`Id`)
            ) CHARACTER SET=utf8mb4;
            """);

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

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS `Orders` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `CustomerName` longtext CHARACTER SET utf8mb4 NOT NULL,
                `Email` longtext CHARACTER SET utf8mb4 NOT NULL,
                `Phone` longtext CHARACTER SET utf8mb4 NOT NULL,
                `Address` longtext CHARACTER SET utf8mb4 NOT NULL,
                `Total` decimal(65,30) NOT NULL,
                `Status` longtext CHARACTER SET utf8mb4 NOT NULL,
                `CreatedAt` datetime(6) NOT NULL,
                `ZipCode` longtext CHARACTER SET utf8mb4 NOT NULL,
                `ShippingMethod` longtext CHARACTER SET utf8mb4 NOT NULL,
                CONSTRAINT `PK_Orders` PRIMARY KEY (`Id`)
            ) CHARACTER SET=utf8mb4;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS `Products` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `Name` longtext CHARACTER SET utf8mb4 NOT NULL,
                `Description` longtext CHARACTER SET utf8mb4 NULL,
                `Price` decimal(65,30) NOT NULL,
                `Stock` int NOT NULL,
                `IsActive` tinyint(1) NOT NULL,
                `IsFeatured` tinyint(1) NOT NULL,
                `IsNewCollection` tinyint(1) NOT NULL,
                `IsBestSeller` tinyint(1) NOT NULL,
                `CreatedAt` datetime(6) NOT NULL,
                `ImageUrl` longtext CHARACTER SET utf8mb4 NULL,
                `VideoUrl` longtext CHARACTER SET utf8mb4 NULL,
                `CategoryId` int NULL,
                CONSTRAINT `PK_Products` PRIMARY KEY (`Id`),
                CONSTRAINT `FK_Products_Categories_CategoryId`
                    FOREIGN KEY (`CategoryId`) REFERENCES `Categories` (`Id`)
            ) CHARACTER SET=utf8mb4;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS `OrderItems` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `OrderId` int NOT NULL,
                `ProductId` int NOT NULL,
                `Quantity` int NOT NULL,
                `UnitPrice` decimal(65,30) NOT NULL,
                CONSTRAINT `PK_OrderItems` PRIMARY KEY (`Id`),
                CONSTRAINT `FK_OrderItems_Orders_OrderId`
                    FOREIGN KEY (`OrderId`) REFERENCES `Orders` (`Id`) ON DELETE CASCADE,
                CONSTRAINT `FK_OrderItems_Products_ProductId`
                    FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE,
                INDEX `IX_OrderItems_OrderId` (`OrderId`),
                INDEX `IX_OrderItems_ProductId` (`ProductId`)
            ) CHARACTER SET=utf8mb4;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS `ProductMedias` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `Url` longtext CHARACTER SET utf8mb4 NOT NULL,
                `Type` longtext CHARACTER SET utf8mb4 NOT NULL,
                `ProductId` int NOT NULL,
                CONSTRAINT `PK_ProductMedias` PRIMARY KEY (`Id`),
                CONSTRAINT `FK_ProductMedias_Products_ProductId`
                    FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE,
                INDEX `IX_ProductMedias_ProductId` (`ProductId`)
            ) CHARACTER SET=utf8mb4;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A baseline must not drop domain tables from an existing environment.
    }
}
