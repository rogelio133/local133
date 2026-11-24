using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Local133.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Referrals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReferralId",
                table: "SubscriptionPeriods",
                type: "int",
                nullable: true);

            // Código de invitación (5.8): se agrega nulo, se genera para los vendedores existentes y luego pasa a no nulo.
            migrationBuilder.AddColumn<string>(
                name: "ReferralCode",
                table: "Merchants",
                type: "char(6)",
                unicode: false,
                fixedLength: true,
                maxLength: 6,
                nullable: true);

            // 6 caracteres de ReferralCode.Alphabet (32: cada byte % 32 es uniforme) con CRYPT_GEN_RANDOM; si choca con
            // otro código, se genera otro.
            migrationBuilder.Sql("""
                DECLARE @alphabet char(32) = '23456789ABCDEFGHJKLMNPQRSTUVWXYZ';
                DECLARE @id int, @bytes varbinary(6), @code varchar(6), @i int;
                DECLARE pending CURSOR LOCAL FAST_FORWARD FOR SELECT [Id] FROM [Merchants] WHERE [ReferralCode] IS NULL;
                OPEN pending;
                FETCH NEXT FROM pending INTO @id;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    WHILE 1 = 1
                    BEGIN
                        SET @bytes = CRYPT_GEN_RANDOM(6);
                        SET @code = '';
                        SET @i = 1;
                        WHILE @i <= 6
                        BEGIN
                            SET @code = @code + SUBSTRING(@alphabet, (CAST(SUBSTRING(@bytes, @i, 1) AS int) % 32) + 1, 1);
                            SET @i = @i + 1;
                        END;
                        IF NOT EXISTS (SELECT 1 FROM [Merchants] WHERE [ReferralCode] = @code)
                        BEGIN
                            UPDATE [Merchants] SET [ReferralCode] = @code WHERE [Id] = @id;
                            BREAK;
                        END;
                    END;
                    FETCH NEXT FROM pending INTO @id;
                END;
                CLOSE pending;
                DEALLOCATE pending;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ReferralCode",
                table: "Merchants",
                type: "char(6)",
                unicode: false,
                fixedLength: true,
                maxLength: 6,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "char(6)",
                oldUnicode: false,
                oldFixedLength: true,
                oldMaxLength: 6,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "Referrals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReferrerMerchantId = table.Column<int>(type: "int", nullable: false),
                    ReferredMerchantId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "char(6)", unicode: false, fixedLength: true, maxLength: 6, nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    QualifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RewardedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SkippedReason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrals", x => x.Id);
                    table.CheckConstraint("CK_Referrals_NotSelf", "[ReferrerMerchantId] <> [ReferredMerchantId]");
                    table.ForeignKey(
                        name: "FK_Referrals_Merchants_ReferredMerchantId",
                        column: x => x.ReferredMerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Referrals_Merchants_ReferrerMerchantId",
                        column: x => x.ReferrerMerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_ReferralId",
                table: "SubscriptionPeriods",
                column: "ReferralId",
                unique: true,
                filter: "[ReferralId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_ReferralCode",
                table: "Merchants",
                column: "ReferralCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferredMerchantId",
                table: "Referrals",
                column: "ReferredMerchantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferrerMerchantId_RewardedAt",
                table: "Referrals",
                columns: new[] { "ReferrerMerchantId", "RewardedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionPeriods_Referrals_ReferralId",
                table: "SubscriptionPeriods",
                column: "ReferralId",
                principalTable: "Referrals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionPeriods_Referrals_ReferralId",
                table: "SubscriptionPeriods");

            migrationBuilder.DropTable(
                name: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPeriods_ReferralId",
                table: "SubscriptionPeriods");

            migrationBuilder.DropIndex(
                name: "IX_Merchants_ReferralCode",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "ReferralId",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "ReferralCode",
                table: "Merchants");
        }
    }
}
