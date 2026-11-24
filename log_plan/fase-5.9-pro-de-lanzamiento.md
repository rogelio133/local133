# Fase 5.9 — Pro de lanzamiento

**Estado:** ✅ hecho 2026-11-23 · [← Plan](../PLAN_DESARROLLO.md#fase-5--suscripciones-56-semanas) · [diseño](../actualizaciones/suscripciones_2026_11_18.md#59-pro-de-lanzamiento)

## Alcance (del plan)
Historial completo con búsqueda y exportación a Excel, y corte de caja diario. Son las funciones de Pro «al lanzar»; el rango de fechas y el recorte a 30 días del gratuito ya existían desde la 5.5.

**Demostrable:** un vendedor en prueba o Pro busca «platano» en Ventas y encuentra «Licuado de plátano», descarga ese resultado en Excel y abre «Corte de caja»: ve lo cobrado hoy por QR y al celular, el ticket promedio, los productos más vendidos y lo que quedó sin pagar, y lo manda por WhatsApp o lo imprime. Un vendedor en el gratuito ve el aviso de Pro en las tres funciones.

## Decisiones
- **Excel con `DocumentFormat.OpenXml`** (decisión del usuario): es el SDK oficial de Microsoft (MIT), está firmado (Smart App Control no lo bloquea) y no trae más dependencias. No se usa ClosedXML, que arrastra SixLabors.Fonts y DLL sin firma, ni CSV. Versión 3.5.1, sin vulnerabilidades conocidas (`dotnet list package --vulnerable`).
- **Búsqueda solo en el concepto del vendedor**, no en el nombre del negocio que va antes («Tacos Don Ramón · 2 tacos»): si no, «tacos» traería todas las ventas. Como ni `BusinessName` ni el concepto admiten `·`, el separador aparece a lo más una vez y se busca con `LIKE '% · %texto%'`, o en todo el concepto si no tiene separador (ventas de antes de la 4.1).
  - No distingue mayúsculas ni acentos: intercalación `Latin1_General_CI_AI`.
  - Se escapan los comodines de `LIKE`.
  - «#12» busca solo el folio; «12» busca el folio **y** el concepto («Mesa 12»).
- **En el gratuito** la búsqueda se descarta en el servidor, igual que las fechas (decisión 3 del diseño). La exportación responde **403** y el corte muestra el aviso de Pro con 200, como el detalle bloqueado de la 5.5.
- **Exportación**: los mismos filtros que la lista, sin paginar, con un tope de **50,000 ventas** (el libro se arma en memoria). Si hay más, regresa a la lista con un aviso para acotar las fechas.
  - Columnas: folio, fecha y hora, concepto, método, origen, estatus efectivo, monto, cobrado (suma de pagos), pagada el y productos.
  - Las fechas van como números de Excel en hora de México y los montos como números con formato de moneda. Los textos van como `inlineStr`, que nunca se interpreta como fórmula.
  - Nunca lleva celular, CLABE ni ids de Pey.
- **Corte de caja por día del pago**: lo cobrado sale de los pagos recibidos ese día en México (`DaySummaryQuery.PaymentsOn`, el mismo cálculo del resumen de Ventas), así que una venta creada ayer y pagada hoy cuenta hoy. Una venta con dos pagos el mismo día cuenta una vez. Pendientes y vencidas son las ventas **creadas** ese día, con el estatus efectivo.
  - Desglose por método (QR, Celular; «QR fijo» solo si hubo) y por origen (Monto, Productos; «Monto libre» solo si hubo).
  - Productos agrupados por el nombre guardado en la venta.
  - Fecha futura o ilegible ⇒ hoy. Se aceptan fechas desde 2020, también en los filtros del historial, para evitar fechas como el año 1.
  - Se comparte como **texto** por WhatsApp, sin liga pública: el corte es privado.
- «Por vendedor» queda para cuando existan vendedores adicionales (fuera de esta implementación).

## Qué se hizo
- **Paquete**: `DocumentFormat.OpenXml` 3.5.1 en `Directory.Packages.props` y `Local133.Web`.
- **Web / Merchant**:
  - `Services/SalesHistoryQuery`: `Filter(...)` (recorte del plan, fechas, estatus y búsqueda), que usan la lista y la exportación; `SalesHistoryFilters`, `FilteredSales`, `EffectiveStatus`; `SaleSearch` (normaliza, folio, `LIKE` con escape); `DaySummaryQuery.PaymentsOn`.
  - `Services/SalesWorkbook`: libro con hoja «Ventas», encabezado en negritas y congelado, autofiltro, anchos fijos y formatos `dd/mm/yyyy hh:mm` y `"$"#,##0.00`. La hoja se escribe con `OpenXmlWriter`. También `MaxRows` y `FileName` (`ventas-2026-11-23.xlsx` o `ventas-2026-11-01-al-2026-11-23.xlsx`).
  - `Services/CashCutQuery` y, en `SalesViewModels.cs`, `CashCutViewModel`, `CashCutBreakdownRow`, `CashCutProductRow`, `CashCutOpenSales`, `SaleChannelText` y `SalesExportRow`. `SalesUrls.Export`, `CashCut` y `CashCutFor`. `SalesHistoryViewModel` suma `Query`, `CanExport`, `HasCashCut`, `ExportUrl` y la búsqueda en `UrlFor`.
  - `SalesController`: `?q=` en la lista, `GET /app/ventas/exportar` y `GET /app/ventas/corte?fecha=`.
  - Vistas: `Sales/Index` (botón «Corte de caja» con la insignia «Pro» en el gratuito, aviso `data-sales-notice`, buscador y «Exportar a Excel»), `Sales/CashCut`, `_CashCutBreakdown` y `CashCutLocked`.
  - `_MerchantLayout`: las barras y el banner de gracia llevan `d-print-none`. `site.scss`: estilos de impresión del corte y `.cash-cut-day`.
  - `wwwroot/js/receipt-share.js`: `data-copied-text` (el corte dice «Resumen copiado.»).
  - «Mi plan»: el historial Pro dice «Completo, con fechas y búsqueda», y hay filas nuevas «Exportar ventas a Excel» y «Corte de caja diario».
- **Pruebas**:
  - `SaleFlowTestBase`: `InsertSaleAsync` (ahora también push y venta con productos) y `AddPaymentAsync`, movidos desde `SalesHistoryTests`;
  - `SalesHistoryTests`: búsqueda por concepto sin acentos ni mayúsculas y no por el negocio, ventas sin negocio, comodines, folio con y sin `#`, búsqueda con estatus y fechas, la liga de exportación con los filtros, y el gratuito sin búsqueda, exportación ni corte;
  - `SalesExportTests` (nueva): descarga, nombre, `no-store`, orden, columnas, tipos, aislamiento entre vendedores, filtros, 403 en el gratuito y login sin sesión;
  - `SalesWorkbookTests` (nueva, **sin base: corre en CI**): libro válido según `OpenXmlValidator`, panel congelado, autofiltro, estilos, fechas, montos, texto con `=` que no es fórmula, libro vacío y nombre del archivo;
  - `CashCutTests` (nueva): totales, ticket, desglose, productos, dos pagos de una venta, venta de ayer pagada hoy, pendientes y vencidas, canceladas y otro vendedor fuera, igual al resumen de Ventas, mensaje de WhatsApp, pago a las 23:30 de México, flechas, fechas futuras o ilegibles, gratuito y sin sesión.

## Pruebas
- `dotnet build`: sin advertencias.
- `dotnet test`: 593 unitarias, 12 de la portada y 713 de integración, todas correctas.
- `dotnet ef migrations has-pending-model-changes`: sin cambios (no hay migración).

## Revisión en navegador
Hecha el 2026-11-23 con el MCP de Playwright y Edge, a 375, 768 y 1280 px.
- **Datos:** sitio con `Jobs__Enabled=false`, `Pey__ApiKey=` y `Resend__ApiKey=` vacíos. Se registraron `pro59@prueba.mx` y `free59@prueba.mx` en `/Identity/Account/Register`. Por SQL se confirmó su correo, quedaron activos con cuenta bancaria (CLABE de prueba, sin llamar a Pey), uno en prueba y otro con la prueba vencida, y se les sembraron ventas: QR, celular, carrito, pendiente, vencida y cancelada, de hoy, de ayer y de hace tres días.
- **Revisado:**
  - Ventas en Pro: buscador; «jugo verde» (2 ventas); «PLATANO» encuentra «Licuado de plátano»; «Quitar búsqueda» y «Ver todas»; «Exportar a Excel» con los filtros (`/app/ventas/exportar?q=jugo%20verde`), que descarga el `.xlsx` con 200, el tipo y el nombre correctos;
  - corte en Pro: totales ($222.50, 4 ventas, $55.63), desglose, productos y sin pagar; flecha al día anterior (`?fecha=2026-11-22`, $120.00) y de regreso; mensaje de WhatsApp (en escritorio cambia a `whatsapp://`); «Copiar resumen» ⇒ «Resumen copiado.»; vista de impresión sin barras ni botones;
  - gratuito: sin buscador ni exportación; aviso «Consulta todo tu historial, filtra por fecha, busca y exporta a Excel con Pro»; insignia «Pro» en «Corte de caja»; el corte muestra «Disponible con Pro»; `/app/ventas/exportar` ⇒ 403;
  - «Mi plan» con las filas nuevas;
  - pestaña «Ventas» activa en todas las pantallas, sin scroll horizontal, la barra inferior no tapa nada, consola sin errores y sin recursos 404. El único error de consola es el 403 provocado a propósito.
- **Corregido durante la revisión:**
  - a 375 px el selector de día del corte desbordaba (402 px) y después recortaba la fecha. El campo ahora puede encogerse (`.cash-cut-day { min-width: 0 }`) y las flechas y «Ver» son más angostos;
  - «Sin pagar · ventas creadas este día» se partía mal: la aclaración quedó en su propio renglón.

## Pendientes
- Abrir el `.xlsx` en Excel de escritorio, en Excel o Google Sheets del celular y en Numbers. Los formatos y el autofiltro se validaron con `OpenXmlValidator`, no en esas aplicaciones.
- Imprimir el corte en papel, o guardarlo como PDF, desde Android, iPhone y Windows.
- Mandar el corte por WhatsApp real desde un celular (Android e iPhone) y desde la app de escritorio.
