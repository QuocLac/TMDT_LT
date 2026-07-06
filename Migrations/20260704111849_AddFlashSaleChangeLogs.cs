using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMDT_LT.Migrations
{
    /// <inheritdoc />
    public partial class AddFlashSaleChangeLogs : Migration
    {
        /// <inheritdoc />
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

            migrationBuilder.AddColumn<int>(
                name: "FlashSaleItemId",
                table: "OrderDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFlashSaleItem",
                table: "OrderDetails",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FlashSaleChangeLogs",
                columns: table => new
                {
                    ChangeLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FlashSaleId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    AdminAccountId = table.Column<int>(type: "int", nullable: true),
                    ActionType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlashSaleChangeLogs", x => x.ChangeLogId);
                    table.ForeignKey(
                        name: "FK_FlashSaleChangeLogs_ACCOUNT_AdminAccountId",
                        column: x => x.AdminAccountId,
                        principalTable: "ACCOUNT",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FlashSaleChangeLogs_FlashSaleItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "FlashSaleItems",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FlashSaleChangeLogs_FlashSales_FlashSaleId",
                        column: x => x.FlashSaleId,
                        principalTable: "FlashSales",
                        principalColumn: "FlashSaleId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderDetails_FlashSaleItemId",
                table: "OrderDetails",
                column: "FlashSaleItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashSaleChangeLogs_AdminAccountId",
                table: "FlashSaleChangeLogs",
                column: "AdminAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashSaleChangeLogs_FlashSaleId",
                table: "FlashSaleChangeLogs",
                column: "FlashSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashSaleChangeLogs_ItemId",
                table: "FlashSaleChangeLogs",
                column: "ItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderDetails_FlashSaleItems_FlashSaleItemId",
                table: "OrderDetails",
                column: "FlashSaleItemId",
                principalTable: "FlashSaleItems",
                principalColumn: "ItemId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderDetails_FlashSaleItems_FlashSaleItemId",
                table: "OrderDetails");

            migrationBuilder.DropTable(
                name: "FlashSaleChangeLogs");

            migrationBuilder.DropIndex(
                name: "IX_OrderDetails_FlashSaleItemId",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "IsStockDeducted",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StockDeductedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FlashSaleItemId",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "IsFlashSaleItem",
                table: "OrderDetails");
        }
    }
}
