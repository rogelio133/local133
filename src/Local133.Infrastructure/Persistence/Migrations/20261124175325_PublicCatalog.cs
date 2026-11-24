using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Local133.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublicCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PublicCatalogEnabled",
                table: "Merchants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PublicSlug",
                table: "Merchants",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_PublicSlug",
                table: "Merchants",
                column: "PublicSlug",
                unique: true,
                filter: "[PublicSlug] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Merchants_PublicSlug",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "PublicCatalogEnabled",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "PublicSlug",
                table: "Merchants");
        }
    }
}
