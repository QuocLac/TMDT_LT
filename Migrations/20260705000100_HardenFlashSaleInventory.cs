using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMDT_LT.Migrations
{
    public partial class HardenFlashSaleInventory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsStockDeducted",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "StockDeductedAt",
                table: "Orders",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFlashSaleItem",
                table: "OrderDetails",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FlashSaleItemId",
                table: "OrderDetails",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderDetails_FlashSaleItemId",
                table: "OrderDetails",
                column: "FlashSaleItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderDetails_FlashSaleItems_FlashSaleItemId",
                table: "OrderDetails",
                column: "FlashSaleItemId",
                principalTable: "FlashSaleItems",
                principalColumn: "ItemId",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderDetails_FlashSaleItems_FlashSaleItemId",
                table: "OrderDetails");

            migrationBuilder.DropIndex(
                name: "IX_OrderDetails_FlashSaleItemId",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "FlashSaleItemId",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "IsFlashSaleItem",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "StockDeductedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsStockDeducted",
                table: "Orders");
        }
    }
}
