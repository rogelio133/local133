using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Local133.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubscriptionChargeId",
                table: "SubscriptionPeriods",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionCharges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MerchantId = table.Column<int>(type: "int", nullable: false),
                    Package = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Months = table.Column<int>(type: "int", nullable: false),
                    ListPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountReason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsFounderPrice = table.Column<bool>(type: "bit", nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PeyPaymentLinkId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaymentCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionCharges", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionCharges_Amount", "[Amount] > 0 AND [Amount] = [ListPrice] - [DiscountAmount]");
                    table.CheckConstraint("CK_SubscriptionCharges_Months", "[Months] > 0");
                    table.ForeignKey(
                        name: "FK_SubscriptionCharges_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MerchantId = table.Column<int>(type: "int", nullable: false),
                    SubscriptionChargeId = table.Column<int>(type: "int", nullable: false),
                    PeyTransactionId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PaymentMethod = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Base = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ApplicationFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPayments_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPayments_SubscriptionCharges_SubscriptionChargeId",
                        column: x => x.SubscriptionChargeId,
                        principalTable: "SubscriptionCharges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_SubscriptionChargeId",
                table: "SubscriptionPeriods",
                column: "SubscriptionChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionCharges_MerchantId_CreatedAt",
                table: "SubscriptionCharges",
                columns: new[] { "MerchantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionCharges_PeyPaymentLinkId",
                table: "SubscriptionCharges",
                column: "PeyPaymentLinkId",
                unique: true,
                filter: "[PeyPaymentLinkId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionCharges_Status_CreatedAt",
                table: "SubscriptionCharges",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_MerchantId",
                table: "SubscriptionPayments",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_PeyTransactionId",
                table: "SubscriptionPayments",
                column: "PeyTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_SubscriptionChargeId",
                table: "SubscriptionPayments",
                column: "SubscriptionChargeId");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionPeriods_SubscriptionCharges_SubscriptionChargeId",
                table: "SubscriptionPeriods",
                column: "SubscriptionChargeId",
                principalTable: "SubscriptionCharges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionPeriods_SubscriptionCharges_SubscriptionChargeId",
                table: "SubscriptionPeriods");

            migrationBuilder.DropTable(
                name: "SubscriptionPayments");

            migrationBuilder.DropTable(
                name: "SubscriptionCharges");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPeriods_SubscriptionChargeId",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "SubscriptionChargeId",
                table: "SubscriptionPeriods");
        }
    }
}
