using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMDT_LT.Migrations
{
    /// <inheritdoc />
    public partial class AddCommerceAnalyticsFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalyticsEvents",
                columns: table => new
                {
                    EventId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    VisitorKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    EventName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    VariantId = table.Column<int>(type: "int", nullable: true),
                    TargetProductId = table.Column<int>(type: "int", nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    SearchKeyword = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PagePath = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    Referrer = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    EventValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "AnalyticsSessions",
                columns: table => new
                {
                    AnalyticsSessionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    VisitorKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    LandingPage = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    Referrer = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    IpHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Medium = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Campaign = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    IsAuthenticated = table.Column<bool>(type: "bit", nullable: false),
                    PageViewCount = table.Column<int>(type: "int", nullable: false),
                    EventCount = table.Column<int>(type: "int", nullable: false),
                    HasAddToCart = table.Column<bool>(type: "bit", nullable: false),
                    HasCheckout = table.Column<bool>(type: "bit", nullable: false),
                    HasPurchase = table.Column<bool>(type: "bit", nullable: false),
                    TotalRevenue = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsSessions", x => x.AnalyticsSessionId);
                });

            migrationBuilder.CreateTable(
                name: "SearchQueryLogs",
                columns: table => new
                {
                    SearchQueryLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    VisitorKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    Keyword = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    NormalizedKeyword = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ResultCount = table.Column<int>(type: "int", nullable: false),
                    ClickedProductId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchQueryLogs", x => x.SearchQueryLogId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_CreatedAt",
                table: "AnalyticsEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_CustomerId",
                table: "AnalyticsEvents",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_EventName",
                table: "AnalyticsEvents",
                column: "EventName");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_OrderId",
                table: "AnalyticsEvents",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_ProductId",
                table: "AnalyticsEvents",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_SessionKey",
                table: "AnalyticsEvents",
                column: "SessionKey");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_VisitorKey",
                table: "AnalyticsEvents",
                column: "VisitorKey");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsSessions_CustomerId",
                table: "AnalyticsSessions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsSessions_SessionKey",
                table: "AnalyticsSessions",
                column: "SessionKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsSessions_VisitorKey",
                table: "AnalyticsSessions",
                column: "VisitorKey");

            migrationBuilder.CreateIndex(
                name: "IX_SearchQueryLogs_CreatedAt",
                table: "SearchQueryLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SearchQueryLogs_NormalizedKeyword",
                table: "SearchQueryLogs",
                column: "NormalizedKeyword");

            migrationBuilder.CreateIndex(
                name: "IX_SearchQueryLogs_SessionKey",
                table: "SearchQueryLogs",
                column: "SessionKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsEvents");

            migrationBuilder.DropTable(
                name: "AnalyticsSessions");

            migrationBuilder.DropTable(
                name: "SearchQueryLogs");
        }
    }
}
