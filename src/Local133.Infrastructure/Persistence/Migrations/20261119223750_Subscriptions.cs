using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Local133.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Subscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AnnualPrice",
                table: "SystemSettings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FounderAnnualPrice",
                table: "SystemSettings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FounderMonthlyPrice",
                table: "SystemSettings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "FounderSlots",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FreeCourtesyCharges",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

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

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyPrice",
                table: "SystemSettings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ProFairUseMonthlyPaid",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ReferenceTerminalFeeRate",
                table: "SystemSettings",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ReferralRewardDays",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReferralRewardsPerYear",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ReferralWelcomeDiscountRate",
                table: "SystemSettings",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TrialDays",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MerchantId = table.Column<int>(type: "int", nullable: false),
                    TrialStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastPeriodKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LastPackage = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsFounder = table.Column<bool>(type: "bit", nullable: false),
                    FounderNumber = table.Column<int>(type: "int", nullable: true),
                    LockedAnnualPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.UniqueConstraint("AK_Subscriptions_MerchantId", x => x.MerchantId);
                    table.CheckConstraint("CK_Subscriptions_FounderNumber", "[FounderNumber] IS NULL OR [FounderNumber] > 0");
                    table.CheckConstraint("CK_Subscriptions_LockedAnnualPrice", "[LockedAnnualPrice] IS NULL OR [LockedAnnualPrice] > 0");
                    table.CheckConstraint("CK_Subscriptions_ProUntil", "[ProUntil] > [TrialStartedAt]");
                    table.ForeignKey(
                        name: "FK_Subscriptions_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MerchantId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPeriods", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionPeriods_Range", "[EndsAt] > [StartsAt]");
                    table.ForeignKey(
                        name: "FK_SubscriptionPeriods_Subscriptions_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Subscriptions",
                        principalColumn: "MerchantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AnnualPrice", "FounderAnnualPrice", "FounderMonthlyPrice", "FounderSlots", "FreeCourtesyCharges", "FreeHistoryDays", "FreeMonthlyGeneratedLimit", "FreeMonthlyPaidLimit", "MonthlyPrice", "ProFairUseMonthlyPaid", "ReferenceTerminalFeeRate", "ReferralRewardDays", "ReferralRewardsPerYear", "ReferralWelcomeDiscountRate", "TrialDays" },
                values: new object[] { 1990m, 1490m, 149m, 100, 5, 30, 300, 100, 199m, 5000, 0.035m, 10, 12, 0.30m, 30 });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemSettings_Limits",
                table: "SystemSettings",
                sql: "[FounderSlots] >= 0 AND [FreeMonthlyPaidLimit] >= 0 AND [FreeCourtesyCharges] >= 0 AND [FreeMonthlyGeneratedLimit] >= 0 AND [FreeHistoryDays] >= 0 AND [ProFairUseMonthlyPaid] >= 0 AND [ReferralRewardDays] >= 0 AND [ReferralRewardsPerYear] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemSettings_Prices",
                table: "SystemSettings",
                sql: "[MonthlyPrice] > 0 AND [AnnualPrice] > 0 AND [FounderMonthlyPrice] > 0 AND [FounderAnnualPrice] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemSettings_Rates",
                table: "SystemSettings",
                sql: "[ReferralWelcomeDiscountRate] >= 0 AND [ReferralWelcomeDiscountRate] < 1 AND [ReferenceTerminalFeeRate] >= 0 AND [ReferenceTerminalFeeRate] < 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemSettings_TrialDays",
                table: "SystemSettings",
                sql: "[TrialDays] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_MerchantId_EndsAt",
                table: "SubscriptionPeriods",
                columns: new[] { "MerchantId", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_FounderNumber",
                table: "Subscriptions",
                column: "FounderNumber",
                unique: true,
                filter: "[FounderNumber] IS NOT NULL");

            // Prueba para los vendedores que ya pasaron el onboarding (§3 del diseño de suscripciones): desde el día
            // de la migración y por TrialDays. Los demás la reciben al activarse su cuenta. Se incluye a suspendidos y
            // dados de baja porque ApplyBankAccountStatus ya no los vuelve a activar. México no tiene horario de
            // verano, así que sumar días en UTC da lo mismo que MexicoTime.AddDays.
            migrationBuilder.Sql("""
                DECLARE @now datetimeoffset = TODATETIMEOFFSET(SYSUTCDATETIME(), 0);
                DECLARE @trialDays int = (SELECT [TrialDays] FROM [SystemSettings] WHERE [Id] = 1);

                INSERT INTO [Subscriptions] ([MerchantId], [TrialStartedAt], [ProUntil], [LastPeriodKind], [IsFounder])
                SELECT m.[Id], @now, DATEADD(day, @trialDays, @now), N'Trial', 0
                FROM [Merchants] m
                WHERE m.[Status] IN (N'Active', N'Suspended', N'Disabled')
                  AND NOT EXISTS (SELECT 1 FROM [Subscriptions] s WHERE s.[MerchantId] = m.[Id]);

                INSERT INTO [SubscriptionPeriods] ([MerchantId], [Kind], [StartsAt], [EndsAt], [CreatedAt])
                SELECT s.[MerchantId], N'Trial', s.[TrialStartedAt], s.[ProUntil], @now
                FROM [Subscriptions] s
                WHERE NOT EXISTS (SELECT 1 FROM [SubscriptionPeriods] p WHERE p.[MerchantId] = s.[MerchantId]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionPeriods");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemSettings_Limits",
                table: "SystemSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemSettings_Prices",
                table: "SystemSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemSettings_Rates",
                table: "SystemSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemSettings_TrialDays",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "AnnualPrice",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "FounderAnnualPrice",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "FounderMonthlyPrice",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "FounderSlots",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "FreeCourtesyCharges",
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

            migrationBuilder.DropColumn(
                name: "MonthlyPrice",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ProFairUseMonthlyPaid",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ReferenceTerminalFeeRate",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ReferralRewardDays",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ReferralRewardsPerYear",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ReferralWelcomeDiscountRate",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "TrialDays",
                table: "SystemSettings");
        }
    }
}
