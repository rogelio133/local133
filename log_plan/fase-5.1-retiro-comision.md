# Fase 5.1 — Retiro del modelo de comisión

**Estado:** ✅ hecho 2026-11-18 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas)

## Alcance (del plan)
Quitar la comisión del 3 % por venta y los estados de cuenta (nunca construidos), que la suscripción reemplaza ([diseño §8](../actualizaciones/suscripciones_2026_11_18.md#8-retiro-del-modelo-de-comisión)). Para el vendedor solo cambian dos cosas: ya no ve la comisión en el detalle de la venta, y cambian los textos legales.

Se conserva: `Payments` (sin las tres columnas), `SystemSettings.VatRate`, `SaleQrExpirationSeconds`, `GraceDays` (ahora 5, gracia de la suscripción), `GatewayTransaction.ApplicationFee` (es el cargo de Pey) y `SaleMetadata.StaticKind` (2.7).

## Qué se hizo
- **Dominio**:
  - se borró `Payments/Commission.cs`;
  - `Payment` pierde `CommissionRate`, `CommissionAmount` y `StatementId`, y `Payment.Record` pierde el parámetro `commissionRate`;
  - `Merchant` pierde `CommissionRateOverride`;
  - `SystemSettings` pierde `CommissionRate` y `DefaultCommissionRate`, y `DefaultGraceDays` pasa de 3 a 5;
  - comentarios de `MerchantStatus.Suspended` y `WebhookEventStatus.Ignored` sin estados de cuenta;
  - `LegalDocuments`: términos y aviso de privacidad pasan a la versión `2026-11-18`.
- **Aplicación**:
  - `SaleMetadata` pierde `StatementKind` y `StatementIdKey`;
  - **`BankAccountId` obligatorio** en `CreateCodiQrRequest` y `CreateCodiPushNotificationRequest`: ahora es un `string` posicional justo después de los campos obligatorios. `PeyRequestGuard` rechaza nulo, vacío o en blanco con `ArgumentException`: ningún QR ni push puede caer en la cuenta de la plataforma;
  - comentarios de `IPaymentGateway`, `RecordPayment` y `Transactions` sin comisión ni estados de cuenta.
- **Infraestructura**:
  - `PaymentRecorder` ya no lee el vendedor ni `SystemSettings` para la tasa. Quitó la rama `statement`: un pago con ese `kind` sigue el flujo normal y queda `Unmatched`. El log ya no trae la comisión;
  - las configuraciones de `Merchant`, `Payment` y `SystemSettings` pierden sus check constraints y precisiones de comisión;
  - `SaleCharging` pasa la cuenta en el nuevo orden.
- **Migración `RemoveCommission`**:
  - borra los 4 check constraints y las 5 columnas (`Merchants.CommissionRateOverride`, `Payments.CommissionRate`/`CommissionAmount`/`StatementId`, `SystemSettings.CommissionRate`);
  - pone `GraceDays = 5` en la fila semilla;
  - el `Down` regresa las columnas en 0.
- **Web**:
  - `SalePaymentView` sin comisión, y se borró `SaleFormats.Rate`;
  - `Detail.cshtml`: la fila del pago solo muestra la clave de rastreo, el pie dice «El dinero llegó completo a tu cuenta.» y la ayuda del recibo «No muestra tus datos bancarios.»;
  - «Próximamente» de `/admin`, `/admin/parametros` y `/app/mas` con textos de la suscripción;
  - comentarios de `ReceiptsController`.
- **Textos legales**:
  - en Términos, la sección 3 pasa a «Planes y prueba» (gratuito con límites; Pro a $199/mes o $1,990/año con IVA incluido, pagado con link; 30 días de prueba desde que se activa la cuenta bancaria) y la 4 a «Vencimiento y baja al plan gratuito» (5 días de gracia y luego baja automática, sin suspensión ni adeudo; Pro vuelve desde el día del pago). El detalle fino (uso justo, referidos, fundador, cancelación) queda para la 5.11;
  - en el Aviso de privacidad, los datos y finalidades hablan de la suscripción, no de comisiones ni de estados de cuenta.
- **Docs**:
  - CLAUDE.md: reglas de arquitectura, Pagos, Ventas y Recibo, orden de fases, pruebas exigidas y módulos fuera del MVP;
  - README: fila semilla;
  - `azure.md`: la migración borra columnas.

## Pruebas
- Se borró `CommissionTests`. Lo que validaba `Payment.Record` pasó a `UnitTests/Payments/PaymentTests` (datos de la transacción, un centavo, id obligatorio y de hasta 64 caracteres, monto positivo, clave de rastreo vacía ⇒ nula y larga ⇒ recortada).
- `PeyWebhookTests`:
  - se quitaron las asserts de comisión y las pruebas de tasa propia y de tasa global, junto con su helper;
  - `Un_centavo_se_registra_con_comision_cero` ahora se llama `Un_centavo_se_registra`;
  - el caso `statement` desaparece: queda `Pagos_del_QR_fijo_se_ignoran_por_ahora`.
- Se quitaron las asserts de comisión en `PushSaleTests`, `PaymentReconciliationJobTests` (prueba renombrada) y `MerchantRegistrationTests`. `SalesHistoryTests` comprueba que el detalle ya no muestra «Comisión».
- `PeyClientTests`:
  - todas las peticiones llevan cuenta;
  - el QR de monto abierto ahora afirma que envía `bankAccountId`;
  - casos nuevos de la guarda con cuenta `""` y `null` en QR y push.
- Resultados:
  - `dotnet build` sin advertencias;
  - `dotnet ef migrations has-pending-model-changes`: sin cambios;
  - `dotnet test`: 312 unitarias y 501 de integración, todas en verde.
- Migración aplicada a la base local `Local133`: `SystemSettings` = `VatRate .16`, `GraceDays 5`, `SaleQrExpirationSeconds 600`, sin ninguna columna de comisión ni `StatementId`.
- Búsqueda `commission|comisi[oó]n|statement|estados? de cuenta` en `src` y `tests` (sin migraciones ni `wwwroot/lib`): solo quedan «estado de cuenta del banco» en la ayuda de la CLABE (`RegisterBankAccount.cs`, `BankAccount/Index.cshtml`) y los `DoesNotContain("Comisión")` de `ReceiptTests` y `SalesHistoryTests`.

## Revisión en navegador
Playwright (Edge) a 375, 768 y 1280 px, con el sitio en `http://localhost:5231` y `Jobs__Enabled=false` (sin llamadas a Pey):
- `/app/ventas/{id}` de una venta pagada (venta y pago insertados por SQL para un vendedor de prueba): sin «comisión»; el pago muestra fecha, monto y clave de rastreo; pie y ayuda del recibo con los textos nuevos; pestaña «Ventas» activa.
- `/terminos` (versión 2026-11-18, secciones 3 y 4 nuevas) y `/aviso-de-privacidad`.
- `/app/mas` (pestaña «Más», «Mi información y Mi plan.»).
- `/admin` y `/admin/parametros`, con el rol Admin dado por SQL al usuario de prueba solo durante la revisión.
- En todos: sin scroll horizontal, consola sin errores y ninguna respuesta ≥ 400.

Notas:
- Como `Resend:ApiKey` está en los user-secrets, el registro del vendedor de prueba mandó un correo real de confirmación a `revision51@local133.test`, un dominio que no existe y que rebota. El correo se confirmó después por SQL.
- En la base de desarrollo quedan el vendedor de prueba (`revision51@local133.test`, Merchant 31) y su venta 70.

## Pendientes
- Revisión legal de los términos y del aviso de privacidad: va en la 5.11.
