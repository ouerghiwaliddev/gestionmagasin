using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using GestionMagasin.Api.Domain;

namespace GestionMagasin.Api.Services;

/// <summary>Tableau d'un rapport. Formats : null = texte/entier, "0.00" ou "0.000" pour les décimaux.</summary>
public record ExportSection(string Title, string[] Headers, List<object?[]> Rows, string?[]? Formats = null);

public static class ExportService
{
    public const string ExcelMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string PdfMime = "application/pdf";

    private static string Text(object? value, string? format) => value switch
    {
        null => "",
        decimal d => d.ToString(format == "0.000" ? "F3" : "F2", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    // ---------------- Excel ----------------

    public static byte[] BuildExcel(IEnumerable<ExportSection> sections)
    {
        using var workbook = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in sections)
        {
            var baseTitle = new string(section.Title.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
            baseTitle = baseTitle.Length > 31 ? baseTitle[..31] : baseTitle;
            if (baseTitle.Length == 0) baseTitle = "Données";
            var title = baseTitle;
            for (var suffix = 2; !used.Add(title); suffix++)
                title = $"{(baseTitle.Length > 27 ? baseTitle[..27] : baseTitle)} {suffix}";

            var sheet = workbook.Worksheets.Add(title);
            for (var c = 0; c < section.Headers.Length; c++)
                sheet.Cell(1, c + 1).Value = section.Headers[c];

            var header = sheet.Range(1, 1, 1, section.Headers.Length);
            header.Style.Font.Bold = true;
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1C3F93");
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            for (var r = 0; r < section.Rows.Count; r++)
            {
                var row = section.Rows[r];
                for (var c = 0; c < row.Length; c++)
                {
                    var cell = sheet.Cell(r + 2, c + 1);
                    var format = section.Formats is { } f && c < f.Length ? f[c] : null;
                    switch (row[c])
                    {
                        case null: break;
                        case decimal d:
                            cell.Value = d;
                            cell.Style.NumberFormat.Format = format ?? "0.00";
                            break;
                        case int i: cell.Value = i; break;
                        case DateTime dt: cell.Value = dt.ToLocalTime(); cell.Style.DateFormat.Format = "dd/MM/yyyy HH:mm"; break;
                        default: cell.Value = Text(row[c], format); break;
                    }
                }
            }

            sheet.SheetView.FreezeRows(1);
            if (section.Rows.Count > 0) sheet.Range(1, 1, section.Rows.Count + 1, section.Headers.Length).SetAutoFilter();
            sheet.Columns().AdjustToContents(1, Math.Min(section.Rows.Count + 1, 200));
            foreach (var column in sheet.ColumnsUsed())
                column.Width = Math.Clamp(column.Width + 2, 12, 45);
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    // ---------------- PDF (généré sans dépendance, comme l'ancienne version) ----------------

    private static readonly Encoding Cp1252 = CreateCp1252();

    private static Encoding CreateCp1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
    }

    /// <summary>Texte → octets cp1252 vus comme des caractères Latin-1, avec échappement PDF.</summary>
    private static string PdfEscape(string value)
    {
        var latin = Encoding.Latin1.GetString(Cp1252.GetBytes(value));
        return latin.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static double[] ColumnWidths(string[] headers, List<string[]> rows, double available)
    {
        var weights = new double[headers.Length];
        for (var i = 0; i < headers.Length; i++)
        {
            var max = headers[i].Length;
            foreach (var row in rows.Take(100)) if (i < row.Length) max = Math.Max(max, row[i].Length);
            weights[i] = Math.Min(Math.Max(max, 8), 28);
        }
        var total = weights.Sum();
        return weights.Select(w => available * w / (total == 0 ? 1 : total)).ToArray();
    }

    private static string Num(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    private static string TablePage(string title, string[] headers, List<string[]> rows, int pageNumber, bool continued)
    {
        const int margin = 30, tableWidth = 782, top = 540, rowHeight = 15;
        var fontSize = headers.Length <= 8 ? 7 : 6;
        var widths = ColumnWidths(headers, rows, tableWidth);
        var cmd = new StringBuilder();
        cmd.AppendLine("0.12 0.20 0.35 rg");
        cmd.AppendLine($"BT /F2 16 Tf {margin} 565 Td ({PdfEscape(title)}) Tj ET");
        cmd.AppendLine("0.35 0.40 0.48 rg");
        cmd.AppendLine($"BT /F1 8 Tf {margin} 550 Td (Page {pageNumber}{(continued ? " - suite" : "")}) Tj ET");

        void DrawRow(string[] values, int y, bool isHeader)
        {
            double x = margin;
            string font;
            if (isHeader)
            {
                cmd.AppendLine($"0.11 0.25 0.58 rg {margin} {y} {tableWidth} {rowHeight} re f");
                cmd.AppendLine("1 1 1 rg");
                font = "F2";
            }
            else
            {
                cmd.AppendLine("0.15 0.18 0.22 rg");
                font = "F1";
            }
            for (var i = 0; i < values.Length && i < widths.Length; i++)
            {
                var maxChars = Math.Max(3, (int)((widths[i] - 6) / (fontSize * 0.52)));
                var shown = values[i];
                if (shown.Length > maxChars) shown = shown[..Math.Max(1, maxChars - 3)] + "...";
                cmd.AppendLine($"BT /{font} {fontSize} Tf {Num(x + 3)} {Num(y + 4)} Td ({PdfEscape(shown)}) Tj ET");
                cmd.AppendLine($"0.72 0.76 0.82 RG {Num(x)} {Num(y)} {Num(widths[i])} {rowHeight} re S");
                x += widths[i];
            }
        }

        DrawRow(headers, top, true);
        var yy = top - rowHeight;
        if (rows.Count > 0)
            foreach (var row in rows) { DrawRow(row, yy, false); yy -= rowHeight; }
        else
            cmd.AppendLine($"0.35 0.40 0.48 rg BT /F1 10 Tf {margin} {yy} Td (Aucune donnée) Tj ET");
        return cmd.ToString();
    }

    public static byte[] BuildPdf(IEnumerable<ExportSection> sections)
    {
        const int rowsPerPage = 32;
        var streams = new List<byte[]>();
        var pageNumber = 1;
        foreach (var section in sections)
        {
            var rows = section.Rows
                .Select(r => r.Select((v, i) => Text(v, section.Formats is { } f && i < f.Length ? f[i] : null)).ToArray())
                .ToList();
            var chunks = rows.Chunk(rowsPerPage).Select(c => c.ToList()).ToList();
            if (chunks.Count == 0) chunks.Add([]);
            for (var i = 0; i < chunks.Count; i++)
            {
                streams.Add(Encoding.Latin1.GetBytes(TablePage(section.Title, section.Headers, chunks[i], pageNumber, i > 0)));
                pageNumber++;
            }
        }

        var pageIds = Enumerable.Range(0, streams.Count).Select(i => 5 + i * 2).ToList();
        var objects = new SortedDictionary<int, byte[]>
        {
            [1] = Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = Encoding.ASCII.GetBytes($"<< /Type /Pages /Count {pageIds.Count} /Kids [{string.Join(' ', pageIds.Select(id => $"{id} 0 R"))}] >>"),
            [3] = Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
            [4] = Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"),
        };
        for (var i = 0; i < streams.Count; i++)
        {
            var pageId = pageIds[i];
            var contentId = pageId + 1;
            objects[pageId] = Encoding.ASCII.GetBytes(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>");
            using var content = new MemoryStream();
            content.Write(Encoding.ASCII.GetBytes($"<< /Length {streams[i].Length} >>\nstream\n"));
            content.Write(streams[i]);
            content.Write(Encoding.ASCII.GetBytes("\nendstream"));
            objects[contentId] = content.ToArray();
        }

        using var output = new MemoryStream();
        output.Write(Encoding.Latin1.GetBytes("%PDF-1.4\n%âãÏÓ\n"));
        var offsets = new List<long> { 0 };
        for (var id = 1; id <= objects.Keys.Max(); id++)
        {
            offsets.Add(output.Position);
            output.Write(Encoding.ASCII.GetBytes($"{id} 0 obj\n"));
            output.Write(objects[id]);
            output.Write(Encoding.ASCII.GetBytes("\nendobj\n"));
        }
        var xref = output.Position;
        var tail = new StringBuilder();
        tail.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) tail.Append($"{offset:D10} 00000 n \n");
        tail.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        output.Write(Encoding.ASCII.GetBytes(tail.ToString()));
        return output.ToArray();
    }
}
