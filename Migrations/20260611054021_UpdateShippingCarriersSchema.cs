using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMDT_LT.Migrations
{
    /// <inheritdoc />
    public partial class UpdateShippingCarriersSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApiToken",
                table: "ShippingCarriers");

            migrationBuilder.DropColumn(
                name: "ApiUrl",
                table: "ShippingCarriers");

            migrationBuilder.AddColumn<string>(
                name: "CarrierCode",
                table: "ShippingCarriers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "ShippingCarriers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "ShippingCarriers",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CarrierCode",
                table: "ShippingCarriers");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "ShippingCarriers");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "ShippingCarriers");

            migrationBuilder.AddColumn<string>(
                name: "ApiToken",
                table: "ShippingCarriers",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ApiUrl",
                table: "ShippingCarriers",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");
        }
    }
}
