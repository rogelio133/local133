using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Local133.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// 5.14: se retira el plan gratuito. Borra sus parámetros (cobros concretados y generados al mes, días de historial)
    /// y rehace el check de los límites. El <c>Down</c> los vuelve a crear con sus valores de antes (100, 300 y 30).
    /// </summary>
    public partial class RemoveFreePlanSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemSettings_Limits",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "FreeHistoryDays",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "FreeMonthlyGeneratedLimit",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "FreeMonthlyPaidLimit",
                table: "SystemSettings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemSettings_Limits",
                table: "SystemSettings",
                sql: "[FounderSlots] >= 0 AND [FairUseMonthlyPaid] >= 0 AND [ReferralRewardDays] >= 0 AND [ReferralRewardsPerYear] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemSettings_Limits",
                table: "SystemSettings");

            migrationBuilder.AddColumn<int>(
                name: "FreeHistoryDays",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FreeMonthlyGeneratedLimit",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FreeMonthlyPaidLimit",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "FreeHistoryDays", "FreeMonthlyGeneratedLimit", "FreeMonthlyPaidLimit" },
                values: new object[] { 30, 300, 100 });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemSettings_Limits",
                table: "SystemSettings",
                sql: "[FounderSlots] >= 0 AND [FreeMonthlyPaidLimit] >= 0 AND [FreeMonthlyGeneratedLimit] >= 0 AND [FreeHistoryDays] >= 0 AND [FairUseMonthlyPaid] >= 0 AND [ReferralRewardDays] >= 0 AND [ReferralRewardsPerYear] >= 0");
        }
    }
}
