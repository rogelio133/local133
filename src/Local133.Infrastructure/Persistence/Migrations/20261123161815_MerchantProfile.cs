using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Local133.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MerchantProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Merchants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoKey",
                table: "Merchants",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopPhotoKey",
                table: "Merchants",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "LogoKey",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "ShopPhotoKey",
                table: "Merchants");
        }
    }
}
