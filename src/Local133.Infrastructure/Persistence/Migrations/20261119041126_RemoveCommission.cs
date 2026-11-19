using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Local133.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCommission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemSettings_CommissionRate",
                table: "SystemSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_CommissionAmount",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_CommissionRate",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Merchants_CommissionRateOverride",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "CommissionRate",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "CommissionAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CommissionRate",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "StatementId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CommissionRateOverride",
                table: "Merchants");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "GraceDays",
                value: 5);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CommissionRate",
                table: "SystemSettings",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionAmount",
                table: "Payments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionRate",
                table: "Payments",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "StatementId",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionRateOverride",
                table: "Merchants",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CommissionRate", "GraceDays" },
                values: new object[] { 0.03m, 3 });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemSettings_CommissionRate",
                table: "SystemSettings",
                sql: "[CommissionRate] >= 0 AND [CommissionRate] < 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_CommissionAmount",
                table: "Payments",
                sql: "[CommissionAmount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_CommissionRate",
                table: "Payments",
                sql: "[CommissionRate] >= 0 AND [CommissionRate] < 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Merchants_CommissionRateOverride",
                table: "Merchants",
                sql: "[CommissionRateOverride] IS NULL OR ([CommissionRateOverride] >= 0 AND [CommissionRateOverride] < 1)");
        }
    }
}
