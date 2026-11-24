using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Local133.Web.Areas.Merchant.Models;

namespace Local133.Web.Areas.Merchant.Services;

/// <summary>
/// Libro de Excel (.xlsx) del historial de ventas (5.9, Pro), con el SDK oficial de Open XML. Una hoja «Ventas» con
/// encabezado congelado y autofiltro; fechas y montos como números (con formato), para que el vendedor pueda sumar y
/// ordenar. Los textos van como <c>inlineStr</c>: nunca se interpretan como fórmulas.
/// </summary>
public static class SalesWorkbook
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string SheetName = "Ventas";

    /// <summary>Tope de ventas por archivo: el libro se arma en memoria. Con más, se pide acotar las fechas.</summary>
    public const int MaxRows = 50_000;

    public static readonly string TooManyRowsMessage =
        $"Son más de {MaxRows:N0} ventas. Acota las fechas para exportarlas a Excel.";

    /// <summary>«ventas-2026-11-23.xlsx» (hoy) o, con fechas, «ventas-2026-11-01-al-2026-11-23.xlsx».</summary>
    public static string FileName(SalesHistoryFilters filters, DateOnly today) => (filters.From, filters.To) switch
    {
        ({ } from, { } to) when from == to => $"ventas-{Iso(from)}.xlsx",
        ({ } from, { } to) => $"ventas-{Iso(from)}-al-{Iso(to)}.xlsx",
        ({ } from, null) => $"ventas-{Iso(from)}-al-{Iso(today)}.xlsx",
        (null, { } to) => $"ventas-hasta-{Iso(to)}.xlsx",
        _ => $"ventas-{Iso(today)}.xlsx",
    };

    private static string Iso(DateOnly day) => SaleFormats.IsoDate(day);

    public static readonly IReadOnlyList<string> Headers =
    [
        "Folio", "Fecha y hora", "Concepto", "Método", "Origen", "Estatus", "Monto", "Cobrado", "Pagada el", "Productos",
    ];

    private static readonly double[] ColumnWidths = [9, 18, 40, 11, 13, 12, 13, 13, 18, 50];

    // Índices de CellFormats en la hoja de estilos (ver Stylesheet()).
    private const uint HeaderStyle = 1;
    private const uint DateTimeStyle = 2;
    private const uint MoneyStyle = 3;

    // Formatos propios (los id 164 en adelante son libres).
    private const uint DateTimeFormatId = 164;
    private const uint MoneyFormatId = 165;

    public static void Write(Stream stream, IEnumerable<SalesExportRow> rows)
    {
        using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        var styles = workbookPart.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = Stylesheet();
        styles.Stylesheet.Save();

        var sheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var rowCount = WriteSheet(sheetPart, rows);

        workbookPart.Workbook.AppendChild(new Sheets(new Sheet
        {
            Id = workbookPart.GetIdOfPart(sheetPart),
            SheetId = 1,
            Name = SheetName,
        }));

        // El autofiltro de Excel necesita su nombre definido oculto (_FilterDatabase).
        workbookPart.Workbook.AppendChild(new DefinedNames(new DefinedName($"'{SheetName}'!$A$1:${LastColumn}${rowCount + 1}")
        {
            Name = "_xlnm._FilterDatabase",
            LocalSheetId = 0,
            Hidden = true,
        }));
        workbookPart.Workbook.Save();
    }

    private static string LastColumn => ColumnName(Headers.Count);

    /// <summary>Escribe la hoja con <see cref="OpenXmlWriter"/> (sin armar el árbol completo en memoria).</summary>
    private static int WriteSheet(WorksheetPart part, IEnumerable<SalesExportRow> rows)
    {
        var count = 0;
        using var writer = OpenXmlWriter.Create(part);
        writer.WriteStartElement(new Worksheet());

        // Fila 1 congelada: el encabezado se queda arriba al desplazarse.
        writer.WriteElement(new SheetViews(new SheetView(
            new Pane
            {
                VerticalSplit = 1,
                TopLeftCell = "A2",
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen,
            },
            new Selection { Pane = PaneValues.BottomLeft })
        {
            WorkbookViewId = 0,
        }));

        writer.WriteElement(new Columns(ColumnWidths.Select((width, i) => new Column
        {
            Min = (uint)(i + 1),
            Max = (uint)(i + 1),
            Width = width,
            CustomWidth = true,
        })));

        writer.WriteStartElement(new SheetData());
        WriteRow(writer, 1, Headers.Select(h => TextCell(h, HeaderStyle)));
        foreach (var row in rows)
        {
            count++;
            WriteRow(writer, count + 1,
            [
                NumberCell(row.Folio),
                DateCell(row.CreatedAt),
                TextCell(row.Concept),
                TextCell(row.Method),
                TextCell(row.Source),
                TextCell(row.Status),
                MoneyCell(row.Amount),
                MoneyCell(row.Collected),
                row.PaidAt is { } paidAt ? DateCell(paidAt) : TextCell(null),
                TextCell(row.Products),
            ]);
        }

        writer.WriteEndElement(); // sheetData

        writer.WriteElement(new AutoFilter { Reference = $"A1:{LastColumn}{count + 1}" });
        writer.WriteEndElement(); // worksheet
        return count;
    }

    private static void WriteRow(OpenXmlWriter writer, int index, IEnumerable<Cell> cells)
    {
        writer.WriteStartElement(new Row { RowIndex = (uint)index });
        var column = 0;
        foreach (var cell in cells)
        {
            column++;
            cell.CellReference = ColumnName(column) + index.ToString(CultureInfo.InvariantCulture);
            writer.WriteElement(cell);
        }

        writer.WriteEndElement();
    }

    private static Cell TextCell(string? text, uint style = 0)
    {
        var cell = new Cell { DataType = CellValues.InlineString };
        if (style != 0)
        {
            cell.StyleIndex = style;
        }

        if (!string.IsNullOrEmpty(text))
        {
            cell.InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        }

        return cell;
    }

    private static Cell NumberCell(int value) =>
        new() { DataType = CellValues.Number, CellValue = new CellValue(value) };

    private static Cell MoneyCell(decimal value) =>
        new() { DataType = CellValues.Number, CellValue = new CellValue(value), StyleIndex = MoneyStyle };

    /// <summary>Fecha como número de serie de Excel (días desde 1899-12-30), ya en hora de México.</summary>
    private static Cell DateCell(DateTime value) =>
        new()
        {
            CellValue = new CellValue(value.ToOADate().ToString("R", CultureInfo.InvariantCulture)),
            StyleIndex = DateTimeStyle,
        };

    /// <summary>1 ⇒ A, 10 ⇒ J, 27 ⇒ AA.</summary>
    private static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            var rest = (column - 1) % 26;
            name = (char)('A' + rest) + name;
            column = (column - 1) / 26;
        }

        return name;
    }

    /// <summary>
    /// Estilos mínimos: 0 normal, 1 encabezado en negritas, 2 fecha y hora, 3 moneda. Excel exige al menos dos rellenos
    /// (ninguno y gris 125), una fuente y un borde.
    /// </summary>
    private static Stylesheet Stylesheet() => new(
        new NumberingFormats(
            new NumberingFormat { NumberFormatId = DateTimeFormatId, FormatCode = "dd/mm/yyyy hh:mm" },
            new NumberingFormat { NumberFormatId = MoneyFormatId, FormatCode = "\"$\"#,##0.00" })
        {
            Count = 2,
        },
        new Fonts(
            new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }))
        {
            Count = 2,
        },
        new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
        {
            Count = 2,
        },
        new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder()))
        {
            Count = 1,
        },
        new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
        new CellFormats(
            new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
            new CellFormat { NumberFormatId = 0, FontId = 1, FillId = 0, BorderId = 0, FormatId = 0, ApplyFont = true },
            new CellFormat { NumberFormatId = DateTimeFormatId, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = MoneyFormatId, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true })
        {
            Count = 4,
        },
        new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
}
