using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Local133.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    CommissionRate = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    GraceDays = table.Column<int>(type: "int", nullable: false),
                    SaleQrExpirationSeconds = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                    table.CheckConstraint("CK_SystemSettings_CommissionRate", "[CommissionRate] >= 0 AND [CommissionRate] < 1");
                    table.CheckConstraint("CK_SystemSettings_GraceDays", "[GraceDays] >= 0");
                    table.CheckConstraint("CK_SystemSettings_SaleQrExpirationSeconds", "[SaleQrExpirationSeconds] = 0 OR [SaleQrExpirationSeconds] >= 360");
                    table.CheckConstraint("CK_SystemSettings_VatRate", "[VatRate] >= 0 AND [VatRate] < 1");
                });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "CommissionRate", "GraceDays", "SaleQrExpirationSeconds", "VatRate" },
                values: new object[] { 1, 0.03m, 3, 600, 0.16m });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemSettings");
        }
    }
}
