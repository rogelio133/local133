using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using Local133.Web.Areas.Merchant.Models;
using Local133.Web.Areas.Merchant.Services;

namespace Local133.IntegrationTests.Sales;

/// <summary>5.9: libro de Excel del historial. Sin base de datos: corre en CI.</summary>
public class SalesWorkbookTests
{
    private static readonly SalesExportRow Paid = new(
        12, new DateTime(2026, 11, 23, 15, 45, 0), "2 tacos al pastor", "QR", "Productos", "Pagada", 150.5m, 150.5m,
        new DateTime(2026, 11, 23, 15, 46, 0), "2 × Taco al pastor");

    private static readonly SalesExportRow Pending = new(
        11, new DateTime(2026, 11, 22, 9, 0, 0), "=HYPERLINK(\"x\")", "Celular", "Monto", "Pendiente", 20m, 0m, null, null);

    [Fact]
    public void El_libro_es_valido_y_tiene_encabezado_congelado_y_autofiltro()
    {
        using var document = Open(Write(Paid, Pending));

        var errors = new OpenXmlValidator().Validate(document).Select(e => e.Description).ToList();
        Assert.Empty(errors);

        var sheet = WorksheetOf(document);
        var pane = sheet.Descendants<Pane>().Single();
        Assert.Equal("A2", pane.TopLeftCell?.Value);
        Assert.Equal(PaneStateValues.Frozen, pane.State?.Value);
        Assert.Equal("A1:J3", sheet.Descendants<AutoFilter>().Single().Reference?.Value);
        Assert.Contains(document.WorkbookPart!.Workbook!.Descendants<DefinedName>(), n => n.Name == "_xlnm._FilterDatabase");
    }

    [Fact]
    public void Fechas_y_montos_son_numeros_con_formato_y_los_textos_nunca_son_formulas()
    {
        using var document = Open(Write(Paid, Pending));
        var rows = WorksheetOf(document).Descendants<Row>().ToList();

        var header = rows[0].Elements<Cell>().ToList();
        Assert.All(header, c => Assert.Equal(1u, c.StyleIndex?.Value));
        Assert.Equal("Folio", header[0].InlineString?.Text?.Text);

        var paid = rows[1].Elements<Cell>().ToList();
        Assert.Equal("A2", paid[0].CellReference?.Value);
        Assert.Equal("12", paid[0].CellValue?.Text);
        Assert.Equal(2u, paid[1].StyleIndex?.Value);
        Assert.Equal(Paid.CreatedAt, DateTime.FromOADate(double.Parse(paid[1].CellValue!.Text, CultureInfo.InvariantCulture)));
        Assert.Equal(3u, paid[6].StyleIndex?.Value);
        Assert.Equal("150.5", paid[6].CellValue?.Text);
        Assert.Equal("2 × Taco al pastor", paid[9].InlineString?.Text?.Text);

        var pending = rows[2].Elements<Cell>().ToList();
        Assert.Equal(CellValues.InlineString, pending[2].DataType?.Value);
        Assert.Null(pending[2].CellFormula);
        Assert.Equal("=HYPERLINK(\"x\")", pending[2].InlineString?.Text?.Text);
        Assert.Null(pending[8].CellValue); // sin pago
        Assert.Equal("J3", pending[9].CellReference?.Value);

        var formats = document.WorkbookPart!.WorkbookStylesPart!.Stylesheet!.NumberingFormats!.Elements<NumberingFormat>()
            .Select(f => f.FormatCode?.Value).ToList();
        Assert.Contains("dd/mm/yyyy hh:mm", formats);
        Assert.Contains("\"$\"#,##0.00", formats);
    }

    [Fact]
    public void Sin_ventas_queda_solo_el_encabezado()
    {
        using var document = Open(Write());

        Assert.Single(WorksheetOf(document).Descendants<Row>());
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Fact]
    public void Nombre_del_archivo_segun_las_fechas()
    {
        var today = new DateOnly(2026, 11, 23);
        Assert.Equal("ventas-2026-11-23.xlsx", SalesWorkbook.FileName(new SalesHistoryFilters(), today));
        Assert.Equal("ventas-2026-11-01-al-2026-11-23.xlsx",
            SalesWorkbook.FileName(new SalesHistoryFilters(From: new DateOnly(2026, 11, 1), To: today), today));
        Assert.Equal("ventas-2026-11-01-al-2026-11-23.xlsx",
            SalesWorkbook.FileName(new SalesHistoryFilters(From: new DateOnly(2026, 11, 1)), today));
        Assert.Equal("ventas-hasta-2026-11-10.xlsx",
            SalesWorkbook.FileName(new SalesHistoryFilters(To: new DateOnly(2026, 11, 10)), today));
    }

    private static byte[] Write(params SalesExportRow[] rows)
    {
        using var stream = new MemoryStream();
        SalesWorkbook.Write(stream, rows);
        return stream.ToArray();
    }

    private static SpreadsheetDocument Open(byte[] bytes) => SpreadsheetDocument.Open(new MemoryStream(bytes), false);

    private static Worksheet WorksheetOf(SpreadsheetDocument document)
    {
        var workbook = document.WorkbookPart!;
        var sheet = workbook.Workbook!.Sheets!.Elements<Sheet>().Single();
        return ((WorksheetPart)workbook.GetPartById(sheet.Id!.Value!)).Worksheet!;
    }
}
