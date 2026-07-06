using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMDT_LT.Migrations
{
    /// <inheritdoc />
    public partial class Crossel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrossSellSettings",
                columns: table => new
                {
                    CrossSellSettingsId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    MinSupportCount = table.Column<int>(type: "int", nullable: false, defaultValue: 2),
                    MinSupportPercent = table.Column<decimal>(type: "decimal(5,4)", nullable: false, defaultValue: 0m),
                    MinConfidence = table.Column<decimal>(type: "decimal(5,4)", nullable: false, defaultValue: 0.2m),
                    MinLift = table.Column<decimal>(type: "decimal(8,4)", nullable: false, defaultValue: 1m),
                    MaxItemsetSize = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                    MaxRecommendationsPerProduct = table.Column<int>(type: "int", nullable: false, defaultValue: 6),
                    AnalysisWindowDays = table.Column<int>(type: "int", nullable: false, defaultValue: 365),
                    OnlyCompletedOrders = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    AllowedOrderStatuses = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: "Đã hoàn thành,Hoàn thành,Đã giao,Completed"),
                    ExcludeOutOfStock = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    AllowFallbackWhenNoRule = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedByAccountId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrossSellSettings", x => x.CrossSellSettingsId);
                });

            migrationBuilder.InsertData(
                table: "CrossSellSettings",
                columns: new[] { "CrossSellSettingsId", "AllowedOrderStatuses", "AnalysisWindowDays", "ExcludeOutOfStock", "IsEnabled", "MaxItemsetSize", "MaxRecommendationsPerProduct", "MinConfidence", "MinLift", "MinSupportCount", "OnlyCompletedOrders", "UpdatedAt", "UpdatedByAccountId" },
                values: new object[] { 1, "Đã hoàn thành,Hoàn thành,Đã giao,Completed", 365, true, true, 3, 6, 0.2m, 1m, 2, true, new DateTime(2026, 7, 6, 0, 0, 0, 0, DateTimeKind.Unspecified), null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrossSellSettings");
        }
    }
}
