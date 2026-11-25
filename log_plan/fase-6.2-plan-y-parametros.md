# Fase 6.2 — Plan por cliente y parámetros globales (admin)

**Estado:** ✅ hecho 2026-11-24 · [← Plan](../PLAN_DESARROLLO.md#fase-6--portal-administrador-15-semanas)

## Alcance (del plan)
**Plan por cliente:**
- periodos, cargos y pagos de la suscripción, y referidos;
- acciones: regalar días, registrar un pago manual y anular un premio.

**Parámetros globales:** precios y fundador, días de prueba y de gracia, límites del gratuito, uso justo, premios de referidos, IVA y expiración del QR.

Todo queda en la auditoría.

**Demostrable:**
1. El admin abre `/admin/clientes/{id}/plan`, regala 10 días con su motivo y el vendedor recibe el aviso y el correo.
2. Abona a mano un cargo `Partial`.
3. Anula el premio de un referido.
4. Ve todo en «Historial de cambios del admin».
5. En `/admin/parametros` cambia un precio y aparece la advertencia de los Términos.

## Decisiones
Del usuario, el 2026-11-24:
- **Pago manual = solo abonar un cargo que ya existe** (`Pending`, `Processing`, `Partial` o `Failed`). No hay pagos fuera de Pey: el caso es un link pagado que el webhook y la conciliación no abonaron (por ejemplo, uno de más de 60 días), o un `Partial` que el admin revisó.
- **El abono manual cuenta igual que uno de Pey**: periodo encadenado desde el último `PaidAt` registrado (o ahora), anual congelado, lugar de fundador si el cargo salió a ese precio, premio para quien lo invitó y correo «Recibimos tu pago».
- **Anular un premio otorgado quita los días que no se han usado**:
  - si el premio no ha empezado, se quita completo; si está en curso, desde ahora;
  - los periodos que venían después se recorren hacia atrás por la misma duración, sin dejar hueco;
  - los días ya usados no se recuperan;
  - un premio pendiente anulado ya no se da;
  - un premio anulado no cuenta para el tope de 12 en 12 meses.
- **Regalar días** lleva la casilla «Avisar al vendedor», marcada por defecto: aviso en el sitio y correo. El motivo nunca le llega al vendedor.

Propias:
- **Dar premios pendientes**: botón para el premio que quedó pendiente por la carrera de la 5.8 (la cuenta de quien invita se activó mientras se abonaba el pago de su referido). Reutiliza `ReferralRewarder.GrantPendingAsync`, con el tope de siempre.
- **Advertencia de los Términos, sin bloqueo** (lo pedía la bitácora de la 5.11):
  - los 14 parámetros que están escritos en los Términos (los que compara `LegalPagesTests`) llevan la insignia «En los Términos»;
  - al guardar uno, se avisa que hay que publicar una versión nueva (texto, `LegalDocuments` y `LegalPagesTests`);
  - la pantalla no impide el cambio: para la prueba real con $0.01 hay que poder bajar el precio.
- **Reglas de los parámetros** (`SystemSettingsRules`): fundador ≤ precio normal, lugares de fundador ≥ asignados, QR de 360 a 86,400 s (Pey exige ≥ 360), tope de generados ≥ límite de concretados, precios con 2 decimales y tasas con 4. Los porcentajes se capturan en %.
- **`AuditLogs` sin filtro multi-tenant**: ningún vendedor la lee. Va en la misma transacción que el cambio.
- La pestaña Plan del admin **sí** muestra los links pendientes (la regla de la 5.7 de no mostrarlos es para el vendedor).

## Qué se hizo
- **Dominio:**
  - `Audit/AuditLog` y `AuditAction`;
  - `Settings/SystemSettingsRules` (`Validate`, `Changes`, `CopyValues`, `ValueOf`, `InTerms`, `Fields`);
  - `Subscription.RemoveUnusedDays`;
  - `Referral.RevokedAt`, `Revoke`, `CanBeRevoked` e `IsRewardActive`; `IsPendingReward` excluye los anulados;
  - `SubscriptionCharge.CreditedManuallyAt` y `MarkPaid(paidAt, creditedManuallyAt)`;
  - `NotificationType.ProDaysGranted`.
- **Persistencia:** migración `AdminPlanActions`, con la tabla `AuditLogs` y las columnas `Referrals.RevokedAt` y `SubscriptionCharges.CreditedManuallyAt`.
- **Infrastructure:**
  - `Subscriptions/SubscriptionCrediting`: el abono, sacado de `SubscriptionPaymentRecorder`. El recorder la usa sin cambiar su comportamiento, y las pruebas de la 5.6 y la 5.8 siguen pasando;
  - `Admin/AdminPlanActions` (`IAdminPlanActions`) y `Admin/SystemSettingsEditor` (`ISystemSettingsEditor`);
  - `MerchantEmails.ProDaysGranted`;
  - `ReferralRewarder` ignora los anulados.
- **Application:** `Admin/AdminPlanActions.cs` (contratos, `AdminActor`, `AdminPlanLimits` y resultados).
- **Web / Admin:**
  - `CustomersController`, con cuatro POST bajo `/admin/clientes/{id}/plan/…`;
  - `CustomerDetailQuery.PlanAsync`;
  - `Models/CustomerPlanViewModels.cs` (`CustomerPlanViewModel`, filas, `PlanLabels`, `GrantDaysInput`, `PlanNoteInput`);
  - vistas `Customers/Plan.cshtml` y `_PlanConfirmModal.cshtml`;
  - `SettingsController` (`/admin/parametros`, que sale de `HomeController`), `Models/SettingsViewModels.cs` y `Views/Settings/Index.cshtml`. Se borró la vista `_ComingSoon` de Parámetros;
  - `_AdminLayout` marca «Parámetros» por controlador.
- **Web / Merchant:** «Invita y gana» muestra «Pagó · premio anulado» y no cuenta los anulados.

## Pruebas
- **Unitarias:** `tests/Local133.UnitTests/Subscriptions/AdminPlanRulesTests`, con 22 pruebas:
  - quitar los días de un premio que no ha empezado, en curso o ya usado, y del último premio;
  - el tipo inválido;
  - anular un referido pendiente, otorgado o sin pago;
  - el abono manual;
  - cada regla de los parámetros y sus bordes;
  - los cambios y los campos de los Términos;
  - la bitácora.
- **Integración:** `Admin/CustomerPlanTests` (19) y `Admin/SettingsTests` (10).
  - Acceso por rol en cada POST, y el token antiforgery en cada formulario.
  - Lectura de periodos, cargos, referidos y quién lo invitó.
  - Regalar días: con y sin aviso, validación en el cliente y en el servidor, sin prueba y vendedor inexistente.
  - Abono manual: el `Partial` pasa a pagado con fundador, premio del referido y correo; uno ya pagado no se repite; el cargo de otro vendedor da 404; sin motivo no pasa nada.
  - Anular: un premio futuro con el regalo recorrido; un pendiente que luego ya no se da; «Dar premios pendientes»; el referido de otro vendedor da 404.
  - Parámetros: valores, menú activo, guardado con auditoría y advertencia, un cambio fuera de los Términos, sin cambios, inválidos, fundadores asignados, y un precio nuevo que solo afecta al link nuevo.
  - En `CustomersTests` se agregó un caso del filtro de plan (premio cortado con el regalo recorrido), y la pestaña Plan ya no tiene «Fase 6.2».
- `dotnet build`: sin advertencias.
- `dotnet test` completo: 658 unitarias, 17 de la portada y 778 de integración, todas correctas (SQL Server local).
- `dotnet ef migrations has-pending-model-changes`: sin cambios.

## Revisión en navegador
Hecha el 2026-11-24 con el MCP de Playwright y Edge, a 375, 768 y 1280 px. El sitio corrió sin Pey, Resend ni jobs, contra la base de desarrollo.
- **Sesión de admin:** con el admin de revisión `revision.admin@local133.test`, que creó el seeder al arrancar con `Seed__Admin__Email`/`Seed__Admin__Password` en variables de entorno (decisión del usuario). La contraseña al azar se borró al terminar. **La cuenta queda en la base de desarrollo.**
- **Pestaña Plan de #4** (Pro, fundador, 3 cargos y 2 referidos sin pago):
  - resumen, periodos, cargos con sus pagos y referidos;
  - «Regalar días» enviado vacío ⇒ «Escribe cuántos días.» y «Escribe el motivo.», sin enviar;
  - el modal «Abonar a mano» del cargo #3 enviado sin motivo ⇒ «Escribe el motivo.», sin enviar. Se cerró con «No, regresar»: **no se abonó nada**.
- **Flujo feliz en #43** (vendedor de prueba en su prueba):
  - se regalaron 2 días con aviso;
  - salió el mensaje «Listo: se regalaron 2 días…», el periodo «Regalo del admin» y la entrada del historial con el motivo;
  - el correo «Te regalamos 2 días de Pro» quedó en `App_Data/emails`, sin el motivo.
- **`/admin/parametros`:**
  - precio `199.999` y QR `300` ⇒ errores en cliente, sin recargar;
  - gracia 5 → 6 ⇒ «se guardó 1 cambio», la advertencia de los Términos y «Cambios recientes». Después se regresó a 5, y las dos entradas quedaron en la bitácora;
  - «Parámetros» activo en el menú.
- **En todos los tamaños:**
  - sin scroll horizontal de la página. A 375 px las tablas de periodos y pagos se desplazan dentro de su `table-responsive`; a 768 y 1280 caben;
  - «Clientes» activo y la pestaña «Plan» marcada;
  - consola sin errores ni advertencias en toda la sesión.
- No se probó en navegador anular un premio ni «Dar premios pendientes», porque la base de desarrollo no tiene premios otorgados ni pendientes. Los cubren las pruebas de integración.

## Pendientes
- Quitar o conservar el admin de revisión `revision.admin@local133.test` de la base de desarrollo (decisión del usuario).
- 6.3: la lista global de cobranza (cargos `Partial`, pagos extra y links pagados sin abonar), la CLABE en disputa y suspender o reactivar. Esas acciones también deben ir a `AuditLogs`.
- Los cambios de parámetros no tienen control de concurrencia optimista. Hay un candado de fila al guardar, y si dos admins guardan, gana el último, con los dos cambios en la bitácora.
