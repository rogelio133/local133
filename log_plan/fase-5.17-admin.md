# Fase 5.17 — Admin

**Estado:** ✅ hecho 2026-11-27, sin publicar: sale junto con la 5.12 a la 5.16 y la 5.18 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas) · [diseño](../actualizaciones/planes_basico_pro_2026_11_26.md#517-admin)

## Alcance (del plan)
- Parámetros de Básico, con sus reglas.
- Filtro de Clientes y pestaña Plan con el plan.
- Regalar días por plan.
- Tablero por plan.

**Demostrable:**
- `/admin/parametros` edita los 4 precios de Básico y rechaza «Básico ≥ Pro».
- `/admin/clientes?plan=basico` lista a los vendedores con Básico.
- La pestaña Plan dice de qué plan es cada periodo y cada cargo, y regala días de Básico o de Pro.
- El tablero cuenta por plan, separa los ingresos por plan y cuenta las subidas de Básico a Pro.

## Decisiones (propias)
- **Básico < Pro también en los precios de fundador** (mensual y anual), además de los normales. Sin eso, subir de plan no tendría sentido para un fundador.
  - El usuario confirmó la regla estricta el 2026-11-27.
  - La base local tenía «Pro fundador, 1 mes» en $0.01. A petición del usuario se ajustó desde la pantalla a 99 / 69 / 199 / 169 (Básico / Básico fundador / Pro / Pro fundador). Solo cambió Pro fundador ($0.01 → $169), con su renglón en la bitácora.
- **El regalo de Pro no convierte el Básico** que haya por delante: se encadena al final, como los premios (§8). La pantalla lo dice.
- **Subidas de Básico a Pro (30 días)**: vendedores distintos con un periodo `Conversion` creado en los últimos 30 días.
  - Ese periodo solo lo crea el abono de un Pro con Básico por delante.
  - Quien paga Pro desde la gracia o el vencido de Básico no cuenta.
- **Los nombres se quedan**: `AuditAction.ProDaysGranted` y `NotificationType.ProDaysGranted` se guardan como texto, y renombrarlos pediría una migración de datos. Solo cambian los textos.
- `InTerms` suma los 4 precios de Básico (15 campos): la 5.18 los escribe en los Términos y sale junto con esta subfase.

## Qué se hizo
- **Dominio**: `SystemSettingsRules`:
  - `Fields`, `CopyValues`, `ValueOf` e `InTerms` con los precios de Básico;
  - `Validate` revisa los 8 precios, fundador ≤ normal en cada plan y Básico < Pro (normal y fundador).
- **Application**: `IAdminPlanActions.GrantDaysAsync(merchantId, plan, …)`.
- **Infraestructura**:
  - `AdminPlanActions.GrantDaysAsync`:
    - un plan inválido se rechaza;
    - regala con `AddDaysPeriod(AdminGrant, plan, …)`;
    - la bitácora dice el plan y `Details` lleva `plan`;
  - `MerchantEmails.ProDaysGranted` pasa a `DaysGranted(…, plan, días, primer día, último día, hoy)`:
    - si empieza hoy: «Ahora tienes Básico hasta el …»;
    - si empieza después: «Tus días de Pro van del … al …».
- **Web**:
  - Parámetros:
    - `SettingsInput` con los 4 campos de Básico;
    - tarjetas «Precios de Básico», «Precios de Pro» y «Fundadores (entre los dos planes)»;
    - ayudas al día (ya no hablan de los paquetes de 3 y 6 meses);
  - Clientes:
    - `PlanTierFilters.All` con `Basic`;
    - `CustomerListQuery.WithPlan` v2: Pro si un periodo de Pro cubre el instante, Básico si no; la prueba como antes;
  - Pestaña Plan:
    - columna Plan en periodos, y plan en los cargos y en el modal de abonar;
    - origen «Básico convertido» y «Cambio de planes 2026-12»;
    - «Después Básico hasta el …», el último pago con su plan y el anual congelado con su plan;
    - «Regalar días» con selector de plan, preseleccionado en el plan del final de la cadena;
  - Tablero:
    - Básico en «Vendedores por plan»;
    - ingresos por plan en «Este mes» y «Mes anterior», con una consulta por plan agrupada por mes;
    - subidas de Básico a Pro en 30 días;
    - «Uso justo (Básico y Pro)»;
  - Transacciones › Suscripciones: el plan del cargo, que antes decía «Pro» escrito a mano.

## Pruebas
- **Unitarias** (792, en verde). `AdminPlanRulesTests`:
  - errores de Básico (≥ Pro, fundador > normal);
  - Básico < Pro también de fundador y en el anual;
  - bordes con Básico;
  - `Changes` con Básico;
  - `InTerms` = 15.
- **Integración** (949 de 950 en verde):
  - `SettingsTests`: mostrar, guardar y auditar los precios de Básico, con la advertencia de Términos; inválidos de Básico;
  - `CustomersTests.Filtro_de_plan_coincide_con_el_estado_calculado`:
    - cubre los 6 estados, con Básico pagado, transición, prueba con Básico por delante, conversión, Pro seguido de Básico, gracia de Básico y transición vencida;
  - `CustomerPlanTests`:
    - regalar Básico con la prueba (se encadena, correo y bitácora con «Básico»);
    - regalar Pro a un vencido (lo reactiva desde hoy);
    - plan inválido (`7`, `Oro` y vacío);
    - plan de periodos y cargos, conversión, transición y «Después…»;
    - validación en cliente del plan;
  - `DashboardTests`:
    - conteo con Básico;
    - ingresos por plan;
    - conversión con un cargo de Básico;
    - subidas en 30 días (cuenta una vez por vendedor y no las de hace 40 días ni un Pro sin Básico);
  - `TransactionsTests`: el plan del cargo.
  - La que falla, `EmailLayoutTests.El_logo_existe_en_wwwroot`, busca `Local133.sln` desde la carpeta de la DLL. Falló porque las pruebas corrieron desde una carpeta de salida temporal (ver Montaje).
- Sin migración: no cambió el modelo.

## Revisión en navegador
- **Montaje**:
  - `src/Local133.Web/bin` estaba bloqueado por Visual Studio y por el sitio del usuario en el puerto 5231;
  - se compiló en una carpeta temporal y el sitio corrió en el 5232, desde Bash, con `Resend__ApiKey=`, `Pey__ApiKey=` y `Jobs__Enabled=false`, sin llamar a Pey;
  - se entró con el admin sembrado.
- **Lo revisado** a 375, 768 y 1280 px:
  - `/admin/parametros`: tarjetas nuevas, validación en cliente («Escribe un precio…») y error del servidor «Básico debe costar menos que Pro.», sin guardar;
  - `/admin/clientes?plan=basico`: el select con Básico, 5 vendedores;
  - pestaña Plan del vendedor 55 (Básico): columna Plan, cargo de Básico, selector en Básico y validación en cliente del regalo. No se envió el regalo, para no tocar sus datos (lo cubren las pruebas);
  - `/admin/tablero`: conteo por plan, ingresos por plan y subidas;
  - `/admin/transacciones/suscripciones`: «Básico, 1 mes» y «Pro, 1 mes».
- **Comprobaciones**: sin scroll horizontal y consola sin errores. En 375 la tabla de periodos se desplaza dentro de su `table-responsive`, como antes.
- Capturas en `.playwright-mcp/517-*.png` (fuera de git).

## Pendientes
- **Verificación de extremo a extremo con Pey real** (§16): pide Básico y Pro en $0.01 al mismo tiempo, y la regla Básico < Pro no lo permite desde la pantalla. Para esa prueba hay que usar, por ejemplo, Básico $0.01 y Pro $0.02 (y los de fundador igual), o cambiar los precios directo en la base.
- Correr `dotnet build` y `dotnet test` desde `src`/`tests` con el sitio de Visual Studio detenido: confirma la prueba del logo y que compila en la carpeta normal.
