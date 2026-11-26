using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Local133.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdminDashboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WebhookEventId",
                table: "AuditLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_ReceivedAt",
                table: "WebhookEvents",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_PaidAt",
                table: "SubscriptionPayments",
                column: "PaidAt");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionCharges_Status_PaidAt",
                table: "SubscriptionCharges",
                columns: new[] { "Status", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CreatedAt",
                table: "Sales",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaidAt",
                table: "Payments",
                column: "PaidAt")
                .Annotation("SqlServer:Include", new[] { "Amount", "SaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_WebhookEventId",
                table: "AuditLogs",
                column: "WebhookEventId",
                filter: "[WebhookEventId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookEvents_ReceivedAt",
                table: "WebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPayments_PaidAt",
                table: "SubscriptionPayments");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionCharges_Status_PaidAt",
                table: "SubscriptionCharges");

            migrationBuilder.DropIndex(
                name: "IX_Sales_CreatedAt",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Payments_PaidAt",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_WebhookEventId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "WebhookEventId",
                table: "AuditLogs");
        }
    }
}
