using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMDT_LT.Migrations
{
    /// <inheritdoc />
    public partial class AddCrossell123 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BundleDiscountLabel",
                table: "CrossSellSettings",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "Ưu đãi mua kèm");

            migrationBuilder.AddColumn<int>(
                name: "BundleDiscountType",
                table: "CrossSellSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "BundleDiscountValue",
                table: "CrossSellSettings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsBundleDiscountEnabled",
                table: "CrossSellSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "CrossSellSettings",
                keyColumn: "CrossSellSettingsId",
                keyValue: 1,
                column: "BundleDiscountLabel",
                value: "Ưu đãi mua kèm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BundleDiscountLabel",
                table: "CrossSellSettings");

            migrationBuilder.DropColumn(
                name: "BundleDiscountType",
                table: "CrossSellSettings");

            migrationBuilder.DropColumn(
                name: "BundleDiscountValue",
                table: "CrossSellSettings");

            migrationBuilder.DropColumn(
                name: "IsBundleDiscountEnabled",
                table: "CrossSellSettings");
        }
    }
}
