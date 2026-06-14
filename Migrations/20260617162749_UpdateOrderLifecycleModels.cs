using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMDT_LT.Migrations
{
    /// <inheritdoc />
    public partial class UpdateOrderLifecycleModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RefundAccountName",
                table: "OrderReturns",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundAccountNumber",
                table: "OrderReturns",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundBankCode",
                table: "OrderReturns",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefundAccountName",
                table: "OrderReturns");

            migrationBuilder.DropColumn(
                name: "RefundAccountNumber",
                table: "OrderReturns");

            migrationBuilder.DropColumn(
                name: "RefundBankCode",
                table: "OrderReturns");
        }
    }
}
