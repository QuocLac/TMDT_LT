using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMDT_LT.Migrations
{
    /// <inheritdoc />
    public partial class InventoryCountJournalShadowRelationCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_InventoryLots_InventoryLotsLotId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_InventoryLotsLotId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "InventoryLotsLotId",
                table: "InventoryTransactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InventoryLotsLotId",
                table: "InventoryTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_InventoryLotsLotId",
                table: "InventoryTransactions",
                column: "InventoryLotsLotId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_InventoryLots_InventoryLotsLotId",
                table: "InventoryTransactions",
                column: "InventoryLotsLotId",
                principalTable: "InventoryLots",
                principalColumn: "LotId");
        }
    }
}
