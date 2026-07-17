using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TMDT_LT.Migrations
{
    /// <inheritdoc />
    public partial class InventoryWarehouseOrderFifo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "GoodsSubtotal",
                table: "PurchaseOrders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InboundShippingFee",
                table: "PurchaseOrders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InputVatAmount",
                table: "PurchaseOrders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "InputVatDeductible",
                table: "PurchaseOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "InventoryCapitalizedCost",
                table: "PurchaseOrders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvoiceDate",
                table: "PurchaseOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "PurchaseOrders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtherCost",
                table: "PurchaseOrders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "PurchaseOrders",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "AllocatedInboundCost",
                table: "PurchaseOrderDetails",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CapitalizedLineCost",
                table: "PurchaseOrderDetails",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InputVatAmount",
                table: "PurchaseOrderDetails",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LandedUnitCost",
                table: "PurchaseOrderDetails",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "PurchaseOrderDetails",
                type: "decimal(9,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CogsAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "CostCalculatedAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FulfillmentWarehouseId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossProfitAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MerchandiseNetRevenueAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CogsAmount",
                table: "OrderDetails",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "CostCalculatedAt",
                table: "OrderDetails",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossProfitAmount",
                table: "OrderDetails",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetRevenueAmount",
                table: "OrderDetails",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "LotId",
                table: "InventoryTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCostSnapshot",
                table: "InventoryTransactions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostSnapshot",
                table: "InventoryTransactions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "InventoryTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "InventoryLots",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    WarehouseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WarehouseName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.WarehouseId);
                });

            migrationBuilder.CreateTable(
                name: "OrderInventoryAllocations",
                columns: table => new
                {
                    AllocationId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    OrderDetailId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    LotId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AllocatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RestoredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RestoredLotId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderInventoryAllocations", x => x.AllocationId);
                    table.ForeignKey(
                        name: "FK_OrderInventoryAllocations_InventoryLots_LotId",
                        column: x => x.LotId,
                        principalTable: "InventoryLots",
                        principalColumn: "LotId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryAllocations_OrderDetails_OrderDetailId",
                        column: x => x.OrderDetailId,
                        principalTable: "OrderDetails",
                        principalColumn: "OrderDetailId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryAllocations_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryAllocations_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "VariantId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryAllocations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Warehouses",
                columns: new[] { "WarehouseId", "Address", "CreatedAt", "IsActive", "IsPrimary", "WarehouseCode", "WarehouseName" },
                values: new object[,]
                {
                    { 1, "Quận 1, TP. Hồ Chí Minh", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), true, true, "WH-HCM", "Kho HCM - Cơ sở chính Quận 1" },
                    { 2, "Quận Hoàn Kiếm, Hà Nội", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), true, false, "WH-HN", "Kho HN - Cơ sở chính Hoàn Kiếm" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_FromWarehouseId_OrderDate",
                table: "SalesOrders",
                columns: new[] { "FromWarehouseId", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_WarehouseId",
                table: "PurchaseOrders",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_FulfillmentWarehouseId",
                table: "Orders",
                column: "FulfillmentWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_LotId",
                table: "InventoryTransactions",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_WarehouseId_TransactionDate",
                table: "InventoryTransactions",
                columns: new[] { "WarehouseId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLots_WarehouseId_VariantId_ReceivedDate_LotId",
                table: "InventoryLots",
                columns: new[] { "WarehouseId", "VariantId", "ReceivedDate", "LotId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryAllocations_LotId",
                table: "OrderInventoryAllocations",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryAllocations_OrderDetailId_LotId",
                table: "OrderInventoryAllocations",
                columns: new[] { "OrderDetailId", "LotId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryAllocations_OrderId_Status",
                table: "OrderInventoryAllocations",
                columns: new[] { "OrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryAllocations_VariantId",
                table: "OrderInventoryAllocations",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryAllocations_WarehouseId_VariantId_AllocatedAt",
                table: "OrderInventoryAllocations",
                columns: new[] { "WarehouseId", "VariantId", "AllocatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_WarehouseCode",
                table: "Warehouses",
                column: "WarehouseCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryLots_Warehouses_WarehouseId",
                table: "InventoryLots",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "WarehouseId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_InventoryLots_LotId",
                table: "InventoryTransactions",
                column: "LotId",
                principalTable: "InventoryLots",
                principalColumn: "LotId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_Warehouses_WarehouseId",
                table: "InventoryTransactions",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "WarehouseId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Warehouses_FulfillmentWarehouseId",
                table: "Orders",
                column: "FulfillmentWarehouseId",
                principalTable: "Warehouses",
                principalColumn: "WarehouseId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Warehouses_WarehouseId",
                table: "PurchaseOrders",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "WarehouseId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_Warehouses_FromWarehouseId",
                table: "SalesOrders",
                column: "FromWarehouseId",
                principalTable: "Warehouses",
                principalColumn: "WarehouseId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryLots_Warehouses_WarehouseId",
                table: "InventoryLots");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_InventoryLots_LotId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_Warehouses_WarehouseId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Warehouses_FulfillmentWarehouseId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Warehouses_WarehouseId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_Warehouses_FromWarehouseId",
                table: "SalesOrders");

            migrationBuilder.DropTable(
                name: "OrderInventoryAllocations");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_FromWarehouseId_OrderDate",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_WarehouseId",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_FulfillmentWarehouseId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_LotId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_WarehouseId_TransactionDate",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryLots_WarehouseId_VariantId_ReceivedDate_LotId",
                table: "InventoryLots");

            migrationBuilder.DropColumn(
                name: "GoodsSubtotal",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "InboundShippingFee",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "InputVatAmount",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "InputVatDeductible",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "InventoryCapitalizedCost",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "InvoiceDate",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "OtherCost",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "AllocatedInboundCost",
                table: "PurchaseOrderDetails");

            migrationBuilder.DropColumn(
                name: "CapitalizedLineCost",
                table: "PurchaseOrderDetails");

            migrationBuilder.DropColumn(
                name: "InputVatAmount",
                table: "PurchaseOrderDetails");

            migrationBuilder.DropColumn(
                name: "LandedUnitCost",
                table: "PurchaseOrderDetails");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "PurchaseOrderDetails");

            migrationBuilder.DropColumn(
                name: "CogsAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CostCalculatedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfillmentWarehouseId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "GrossProfitAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "MerchandiseNetRevenueAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CogsAmount",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "CostCalculatedAt",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "GrossProfitAmount",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "NetRevenueAmount",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "LotId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "TotalCostSnapshot",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "UnitCostSnapshot",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "InventoryLots");
        }
    }
}
