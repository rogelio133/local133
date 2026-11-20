using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Local133.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFreeCourtesyCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemSettings_Limits",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "FreeCourtesyCharges",
                table: "SystemSettings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemSettings_Limits",
                table: "SystemSettings",
                sql: "[FounderSlots] >= 0 AND [FreeMonthlyPaidLimit] >= 0 AND [FreeMonthlyGeneratedLimit] >= 0 AND [FreeHistoryDays] >= 0 AND [ProFairUseMonthlyPaid] >= 0 AND [ReferralRewardDays] >= 0 AND [ReferralRewardsPerYear] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemSettings_Limits",
                table: "SystemSettings");

            migrationBuilder.AddColumn<int>(
                name: "FreeCourtesyCharges",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "FreeCourtesyCharges",
                value: 5);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemSettings_Limits",
                table: "SystemSettings",
                sql: "[FounderSlots] >= 0 AND [FreeMonthlyPaidLimit] >= 0 AND [FreeCourtesyCharges] >= 0 AND [FreeMonthlyGeneratedLimit] >= 0 AND [FreeHistoryDays] >= 0 AND [ProFairUseMonthlyPaid] >= 0 AND [ReferralRewardDays] >= 0 AND [ReferralRewardsPerYear] >= 0");
        }
    }
}
