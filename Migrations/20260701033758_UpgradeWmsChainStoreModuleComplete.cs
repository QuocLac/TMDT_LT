using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMDT_LT.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeWmsChainStoreModuleComplete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BẮT ĐẦU THẲNG TỪ LỆNH TẠO BẢNG STORES (ĐÃ XÓA SẠCH CÁC LỆNH DROP VÀ RENAME LỖI PHÍA TRÊN)
            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    StoreId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StoreType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TaxCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContactPerson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContractNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContractDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ContractExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.StoreId);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrders",
                columns: table => new
                {
                    SOId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SOCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    FromWarehouseId = table.Column<int>(type: "int", nullable: false),
                    OrderDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ShippingCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    COGSTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ProfitTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrders", x => x.SOId);
                    table.ForeignKey(
                        name: "FK_SalesOrders_ACCOUNT_AccountId",
                        column: x => x.AccountId,
                        principalTable: "ACCOUNT",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderDetails",
                columns: table => new
                {
                    SODetailId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SOId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    LotId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Profit = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderDetails", x => x.SODetailId);
                    table.ForeignKey(
                        name: "FK_SalesOrderDetails_InventoryLots_LotId",
                        column: x => x.LotId,
                        principalTable: "InventoryLots",
                        principalColumn: "LotId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderDetails_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "VariantId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderDetails_SalesOrders_SOId",
                        column: x => x.SOId,
                        principalTable: "SalesOrders",
                        principalColumn: "SOId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Stores",
                columns: new[] { "StoreId", "Address", "ContactPerson", "ContractDate", "ContractExpiry", "ContractNumber", "CreatedAt", "Email", "IsActive", "Notes", "Phone", "StoreCode", "StoreName", "StoreType", "TaxCode" },
                values: new object[,]
                {
            { 1, "Quận 1, TP. Hồ Chí Minh", "Nguyễn Văn Trưởng Chi Nhánh", null, null, null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "hcm@kingphone.vn", true, null, "0283999111", "ST001", "KingPhone Store HCM - Quận 1", "Direct", "0304050607" },
            { 2, "Quận Hoàn Kiếm, Hà Nội", "Trần Thị Quản Lý", null, null, null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "hn@kingphone.vn", true, null, "0243999222", "ST002", "KingPhone Store HN - Hoàn Kiếm", "Direct", "0304050608" },
            { 3, "Quận Bình Thạnh, TP. HCM", "Lê Văn Đối Tác", null, null, null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "binhthanh@gmail.com", true, null, "0903888999", "AG001", "Đại lý Ủy quyền Bình Thạnh", "Agent", "0304050609" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderDetails_LotId",
                table: "SalesOrderDetails",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderDetails_SOId",
                table: "SalesOrderDetails",
                column: "SOId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderDetails_VariantId",
                table: "SalesOrderDetails",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_AccountId",
                table: "SalesOrders",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_StoreId",
                table: "SalesOrders",
                column: "StoreId");

            // ĐÃ XÓA TOÀN BỘ CÁC LỆNH MIGRATIONBUILDER.ADDFOREIGNKEY BỊ THỪA Ở CUỐI HÀM UP
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
