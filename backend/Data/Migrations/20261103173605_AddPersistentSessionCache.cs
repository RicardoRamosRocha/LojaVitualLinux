using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LojaVirtual.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistentSessionCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DistributedCacheEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<byte[]>(type: "longblob", nullable: false),
                    AbsoluteExpiration = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    SlidingExpiration = table.Column<TimeSpan>(type: "time(6)", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DistributedCacheEntries", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DistributedCacheEntries");
        }
    }
}
