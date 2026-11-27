using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Local133.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BasicAndProPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemSettings_Limits",
                table: "SystemSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemSettings_Prices",
                table: "SystemSettings");

            migrationBuilder.RenameColumn(
                name: "ProFairUseMonthlyPaid",
                table: "SystemSettings",
                newName: "FairUseMonthlyPaid");

            migrationBuilder.RenameColumn(
                name: "MonthlyPrice",
                table: "SystemSettings",
                newName: "ProMonthlyPrice");

            migrationBuilder.RenameColumn(
                name: "FounderMonthlyPrice",
                table: "SystemSettings",
                newName: "ProFounderMonthlyPrice");

            migrationBuilder.RenameColumn(
                name: "FounderAnnualPrice",
                table: "SystemSettings",
                newName: "ProFounderAnnualPrice");

            migrationBuilder.RenameColumn(
                name: "AnnualPrice",
                table: "SystemSettings",
                newName: "ProAnnualPrice");

            migrationBuilder.AddColumn<decimal>(
                name: "BasicAnnualPrice",
                table: "SystemSettings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 990m);

            migrationBuilder.AddColumn<decimal>(
                name: "BasicFounderAnnualPrice",
                table: "SystemSettings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 690m);

            migrationBuilder.AddColumn<decimal>(
                name: "BasicFounderMonthlyPrice",
                table: "SystemSettings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 69m);

            migrationBuilder.AddColumn<decimal>(
                name: "BasicMonthlyPrice",
                table: "SystemSettings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 99m);

            migrationBuilder.AddColumn<string>(
                name: "LastPlan",
                table: "Subscriptions",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockedAnnualPlan",
                table: "Subscriptions",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "SubscriptionPeriods",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Pro");

            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "SubscriptionCharges",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Pro");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TermsAcceptedAt",
                table: "SubscriptionCharges",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsVersion",
                table: "SubscriptionCharges",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "BasicAnnualPrice", "BasicFounderAnnualPrice", "BasicFounderMonthlyPrice", "BasicMonthlyPrice", "TrialDays" },
                values: new object[] { 990m, 690m, 69m, 99m, 15 });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemSettings_Limits",
                table: "SystemSettings",
                sql: "[FounderSlots] >= 0 AND [FreeMonthlyPaidLimit] >= 0 AND [FreeMonthlyGeneratedLimit] >= 0 AND [FreeHistoryDays] >= 0 AND [FairUseMonthlyPaid] >= 0 AND [ReferralRewardDays] >= 0 AND [ReferralRewardsPerYear] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemSettings_Prices",
                table: "SystemSettings",
                sql: "[BasicMonthlyPrice] > 0 AND [BasicAnnualPrice] > 0 AND [BasicFounderMonthlyPrice] > 0 AND [BasicFounderAnnualPrice] > 0 AND [ProMonthlyPrice] > 0 AND [ProAnnualPrice] > 0 AND [ProFounderMonthlyPrice] > 0 AND [ProFounderAnnualPrice] > 0");

            // Lo pagado hasta hoy es de Pro (los periodos y los cargos ya quedaron en Pro por el valor por defecto).
            migrationBuilder.Sql("""
                UPDATE [Subscriptions] SET [LastPlan] = N'Pro' WHERE [LastPackage] IS NOT NULL;
                UPDATE [Subscriptions] SET [LockedAnnualPlan] = N'Pro' WHERE [LockedAnnualPrice] IS NOT NULL;
                """);

            // Transición de los vendedores existentes (§10 de actualizaciones/planes_basico_pro_2026_11_26.md), con la
            // misma regla que CustomerListQuery.WithPlan. Los periodos van encadenados, así que el instante está
            // cubierto si y solo si es anterior a ProUntil:
            // - gratuito (ProUntil ya pasó y no está en gracia): 15 días de Básico desde ahora;
            // - en prueba (solo la prueba lo cubre y no hay un pago por delante): 15 días de Básico al terminar su cadena;
            // - en gracia: Básico desde que venció lo pagado hasta 15 días después de ahora;
            // - Pro pagado, premio o regalo vigente, y sin suscripción: nada.
            // México no tiene horario de verano, así que sumar días en UTC da lo mismo que MexicoTime.AddDays.
            migrationBuilder.Sql("""
                DECLARE @now datetimeoffset = TODATETIMEOFFSET(SYSUTCDATETIME(), 0);
                DECLARE @days int = 15;
                DECLARE @graceDays int = (SELECT [GraceDays] FROM [SystemSettings] WHERE [Id] = 1);
                DECLARE @transition TABLE (
                    [MerchantId] int NOT NULL PRIMARY KEY,
                    [State] nvarchar(10) NOT NULL,
                    [StartsAt] datetimeoffset NOT NULL,
                    [EndsAt] datetimeoffset NOT NULL);

                INSERT INTO @transition ([MerchantId], [State], [StartsAt], [EndsAt])
                SELECT x.[MerchantId], x.[State],
                    CASE x.[State] WHEN N'Free' THEN @now ELSE x.[ProUntil] END,
                    CASE x.[State] WHEN N'Trial' THEN DATEADD(day, @days, x.[ProUntil]) ELSE DATEADD(day, @days, @now) END
                FROM (
                    SELECT s.[MerchantId], s.[ProUntil],
                        CASE
                            WHEN s.[ProUntil] > @now THEN N'Trial'
                            WHEN s.[LastPeriodKind] = N'Paid' AND DATEADD(day, @graceDays, s.[ProUntil]) > @now THEN N'Grace'
                            ELSE N'Free'
                        END AS [State]
                    FROM [Subscriptions] s
                    WHERE s.[ProUntil] <= @now
                       OR (EXISTS (SELECT 1 FROM [SubscriptionPeriods] p
                                   WHERE p.[MerchantId] = s.[MerchantId] AND p.[Kind] = N'Trial'
                                     AND p.[StartsAt] <= @now AND p.[EndsAt] > @now)
                           AND NOT EXISTS (SELECT 1 FROM [SubscriptionPeriods] p
                                           WHERE p.[MerchantId] = s.[MerchantId]
                                             AND ((p.[Kind] <> N'Trial' AND p.[StartsAt] <= @now AND p.[EndsAt] > @now)
                                                  OR (p.[Kind] = N'Paid' AND p.[EndsAt] > @now))))
                ) x;

                INSERT INTO [SubscriptionPeriods] ([MerchantId], [Kind], [Plan], [StartsAt], [EndsAt], [CreatedAt])
                SELECT t.[MerchantId], N'Transition', N'Basic', t.[StartsAt], t.[EndsAt], @now
                FROM @transition t;

                UPDATE s SET s.[ProUntil] = t.[EndsAt], s.[LastPeriodKind] = N'Transition'
                FROM [Subscriptions] s
                JOIN @transition t ON t.[MerchantId] = s.[MerchantId];

                INSERT INTO [AuditLogs] ([OccurredAt], [ActorUserId], [ActorEmail], [Action], [MerchantId], [Summary], [Note], [Details])
                SELECT @now, N'sistema', N'sistema', N'PlanTransition', t.[MerchantId],
                    N'Días de Básico de transición: del '
                        + FORMAT(SWITCHOFFSET(t.[StartsAt], '-06:00'), 'dd/MM/yyyy HH:mm') + N' al '
                        + FORMAT(SWITCHOFFSET(t.[EndsAt], '-06:00'), 'dd/MM/yyyy HH:mm') + N' (hora de México)',
                    N'Cambio de planes 2026-12',
                    N'{"state":"' + CASE t.[State] WHEN N'Free' THEN N'free' WHEN N'Trial' THEN N'trial' ELSE N'grace' END
                        + N'","startsAt":"' + CONVERT(nvarchar(40), t.[StartsAt], 127)
                        + N'","endsAt":"' + CONVERT(nvarchar(40), t.[EndsAt], 127) + N'"}'
                FROM @transition t;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Quita la transición y recalcula la cola de la suscripción. Las conversiones de Básico a Pro (5.15) no se
            // deshacen: sus periodos quedarían con un kind que el código anterior no conoce.
            migrationBuilder.Sql("""
                DELETE FROM [AuditLogs] WHERE [Action] = N'PlanTransition';
                DELETE FROM [SubscriptionPeriods] WHERE [Kind] = N'Transition';
                UPDATE s SET
                    s.[ProUntil] = last.[EndsAt],
                    s.[LastPeriodKind] = last.[Kind]
                FROM [Subscriptions] s
                CROSS APPLY (SELECT TOP (1) p.[EndsAt], p.[Kind] FROM [SubscriptionPeriods] p
                             WHERE p.[MerchantId] = s.[MerchantId]
                             ORDER BY p.[EndsAt] DESC, p.[CreatedAt] DESC) last
                WHERE s.[LastPeriodKind] = N'Transition';
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemSettings_Limits",
                table: "SystemSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemSettings_Prices",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BasicAnnualPrice",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BasicFounderAnnualPrice",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BasicFounderMonthlyPrice",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BasicMonthlyPrice",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "LastPlan",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "LockedAnnualPlan",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "SubscriptionCharges");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedAt",
                table: "SubscriptionCharges");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                table: "SubscriptionCharges");

            migrationBuilder.RenameColumn(
                name: "ProMonthlyPrice",
                table: "SystemSettings",
                newName: "MonthlyPrice");

            migrationBuilder.RenameColumn(
                name: "ProFounderMonthlyPrice",
                table: "SystemSettings",
                newName: "FounderMonthlyPrice");

            migrationBuilder.RenameColumn(
                name: "ProFounderAnnualPrice",
                table: "SystemSettings",
                newName: "FounderAnnualPrice");

            migrationBuilder.RenameColumn(
                name: "ProAnnualPrice",
                table: "SystemSettings",
                newName: "AnnualPrice");

            migrationBuilder.RenameColumn(
                name: "FairUseMonthlyPaid",
                table: "SystemSettings",
                newName: "ProFairUseMonthlyPaid");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "TrialDays",
                value: 30);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemSettings_Limits",
                table: "SystemSettings",
                sql: "[FounderSlots] >= 0 AND [FreeMonthlyPaidLimit] >= 0 AND [FreeMonthlyGeneratedLimit] >= 0 AND [FreeHistoryDays] >= 0 AND [ProFairUseMonthlyPaid] >= 0 AND [ReferralRewardDays] >= 0 AND [ReferralRewardsPerYear] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemSettings_Prices",
                table: "SystemSettings",
                sql: "[MonthlyPrice] > 0 AND [AnnualPrice] > 0 AND [FounderMonthlyPrice] > 0 AND [FounderAnnualPrice] > 0");
        }
    }
}
