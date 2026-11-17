# Fase 2.3 — Webhook de Pey y job de sondeo

**Estado:** ✅ hecho 2026-11-12, junto con la [2.5](fase-2.5-tiempo-real.md) · [← Plan](../PLAN_DESARROLLO.md#fase-2--ventas-núcleo-25-semanas)

## Alcance (del plan)
**Endpoint de webhook** con verificación de firma, `WebhookEvents`, manejo de `bank.account.updated` ⇒ `Active`. Job de sondeo de estatus.

## Qué se hizo
- Formato real tomado de docs.pey.mx (el OpenAPI trae `"webhooks": {}`):
  - `codi.qr.payment_received` = `{type, amount (number), apiKeyId, bankAccountId?, codiQrId, concept, datePaid (segundos Unix), numRef (texto), transactionId, metadata:{clabe, bankCode, accountType, description, trackingKey, externalMetadata}}`;
  - `bank.account.updated` = `{type, bankAccountId, codiStatus:"ACTIVE", name, clabe, entity, ownerType, ownerId, taxId?}`, sin `errorMessage`.
- Firma: `Infrastructure/Webhooks/StandardWebhookVerifier`, propio y sin paquete. Calcula HMAC-SHA256 de `"{id}.{timestamp}.{body}"` con el secreto `whsec_<base64>` y acepta varias firmas `v1,…` en el header. Compara en tiempo constante y tolera ±5 min (la del estándar; Pey no documenta otra). 29 pruebas, incluido el vector oficial de Standard Webhooks; corren en CI.
- `POST /api/webhooks/pey` (`PeyWebhooksController`, anónimo, body crudo, máximo 64 KB). Respuestas:
  - sin `Pey:WebhookSecret` ⇒ 500;
  - firma inválida o sin headers ⇒ 401;
  - si no, `IPeyWebhookProcessor` (Infrastructure/Webhooks/PeyWebhookProcessor), que guarda en `WebhookEvents` y **procesa en la misma petición** (ver §4 del plan).
- Bitácora `WebhookEvents` (`Domain/Webhooks/WebhookEvent`): `WebhookId` único, tipo, payload crudo, timestamp, estatus `Received`/`Processed`/`Ignored`/`Failed`, intentos y error. No tiene filtro multi-tenant (es del admin; su pantalla llega en la Fase 6).
  - Un id `Processed`/`Ignored` repetido ⇒ `Duplicate`.
  - Uno `Failed` o a medias se reprocesa cuando Pey lo reintenta.
  - `codi.pn.payment_received` y `payment_link.*` ⇒ `Ignored`, con el payload guardado.
- `bank.account.updated` ⇒ `IBankAccountStatusSync.ApplyWebhookAsync(peyBankAccountId, status)`: comparte la transición con la consulta, pero usa `UpdateStatus` y no toca `StatusCheckedAt` ni llama a Pey. Una cuenta desconocida ⇒ `Failed`.
- Hangfire 1.8.25 (`Hangfire.AspNetCore` + `Hangfire.SqlServer` con `Microsoft.Data.SqlClient`):
  - almacenamiento en la misma base, esquema `HangFire`, que crea Hangfire;
  - `Web/Jobs/JobsSetup`, con `Jobs:Enabled` (`true` por defecto; las pruebas lo apagan) y sin reintentos automáticos;
  - sin dashboard todavía (Fase 6/7).
- Job `sondeo-cuentas-bancarias` (`*/30 * * * *`, en hora de CDMX; `Infrastructure/Jobs/BankAccountPollingJob`): toma las cuentas `Pending`/`Processing`, las nunca consultadas y luego las de lectura más antigua. Llama `SyncAsync(id)` sin `minInterval`, un scope por cuenta; si una falla, sigue con la siguiente.

## Pruebas
- `PeyWebhookTests` (firma, bitácora, idempotencia, `bank.account.updated`) y `BankAccountPollingJobTests`.

## Pendientes
- registrar la URL pública y el secreto en portal.mipey.mx;
- recibir un `bank.account.updated` real con la CLABE de prueba;
- el correo de cuenta activada/rechazada (Notificaciones).
