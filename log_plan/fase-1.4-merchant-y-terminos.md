# Fase 1.4 — Merchant al registrarse y aceptación de términos

**Estado:** ✅ hecho 2026-11-10 · [← Plan](../PLAN_DESARROLLO.md#fase-1--registro-y-autenticación-1-semana)

## Alcance (del plan)
Al registrarse se crea `Merchant` en `PendingBankAccount`; casillas de aceptación de términos y aviso de privacidad en la página Register scaffoldeada.

## Qué se hizo
- Entidad `Merchant` (`Domain/Merchants`: `UserId` 1:1 con `AspNetUsers`, FK `Restrict`, índice único; `Status` como texto; `BusinessName` y `CommissionRateOverride` vacíos hasta Fases 4/6) y enum `MerchantStatus`.
- Toda alta de vendedor pasa por `AccountRegistration` (Web/Identity): usuario + rol `Merchant` + cuenta externa + `Merchant` en **una transacción**; si algo falla no queda usuario sin Merchant.
- **Una sola casilla** "Acepto los Términos y condiciones y el Aviso de privacidad" (base `LegalAcceptanceInput`, parcial `_AcceptLegalCheckbox`) en Register y en `/Identity/Account/ExternalLogin` (alta con Google/Facebook; quien ya tiene la cuenta ligada entra sin volver a aceptar). Validación `[MustBeTrue]` en servidor + adaptador en `wwwroot/js/validation.js`.
- `AspNetUsers` guarda `TermsAcceptedAt`/`TermsVersion` y `PrivacyNoticeAcceptedAt`/`PrivacyNoticeVersion`; la versión vigente está en `Domain/Legal/LegalDocuments` (subirla al cambiar el texto).
- Páginas públicas `/terminos` y `/aviso-de-privacidad` con texto **borrador pendiente de revisión legal**; ligas en el pie del layout.
- Migración `Merchants` con backfill: los usuarios con rol `Merchant` que ya existían reciben su `Merchant` pendiente (sin términos aceptados).

## Pruebas
- 7 pruebas nuevas (`MerchantRegistrationTests`); los helpers de prueba crean vendedores con `AccountRegistration`. 46/46 verdes en local (4 corren en CI).

## Pendientes
- Probar en navegador que la validación en cliente bloquea el envío con la casilla sin marcar (el HTML ya emite `data-val-mustbetrue` y carga `validation.js`).
