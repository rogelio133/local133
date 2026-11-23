# Fase 6.1 — Clientes en el admin

**Estado:** ✅ hecho 2026-11-23 · [← Plan](../PLAN_DESARROLLO.md#fase-6--portal-administrador-15-semanas)

## Alcance (del plan)
Listado de clientes con búsqueda y filtros (estatus, plan, fecha, vencimiento); detalle con pestañas General / Cuenta / Ventas / Plan.

**Demostrable:** el admin entra a `/admin/clientes`, busca «Güera» o filtra «Gracia», abre al vendedor y revisa su cuenta CoDi (CLABE enmascarada), sus ventas y el estado de su plan.

## Decisiones
- **Solo lectura.** Las acciones (regalar días, pago manual, suspender, liberar CLABE) son de la 6.2 y la 6.3.
- **Pestaña Plan: solo el resumen** (decisión del usuario): estado, último día de Pro, de gracia y de prueba, días que quedan, paquete, fundador y anual congelado. Periodos, cargos y pagos quedan como «Fase 6.2».
- **Cada pestaña es su propia ruta** (`/admin/clientes/{id}`, `/cuenta`, `/ventas`, `/plan`): funcionan sin JS, se prueban por HTTP y cargan solo lo que muestran.
- **Filtro del plan en SQL** (`CustomerListQuery.WithPlan`), equivalente a `PlanStatus.For`. Se apoya en que los periodos van encadenados, así que el instante está cubierto si y solo si es anterior a `Subscriptions.ProUntil`:
  - sin suscripción ⇒ sin prueba;
  - antes de `ProUntil`: prueba si solo la prueba cubre el instante y no hay un pago por delante; si no, Pro;
  - después: gracia si `LastPeriodKind = Paid` y `ProUntil > MexicoTime.AddDays(now, -GraceDays)`; si no, gratuito.

  El plan que se **muestra** en cada fila se calcula exacto con `PlanStatus.For` cargando los periodos de la página. Una prueba de integración compara los dos para todos los estados, con casos borde: prueba con premio encadenado, prueba con pago por delante, regalo del admin, gracia recién vencida y premio vencido.
- **Vencimiento** = `ProUntil` (prueba, pago o regalo): «Próximos 7 días», «Próximos 30 días» o «Ya venció». Con este filtro la lista se ordena por vencimiento; sin él, por fecha de registro (más reciente primero).
- **Búsqueda** por nombre de la persona, negocio, correo (contiene) o número de cliente exacto. Hasta 100 caracteres.
- **Datos sensibles:** la CLABE solo con `Clabe.Mask`, el celular de un push solo con `PhoneNumber.Mask`. Sí se muestra el id de la cuenta en Pey (sirve para soporte).
- El menú del admin marca la opción activa por controlador (Clientes queda activo en todas sus pantallas).

## Qué se hizo
- **Web / Admin:**
  - `Controllers/CustomersController` (`/admin/clientes`, `/resultados`, `/{id}`, `/{id}/cuenta`, `/{id}/ventas`, `/{id}/plan`); id inexistente ⇒ 404. Se quitó `Customers()` de `HomeController` y su `_ComingSoon`.
  - `Services/CustomerListQuery` (estático, como `SalesHistoryQuery`) y `Services/CustomerDetailQuery` (scoped, registrado en `Program.cs`; usa `IPlanAccess` e `IChargeUsage`).
  - `Models/CustomerViewModels.cs`: `CustomerUrls`, `AdminLabels` (textos e insignias de estatus, plan, CoDi y tipo de titular), filtros en la URL en español (`estatus=sin-cuenta|en-revision|activos|suspendidos|de-baja`, `plan=sin-prueba|prueba|pro|gracia|gratuito`, `vence=7|30|vencido`, `desde`, `hasta`, `pagina`) y los modelos de cada pestaña.
  - Vistas `Customers/Index`, `_CustomerResults`, `_CustomerHeader` (encabezado + `nav-tabs`), `General`, `Account`, `Sales`, `Plan`. La pestaña Ventas reutiliza `SalesHistoryQuery.WithStatus`, `SaleStatusFilters`, `SaleStatusText` y `SaleFormats` del área del vendedor.
  - `_AdminLayout`: opción activa por controlador + acción.
  - `wwwroot/js/admin-customer-search.js`: busca al escribir (300 ms), al cambiar un filtro y con Enter; pide `GET /admin/clientes/resultados` y deja la URL con los filtros (atrás/adelante, paginación y «Ver todos» sin recargar). Sin JS es un GET normal.
- **Pruebas** (`tests/Local133.IntegrationTests/Admin/CustomersTests`, 13 pruebas; `AreaAccessTests` suma `/admin/clientes/1`): acceso por rol, búsqueda, cada filtro, plan contra `PlanStatus.For`, vencimiento y orden, fecha de registro en hora de México (y al revés), paginación de 20, fragmento de resultados, las cuatro pestañas, 404, CLABE completa nunca en el HTML, ventas solo del vendedor con filtro de estatus y celular enmascarado.

## Pruebas
- `dotnet build`: sin advertencias.
- `dotnet test`: 498 unitarias, 12 de la portada y 634 de integración, todas correctas. Las de integración se corrieron con `LOCAL133_TEST_DB` apuntando a LocalDB, porque se creyó que el equipo ya no tenía `localhost\SQLEXPRESS`. Sí la tiene: el servicio `MSSQL$SQLEXPRESS` (SQL Server 2022) está corriendo, se comprobó el 2026-11-23. Las pruebas pasan con la conexión por defecto (ver la segunda revisión).
- `dotnet ef migrations has-pending-model-changes`: sin cambios (corrido con `ConnectionStrings__Default` apuntando a LocalDB).

## Revisión en navegador
Hecha el 2026-11-23 con Playwright y Edge instalado, a 375, 768 y 1280 px.
- **Cómo:** el MCP no conectaba porque el equipo no tenía Node.js. Se instaló, y `.mcp.json` ahora antepone `C:\Program Files\nodejs` al `PATH`. Como la sesión no se había reiniciado, la revisión se hizo con un script de Playwright (`playwright-core` del caché de `npx`, `channel: 'msedge'`).
- **Datos:** sitio con `Jobs__Enabled=false`, `Pey__ApiKey=` y `Resend__ApiKey=` vacíos, contra la base de desarrollo (30 vendedores). Se entró con el admin sembrado y solo se leyó: no se creó ni cambió nada.
- **Resultado:** consola sin errores, ninguna respuesta ≥ 400 (salvo el 404 esperado de un cliente inexistente) y sin scroll horizontal en ninguna pantalla.
- **Revisado:**
  - listado: filtros colapsados con «Filtros» en 375 y 768, en línea en 1280;
  - búsqueda al escribir (pide `/resultados` y deja `?q=` en la URL), estado sin resultados y «Ver todos»;
  - filtro de estatus al cambiar el select, y atrás;
  - paginación;
  - «Clientes» activo en el offcanvas (375) y en la barra lateral (1280);
  - las cuatro pestañas con un vendedor sin cuenta (#38) y con uno activo con ventas (#20): pestaña activa correcta, CLABE como «•••• 5713», ventas con el resumen y el carrito.
- **Corregido durante la revisión:**
  - a 375 px la pestaña «Plan» quedaba fuera de la barra; ahora los íconos de las pestañas solo salen desde `sm` y las cuatro caben (el script falla si desbordan);
  - el placeholder del buscador se cortaba: quedó «Nombre, negocio o correo»;
  - «1 generados» ⇒ «1 generado». Este último no se volvió a ver en el navegador; es solo texto.

### Segunda revisión (2026-11-23, MCP de Playwright)
- **Pruebas:** `CustomersTests` + `AreaAccessTests`: 33 correctas antes de los cambios y 34 después (se sumó `Plan_en_gracia_no_muestra_dias_de_Pro`). Las 34 pasan con LocalDB y también con la conexión por defecto a `localhost\SQLEXPRESS`, sin `LOCAL133_TEST_DB`.
- **Navegador:** el MCP de Playwright ya conecta, con Edge, a 375, 768 y 1280 px. Mismo sitio sin Pey, Resend ni jobs, contra la base de desarrollo; solo lectura. Revisado: el listado, la búsqueda al escribir («Tacos»), el filtro de plan «Gracia» (deja `?q=Tacos&plan=gracia` sin recargar), «Vence en 30 días» ordenado por vencimiento, las cuatro pestañas de #20 (CLABE «•••• 5713», ningún número de 18 dígitos en el HTML), el filtro «Pendientes» sin resultados, el Plan de #35 (gracia) y de #34 (Pro, «Quedan 48 días») y el 404 de un cliente inexistente. Sin scroll horizontal; en la consola solo aparece el 404 provocado; ninguna otra respuesta ≥ 400. Se confirmó «1 generado» en singular.
- **Corregido:**
  - en la pestaña Plan, un vendedor en **gracia** mostraba «Días de Pro: Hoy es el último día», porque `IsProLike` incluye la gracia y ahí `DaysLeft` vale 0. Ahora la fila solo sale en prueba y Pro; en gracia ya está «Gracia hasta el …» junto a la insignia;
  - a 1280 px el select de vencimiento cortaba «En los próximos 3…», y no se distinguía 7 de 30. Ahora dice «Próximos 7 días» / «Próximos 30 días». Todavía se corta la «s» final; se ve el número.
- **Nota:** recién cargada la página, la barra de pestañas a 375 px desborda unos píxeles mientras llegan las fuentes de Google; con ellas cabe (343/343 px). Tiene `overflow-x: auto`, así que no mueve la página.

## Pendientes
- Periodos, cargos, pagos, referidos y acciones del plan: 6.2.
