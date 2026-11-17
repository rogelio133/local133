# Fase 2.1 — PeyClient

**Estado:** ✅ hecho 2026-11-10 · [← Plan](../PLAN_DESARROLLO.md#fase-2--ventas-núcleo-25-semanas)

## Alcance (del plan)
**PeyClient** completo (`bank-account`, `codi/qr`, `codi/pn`, `transaction/*`) con enums y manejo de errores.

## Qué se hizo
- Contratos tomados del OpenAPI oficial (`https://docs.pey.mx/openapi.json`, versión 2026-10-20). Apareció un `error.type` nuevo: `INVALID_PARAMETERS` (plural, en los listados). `POST /codi/qr` responde 201; `codi/pn` y `bank-account`, 200.
- `Domain/Pey`: enums en PascalCase (en el cable, `UPPER_SNAKE`), `PeyErrorType` (con `Unknown`), `PeyWebhookEventTypes` y `PeyLimits`.
- `Application/Payments`: `IPaymentGateway` (8 operaciones), DTOs y excepciones `PaymentGatewayException` → `Validation` (+ `InvalidClabe`), `NotFound`, `Authentication`, `State` (`CONCURRENT_UPDATE` es transitorio) y `Unavailable`. Sin `error.type` reconocible se clasifica por código HTTP.
- `Infrastructure/Pey`: `PeyClient` + `AddPeyGateway` (sección `Pey`; `Pey:ApiKey` en user-secrets, opcional al arrancar). `Microsoft.Extensions.Http.Resilience` 9.10.0 (pipeline estándar). **Reintentos por método**: GET en cualquier falla transitoria; POST solo en 503/429, porque Pey no tiene llave de idempotencia y un reintento tras un 500 o un timeout podría duplicar un QR o un push. Un timeout de POST se reporta como `Unavailable` con resultado incierto.
- Las reglas de Pey se validan antes de llamar (`ArgumentException`): expiración 0/≥360, centavos, `numRef` ≤ 7 dígitos, celular de 10, CLABE de 18 dígitos (el dígito verificador llega en 2.2), metadata 50/40/500. Sin `bankAccountId` el campo se omite (cobro a la cuenta de la plataforma). Logs con `requestId`, sin bodies ni API key.

## Pruebas
- 60 pruebas nuevas contra WireMock (`WireMock.Net.Minimal` 2.20.0) en `tests/Local133.IntegrationTests/Pey`: cuerpos y respuestas de cada operación, los 18 `error.type`, clasificación por código, reintentos GET/POST, timeout, cancelación, validación previa y falta de API key. No usan SQL Server, así que corren en CI. 106/106 verdes en local (64 en CI).

## Notas
- Sin llamadas reales a Pey todavía: la primera prueba en producción ($0.01, CLABE de prueba) llega con 2.2/2.4.
