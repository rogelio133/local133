using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Local133.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalePushNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PeyCodiId",
                table: "Sales",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PeyTransactionId",
                table: "Sales",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Sales",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_PeyTransactionId",
                table: "Sales",
                column: "PeyTransactionId",
                unique: true,
                filter: "[PeyTransactionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_PeyTransactionId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "PeyCodiId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "PeyTransactionId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Sales");
        }
    }
}
