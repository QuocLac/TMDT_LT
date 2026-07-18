using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMDT_LT.Migrations
{
    /// <inheritdoc />
    public partial class InventoryCountJournalPhaseD2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InventoryLotsLotId",
                table: "InventoryTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuantityAfter",
                table: "InventoryTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuantityBefore",
                table: "InventoryTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                table: "InventoryTransactions",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceType",
                table: "InventoryTransactions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValueImpact",
                table: "InventoryTransactions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SupplierId",
                table: "InventoryLots",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "Poid",
                table: "InventoryLots",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "InventoryCountLineId",
                table: "InventoryLots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "InventoryLots",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "InventoryLots",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryCountSessions",
                columns: table => new
                {
                    CountSessionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ScopeType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAccountId = table.Column<int>(type: "int", nullable: true),
                    SubmittedByAccountId = table.Column<int>(type: "int", nullable: true),
                    PostedByAccountId = table.Column<int>(type: "int", nullable: true),
                    CancelledByAccountId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCountSessions", x => x.CountSessionId);
                    table.ForeignKey(
                        name: "FK_InventoryCountSessions_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryCountLines",
                columns: table => new
                {
                    CountLineId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountSessionId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    SystemQuantity = table.Column<int>(type: "int", nullable: false),
                    CountedQuantity = table.Column<int>(type: "int", nullable: true),
                    Difference = table.Column<int>(type: "int", nullable: false),
                    UnitCostSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdjustmentUnitCost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    VarianceValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CountedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CountedByAccountId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCountLines", x => x.CountLineId);
                    table.ForeignKey(
                        name: "FK_InventoryCountLines_InventoryCountSessions_CountSessionId",
                        column: x => x.CountSessionId,
                        principalTable: "InventoryCountSessions",
                        principalColumn: "CountSessionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryCountLines_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "VariantId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_InventoryLotsLotId",
                table: "InventoryTransactions",
                column: "InventoryLotsLotId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLots_InventoryCountLineId",
                table: "InventoryLots",
                column: "InventoryCountLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountLines_CountSessionId_VariantId",
                table: "InventoryCountLines",
                columns: new[] { "CountSessionId", "VariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountLines_VariantId_CountedAt",
                table: "InventoryCountLines",
                columns: new[] { "VariantId", "CountedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountSessions_CountCode",
                table: "InventoryCountSessions",
                column: "CountCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountSessions_WarehouseId_Status_CreatedAt",
                table: "InventoryCountSessions",
                columns: new[] { "WarehouseId", "Status", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryLots_InventoryCountLines_InventoryCountLineId",
                table: "InventoryLots",
                column: "InventoryCountLineId",
                principalTable: "InventoryCountLines",
                principalColumn: "CountLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_InventoryLots_InventoryLotsLotId",
                table: "InventoryTransactions",
                column: "InventoryLotsLotId",
                principalTable: "InventoryLots",
                principalColumn: "LotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryLots_InventoryCountLines_InventoryCountLineId",
                table: "InventoryLots");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_InventoryLots_InventoryLotsLotId",
                table: "InventoryTransactions");

            migrationBuilder.DropTable(
                name: "InventoryCountLines");

            migrationBuilder.DropTable(
                name: "InventoryCountSessions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_InventoryLotsLotId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryLots_InventoryCountLineId",
                table: "InventoryLots");

            migrationBuilder.DropColumn(
                name: "InventoryLotsLotId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "QuantityAfter",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "QuantityBefore",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "ReasonCode",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "ReferenceType",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "ValueImpact",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "InventoryCountLineId",
                table: "InventoryLots");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "InventoryLots");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "InventoryLots");

            migrationBuilder.AlterColumn<int>(
                name: "SupplierId",
                table: "InventoryLots",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Poid",
                table: "InventoryLots",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
