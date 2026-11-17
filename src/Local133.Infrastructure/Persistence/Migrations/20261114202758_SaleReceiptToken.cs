using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Local133.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Fase 2.8: token del recibo público de cada venta (+ índice del historial). Las ventas existentes reciben un token
    /// aleatorio (CRYPT_GEN_RANDOM, 16 bytes en hexadecimal) antes de volver obligatoria la columna.
    /// </summary>
    public partial class SaleReceiptToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReceiptToken",
                table: "Sales",
                type: "char(32)",
                unicode: false,
                fixedLength: true,
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [Sales] SET [ReceiptToken] = LOWER(CONVERT(char(32), CRYPT_GEN_RANDOM(16), 2)) WHERE [ReceiptToken] IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "ReceiptToken",
                table: "Sales",
                type: "char(32)",
                unicode: false,
                fixedLength: true,
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "char(32)",
                oldUnicode: false,
                oldFixedLength: true,
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_MerchantId_CreatedAt",
                table: "Sales",
                columns: new[] { "MerchantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ReceiptToken",
                table: "Sales",
                column: "ReceiptToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_MerchantId_CreatedAt",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_ReceiptToken",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ReceiptToken",
                table: "Sales");
        }
    }
}
