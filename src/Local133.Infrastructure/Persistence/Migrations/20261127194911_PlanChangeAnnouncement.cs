using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Local133.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// 5.16 (solo datos, sin cambios del modelo):
    /// <list type="bullet">
    /// <item>renombra los tipos de aviso que se guardan como texto: <c>DowngradedToFree</c> → <c>PlanExpired</c> y
    /// <c>ProDaysEnding</c> → <c>PlanDaysEnding</c>;</item>
    /// <item>deja el renglón global <c>PlanChange</c> en <c>AuditLogs</c> (actor «sistema») con la hora del servidor: es el
    /// corte del anuncio del cambio de planes, que el job diario manda a los vendedores registrados antes.</item>
    /// </list>
    /// </summary>
    public partial class PlanChangeAnnouncement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [Notifications] SET [Type] = N'PlanExpired' WHERE [Type] = N'DowngradedToFree';
                UPDATE [Notifications] SET [Type] = N'PlanDaysEnding' WHERE [Type] = N'ProDaysEnding';

                INSERT INTO [AuditLogs] ([OccurredAt], [ActorUserId], [ActorEmail], [Action], [MerchantId], [Summary], [Note], [Details])
                VALUES (TODATETIMEOFFSET(SYSUTCDATETIME(), 0), N'sistema', N'sistema', N'PlanChange', NULL,
                    N'Planes Básico y Pro, sin plan gratuito: empieza el anuncio a los vendedores registrados antes de este momento',
                    N'Cambio de planes 2026-12', NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Los tipos nuevos (seguimiento y anuncio) no existían antes: se borran sus avisos.
            migrationBuilder.Sql("""
                DELETE FROM [AuditLogs] WHERE [Action] = N'PlanChange';
                DELETE FROM [Notifications] WHERE [Type] IN (N'PlanExpiredFollowUp', N'PlanChangeAnnouncement');
                UPDATE [Notifications] SET [Type] = N'DowngradedToFree' WHERE [Type] = N'PlanExpired';
                UPDATE [Notifications] SET [Type] = N'ProDaysEnding' WHERE [Type] = N'PlanDaysEnding';
                """);
        }
    }
}
