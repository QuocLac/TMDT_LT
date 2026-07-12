using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMDT_LT.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderInvoiceRequestFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderInvoiceRequests",
                columns: table => new
                {
                    InvoiceRequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    BuyerType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BuyerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TaxCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BuyerAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BuyerEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BuyerPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    AdminNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InvoiceLookupCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderInvoiceRequests", x => x.InvoiceRequestId);
                    table.ForeignKey(
                        name: "FK_OrderInvoiceRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInvoiceRequests_OrderId",
                table: "OrderInvoiceRequests",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderInvoiceRequests_Status_RequestedAt",
                table: "OrderInvoiceRequests",
                columns: new[] { "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInvoiceRequests_TaxCode",
                table: "OrderInvoiceRequests",
                column: "TaxCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderInvoiceRequests");
        }
    }
}
