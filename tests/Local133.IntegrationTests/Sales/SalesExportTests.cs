using System.Globalization;
using System.Net;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Local133.Domain.Common;
using Local133.Domain.Sales;
using Local133.IntegrationTests.Infrastructure;
using Local133.Web.Areas.Merchant.Services;

namespace Local133.IntegrationTests.Sales;

/// <summary>5.9: exportación del historial a Excel (/app/ventas/exportar), con los mismos filtros que la lista.</summary>
public class SalesExportTests(Local133WebFactory factory) : SaleFlowTestBase(factory)
{
    private const string Url = "/app/ventas/exportar";

    [Fact]
    public async Task Pro_descarga_un_xlsx_con_sus_ventas_de_la_mas_reciente_a_la_mas_antigua()
    {
        var client = await ActiveMerchantAsync(Email);
        await ActiveMerchantAsync(OtherEmail);
        var now = DateTimeOffset.UtcNow;
        var direct = await InsertSaleAsync(Email, 150m, now.AddMinutes(-30), expiresAt: now.AddMinutes(5), concept: "Tacos Don Ramón · Refresco");
        var cart = await InsertSaleAsync(Email, 0m, now.AddMinutes(-10), concept: "Tacos Don Ramón · Pedido",
            phone: "5512345678", items: [("Taco al pastor", 20m, 2), ("Refresco", 15m, 1)]);
        await AddPaymentAsync(cart, 55m, now.AddMinutes(-9));
        await InsertSaleAsync(OtherEmail, 999m, now.AddMinutes(-5));

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SalesWorkbook.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            $"ventas-{MexicoTime.Today(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.xlsx",
            response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        Assert.True(response.Headers.CacheControl?.NoStore);

        var rows = ReadSheet(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(SalesWorkbook.Headers, rows[0]);
        Assert.Equal(3, rows.Count); // encabezado + 2 (la del otro vendedor no sale)

        var newest = rows[1];
        Assert.Equal(cart.Folio.ToString(CultureInfo.InvariantCulture), newest[0]);
        Assert.Equal("Pedido", newest[2]);
        Assert.Equal("Celular", newest[3]);
        Assert.Equal("Productos", newest[4]);
        Assert.Equal("Pagada", newest[5]);
        Assert.Equal(55m, Money(newest[6]));
        Assert.Equal(55m, Money(newest[7]));
        Assert.NotEqual(string.Empty, newest[8]);
        Assert.Equal("2 × Taco al pastor; 1 × Refresco", newest[9]);

        var oldest = rows[2];
        Assert.Equal(direct.Folio.ToString(CultureInfo.InvariantCulture), oldest[0]);
        Assert.Equal("Refresco", oldest[2]);
        Assert.Equal("QR", oldest[3]);
        Assert.Equal("Monto", oldest[4]);
        Assert.Equal("Pendiente", oldest[5]);
        Assert.Equal(150m, Money(oldest[6]));
        Assert.Equal(0m, Money(oldest[7]));
        Assert.Equal(string.Empty, oldest[8]);

        // La fecha es un número de Excel en hora de México.
        var created = DateTime.FromOADate(double.Parse(oldest[1], CultureInfo.InvariantCulture));
        Assert.True(Math.Abs((created - MexicoTime.ToLocal(direct.CreatedAt).DateTime).TotalSeconds) < 1);
    }

    [Fact]
    public async Task Respeta_los_filtros_de_la_lista()
    {
        var client = await ActiveMerchantAsync(Email);
        var now = DateTimeOffset.UtcNow;
        var today = MexicoTime.Today(now);
        await InsertSaleAsync(Email, 10m, now.AddMinutes(-30), concept: "Tacos Don Ramón · Refresco");
        var paid = await InsertSaleAsync(Email, 20m, now.AddMinutes(-20), concept: "Tacos Don Ramón · Refresco grande");
        await AddPaymentAsync(paid, 20m, now.AddMinutes(-19));
        var otherDay = await InsertSaleAsync(Email, 30m, MexicoTime.DayRange(today.AddDays(-3)).StartUtc.AddHours(12),
            concept: "Tacos Don Ramón · Refresco");
        await AddPaymentAsync(otherDay, 30m, otherDay.CreatedAt.AddMinutes(1));
        var iso = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var response = await client.GetAsync($"{Url}?estado=pagadas&q=refresco&desde={iso}&hasta={iso}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"ventas-{iso}.xlsx",
            response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        var rows = ReadSheet(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(2, rows.Count);
        Assert.Equal(paid.Folio.ToString(CultureInfo.InvariantCulture), rows[1][0]);
    }

    [Fact]
    public async Task Basico_y_vencido_reciben_403()
    {
        var client = await ActiveMerchantAsync(Email);
        await SeedBasicPlanAsync(Email);
        await InsertSaleAsync(Email, 10m, DateTimeOffset.UtcNow.AddMinutes(-5));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);

        await SeedExpiredPlanAsync(Email);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_redirige_al_login()
    {
        var response = await CreateClient().GetAsync(Url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", Location(response));
    }

    private static decimal Money(string cell) => decimal.Parse(cell, CultureInfo.InvariantCulture);

    /// <summary>Celdas de la hoja como texto (vacío si no hay valor), renglón por renglón.</summary>
    internal static List<List<string>> ReadSheet(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbook = document.WorkbookPart!;
        var sheet = workbook.Workbook!.Sheets!.Elements<Sheet>().Single();
        Assert.Equal(SalesWorkbook.SheetName, sheet.Name?.Value);
        var part = (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
        return part.Worksheet!.Descendants<Row>()
            .Select(row => row.Elements<Cell>()
                .Select(cell => cell.DataType?.Value == CellValues.InlineString
                    ? cell.InlineString?.Text?.Text ?? string.Empty
                    : cell.CellValue?.Text ?? string.Empty)
                .ToList())
            .ToList();
    }
}
