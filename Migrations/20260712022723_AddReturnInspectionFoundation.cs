using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMDT_LT.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnInspectionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReturnInspections",
                columns: table => new
                {
                    InspectionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OriginalOrderAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EligibleRefundAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SummaryNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InspectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InspectedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnInspections", x => x.InspectionId);
                    table.ForeignKey(
                        name: "FK_ReturnInspections_OrderReturns_ReturnId",
                        column: x => x.ReturnId,
                        principalTable: "OrderReturns",
                        principalColumn: "ReturnId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReturnInspections_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "OrderId");
                });

            migrationBuilder.CreateTable(
                name: "ReturnInspectionItems",
                columns: table => new
                {
                    InspectionItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InspectionId = table.Column<int>(type: "int", nullable: false),
                    OrderDetailId = table.Column<int>(type: "int", nullable: false),
                    ExpectedQuantity = table.Column<int>(type: "int", nullable: false),
                    ReceivedQuantity = table.Column<int>(type: "int", nullable: false),
                    ApprovedQuantity = table.Column<int>(type: "int", nullable: false),
                    ConditionCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Disposition = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UnitAmountSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EligibleAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnInspectionItems", x => x.InspectionItemId);
                    table.ForeignKey(
                        name: "FK_ReturnInspectionItems_OrderDetails_OrderDetailId",
                        column: x => x.OrderDetailId,
                        principalTable: "OrderDetails",
                        principalColumn: "OrderDetailId");
                    table.ForeignKey(
                        name: "FK_ReturnInspectionItems_ReturnInspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "ReturnInspections",
                        principalColumn: "InspectionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspectionItems_InspectionId_OrderDetailId",
                table: "ReturnInspectionItems",
                columns: new[] { "InspectionId", "OrderDetailId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspectionItems_OrderDetailId",
                table: "ReturnInspectionItems",
                column: "OrderDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspections_OrderId",
                table: "ReturnInspections",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspections_ReturnId",
                table: "ReturnInspections",
                column: "ReturnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspections_Status_UpdatedAt",
                table: "ReturnInspections",
                columns: new[] { "Status", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReturnInspectionItems");

            migrationBuilder.DropTable(
                name: "ReturnInspections");
        }
    }
}
