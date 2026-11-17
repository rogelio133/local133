# Fase 2.2 — Cuenta bancaria mínima

**Estado:** ✅ hecho 2026-11-11 · [← Plan](../PLAN_DESARROLLO.md#fase-2--ventas-núcleo-25-semanas)

## Alcance (del plan)
**Cuenta bancaria mínima**: formulario (titular, CLABE con validación, alias, tipo de titular) → `POST /bank-account`; pantalla de estatus.

## Qué se hizo
- `Domain/BankAccounts`: `Clabe` (`Normalize` quita espacios/guiones, `IsValid` con dígito verificador de pesos 3-7-1, `Mask` ⇒ `•••• 5713`) y entidad `BankAccount` (`Submit`, `Resubmit` para reintento tras `Error`, `UpdateStatus` listo para 2.3). Tabla `BankAccounts` (migración `BankAccounts`): enums como texto, CLABE `char(18)`, FK `Restrict`, **índice único en `MerchantId`** (una cuenta por vendedor hasta la Fase 4; también frena el doble envío) e índice único en `PeyBankAccountId`.
- Reglas puras en `Merchant`: `CanSubmitBankAccount` (solo `PendingBankAccount` y sin cuenta o con la cuenta en `Error`) y `ApplyBankAccountStatus(PeyCodiStatus)`: durante el onboarding `Pending/Processing` ⇒ `BankAccountInReview`, `Active` ⇒ `Active`, `Error` ⇒ `PendingBankAccount`; fuera del onboarding no cambia nada (el `Error` de una cuenta activa queda para 2.3/Fase 4). 2.3 reutiliza la misma regla.
- Caso de uso `IBankAccountRegistration` (comando + validador FluentValidation en `Application/BankAccounts`, implementación en `Infrastructure/BankAccounts`): valida → `RegisterBankAccountAsync` → guarda cuenta + estatus + `BusinessName` (si estaba vacío, toma el alias) en un solo `SaveChanges`. Si Pey falla no se guarda nada: `INVALID_CLABE` ⇒ error en el campo CLABE; otra validación ⇒ error general; no disponible ⇒ "intenta en unos minutos" (log: resultado incierto); auth/estado/falta de API key ⇒ mensaje genérico + log de error. Los logs llevan la CLABE enmascarada.
- **Multi-tenant** (primera entidad con `MerchantId`): `ITenantContext` (Application) + filtro global en `AppDbContext` (`!IsMerchantScoped || MerchantId == CurrentMerchantId`). En el sitio, `HttpTenantContext` usa el claim `local133:merchant_id` que agrega `AppClaimsPrincipalFactory` al iniciar sesión; un `Merchant` sin claim no ve nada; admin, anónimo (webhook) y jobs no tienen alcance (`SystemTenantContext`). El alta busca la cuenta existente con `IgnoreQueryFilters()` y `MerchantId` explícito para no depender del claim.
- `/app/cuenta-bancaria` (`BankAccountController`): formulario (tipo de titular en radios, titular, CLABE `inputmode=numeric`, nombre del negocio precargado) o pantalla de estatus (badge, CLABE enmascarada, fecha en hora de CDMX). PRG con mensaje. Reintento tras rechazo: aviso con el motivo y datos precargados (menos la CLABE). `[Clabe]` en cliente y servidor (adaptador en `validation.js`) y `form[data-disable-on-submit]` en `site.js` para evitar doble envío.
- Inicio: "Registrar mi CLABE" / "Corregir mi cuenta" (con el motivo del rechazo) / "Ver estatus". Pestaña activa del layout del vendedor por controlador (la cuenta bancaria marca "Más"); "Más" tiene el link a Cuenta bancaria.

## Pruebas
- 48 unitarias (`ClabeTests`, `MerchantBankAccountStatusTests`) y 21 de integración (`BankAccountTests`: alta, activación inmediata, CLABE inválida sin llamar a Pey, errores de la pasarela, segundo envío, reintento sobre la misma fila, antiforgery, suspendido, admin, aislamiento por página y por filtro de EF). `FakePaymentGateway` reemplaza a Pey en `Local133WebFactory`: ninguna prueba del sitio llama a la API real. 198/198 verdes en local (124 en CI).
- Prueba de arranque del sitio real: registro → Inicio con "Registrar mi CLABE" → formulario → CLABE con dígito incorrecto rechazada en servidor sin llamar a Pey.

## Pendientes
- Alta real contra Pey (producción) con la CLABE de prueba, y revisión en navegador de la validación en cliente a 375 px.

## Verificar estatus (agregado 2026-11-11)
- Con la cuenta en revisión, `/app/cuenta-bancaria` muestra el botón "Verificar estatus" (`POST /app/cuenta-bancaria/verificar`, antiforgery). Consulta `GET /bank-account/{id}` y aplica el resultado:
  - `Active` ⇒ vendedor `Active` + "Comenzar a vender" (`/app/cobrar`);
  - `Error` ⇒ formulario de reintento con el motivo;
  - `Pending`/`Processing` ⇒ aviso.
- Freno de 1 min entre consultas manuales (`BankAccountStatusChecks.ManualMinInterval`), sobre la nueva columna `BankAccounts.StatusCheckedAt` (migración `BankAccountStatusCheckedAt`). El alta y el reintento cuentan como lectura.
- La lógica es el caso de uso `IBankAccountStatusSync.SyncAsync(bankAccountId, minInterval)` (Application/BankAccounts; implementación en Infrastructure/BankAccounts): sin sesión ni claims, con `IgnoreQueryFilters()`. Guarda en un solo `SaveChanges` `BankAccount.RecordStatusCheck` + `Merchant.ApplyBankAccountStatus`. Si Pey falla no guarda nada.
- Pruebas: 13 unitarias (`BankAccountStatusCheckTests`) y 14 de integración en `BankAccountTests` (incluye la llamada sin sesión, como el job, y el aislamiento entre vendedores). 225/225 verdes en local (137 en CI).
- Pendiente: revisión en navegador con Playwright a 375 px y ≥ 992 px (el MCP no conectó en esta sesión) y una consulta real contra Pey con la CLABE de prueba.
