# Fase 1.5 — Inicio del portal cliente y seed del admin

**Estado:** ✅ hecho 2026-11-11 · [← Plan](../PLAN_DESARROLLO.md#fase-1--registro-y-autenticación-1-semana)

## Alcance (del plan)
Inicio del portal cliente (área `Merchant`) con checklist de onboarding; seed del usuario admin.

## Qué se hizo
- Regla pura `Domain/Merchants/OnboardingChecklist.For(MerchantStatus)`: 4 pasos (crear cuenta → cuenta bancaria → revisión de Banxico → listo para cobrar) con estado Done/Current/Pending. Para `Suspended`/`Disabled` la lista viene vacía. **Sin paso de perfil**: se agrega en la Fase 4 junto con la pantalla.
- `/app` carga el `Merchant` del usuario. Muestra "Hola, {nombre del negocio o correo}" y:
  - en onboarding: tarjeta "Activa tu cuenta" con "Paso X de 4", barra de avance y el paso en curso con su descripción. El botón de cuenta bancaria dice "Disponible pronto" hasta 2.2;
  - `Active`: "Ya puedes cobrar" con botón a `/app/cobrar`;
  - `Suspended`/`Disabled`: aviso.
- Los textos están en `Areas/Merchant/Models/MerchantHomeViewModel`. Atributos `data-merchant-status`, `data-step` y `data-state` para las pruebas.
- Seed del admin al arrancar (`Web/Identity/AdminSeeder`) si existen `Seed:Admin:Email` y `Seed:Admin:Password` (user-secrets o variables de entorno). Crea el usuario con correo confirmado y rol `Admin` en una transacción. Es idempotente: si ya es Admin no cambia la contraseña. No promueve a un `Merchant` (log de error). A un usuario sin roles le agrega `Admin`. Si la contraseña no cumple la política no crea nada. Una falla se registra en el log y el sitio sigue arrancando.

## Pruebas
- Nuevo `tests/Local133.UnitTests` (Domain, corre en CI) con 12 pruebas del checklist. 11 de integración: Inicio por estatus + aislamiento entre dos vendedores (`MerchantHomeTests`) y casos del seeder + siembra real al arrancar (`AdminSeederTests`). La fábrica de pruebas vacía `Seed:Admin` para que los user-secrets no siembren nada. 129/129 verdes en local (76 en CI).
- Prueba de arranque del sitio real con `Seed__Admin__*`: se creó el admin y su login va a `/admin`; un vendedor registrado y confirmado ve el paso 2 en curso; con `UPDATE Merchants SET Status=…` se vieron los otros cuatro estatus.

## Pendientes
- Revisión visual en navegador a 375 px.
