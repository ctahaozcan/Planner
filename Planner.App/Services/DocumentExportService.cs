using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Planner.Core.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using S = DocumentFormat.OpenXml.Spreadsheet;
using WordDoc = DocumentFormat.OpenXml.Wordprocessing.Document;
using WordTable = DocumentFormat.OpenXml.Wordprocessing.Table;
using WordTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;
using WordTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using WordParagraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using WordRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace Planner.App.Services;

public sealed class DocumentExportService
{
    static DocumentExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public void ExportWord(string path, string title, string plainText)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new WordDoc(new Body());
        var body = main.Document.Body!;
        body.Append(Para(title, bold: true));
        foreach (var line in SplitLines(plainText))
        {
            body.Append(Para(line));
        }

        main.Document.Save();
    }

    public void ExportWordTable(string path, string title, TableDocument table)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new WordDoc(new Body());
        var body = main.Document.Body!;
        body.Append(Para(title, bold: true));
        foreach (var sheet in table.GetSheets())
        {
            body.Append(Para(sheet.Name, bold: true));
            body.Append(WordGrid(sheet));
        }

        main.Document.Save();
    }

    public void ExportExcel(string path, string title, TableDocument table)
    {
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var wb = doc.AddWorkbookPart();
        wb.Workbook = new S.Workbook();
        var sheetsEl = wb.Workbook.AppendChild(new S.Sheets());
        uint sheetId = 1;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in table.GetSheets())
        {
            var wsPart = wb.AddNewPart<WorksheetPart>();
            var sheetData = new S.SheetData();
            uint r = 1;
            foreach (var row in UsedRows(sheet))
            {
                sheetData.Append(SheetLine(r++, row));
            }

            if (r == 1)
            {
                sheetData.Append(SheetLine(1, [""]));
            }

            wsPart.Worksheet = new S.Worksheet(sheetData);
            var name = ExcelSheetName(sheet.Name, used, title, (int)sheetId);
            sheetsEl.Append(new S.Sheet
            {
                Id = wb.GetIdOfPart(wsPart),
                SheetId = sheetId++,
                Name = name
            });
        }

        wb.Workbook.Save();
    }

    public void ExportExcelFromText(string path, string title, string plainText)
    {
        var table = new TableDocument
        {
            Headers = ["Metin"],
            Rows = SplitLines(plainText).Select(l => new List<string> { l }).ToList()
        };
        ExportExcel(path, title, table);
    }

    public void ExportPdf(string path, string title, string plainText)
    {
        QuestPDF.Fluent.Document.Create(c =>
        {
            c.Page(p =>
            {
                p.Margin(40);
                p.Size(PageSizes.A4);
                p.Header().Text(title).FontSize(18).SemiBold();
                p.Content().PaddingTop(16).Text(plainText).FontSize(11);
                p.Footer().AlignRight().Text(x => x.CurrentPageNumber());
            });
        }).GeneratePdf(path);
    }

    public void ExportPdfTable(string path, string title, TableDocument table)
    {
        var sheets = table.GetSheets();
        QuestPDF.Fluent.Document.Create(c =>
        {
            foreach (var sheet in sheets)
            {
                var (headers, eval) = UsedRange(sheet, evaluate: true);
                c.Page(p =>
                {
                    p.Margin(28);
                    p.Size(PageSizes.A4.Landscape());
                    p.Header().Text($"{title} — {sheet.Name}").FontSize(16).SemiBold();
                    p.Content().PaddingTop(12).Table(t =>
                    {
                        t.ColumnsDefinition(d =>
                        {
                            d.ConstantColumn(28);
                            foreach (var _ in headers)
                            {
                                d.RelativeColumn();
                            }
                        });
                        t.Header(h =>
                        {
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("").FontSize(8);
                            foreach (var header in headers)
                            {
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(header).SemiBold().FontSize(9);
                            }
                        });
                        for (var r = 0; r < eval.Count; r++)
                        {
                            t.Cell().Background(Colors.Grey.Lighten4).Padding(3).Text((r + 1).ToString()).FontSize(8);
                            for (var i = 0; i < headers.Count; i++)
                            {
                                var value = i < eval[r].Count ? eval[r][i] : "";
                                t.Cell().BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(value).FontSize(8);
                            }
                        }
                    });
                    p.Footer().AlignRight().Text(x => x.CurrentPageNumber());
                });
            }
        }).GeneratePdf(path);
    }

    private static WordTable WordGrid(TableSheet sheet)
    {
        var (headers, eval) = UsedRange(sheet, evaluate: true);
        var wt = new WordTable();
        wt.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));
        var header = new List<string> { "" };
        header.AddRange(headers);
        wt.Append(TableRow(header, bold: true));
        for (var r = 0; r < eval.Count; r++)
        {
            var line = new List<string> { (r + 1).ToString() };
            line.AddRange(eval[r]);
            wt.Append(TableRow(line, bold: false));
        }

        return wt;
    }

    private static WordParagraph Para(string text, bool bold = false)
    {
        var run = new WordRun(new WordText(text ?? "") { Space = SpaceProcessingModeValues.Preserve });
        if (bold)
        {
            run.RunProperties = new RunProperties(new Bold());
        }

        return new WordParagraph(run);
    }

    private static WordTableRow TableRow(IReadOnlyList<string> cells, bool bold)
    {
        var row = new WordTableRow();
        foreach (var cell in cells)
        {
            var run = new WordRun(new WordText(cell ?? ""));
            if (bold)
            {
                run.RunProperties = new RunProperties(new Bold());
            }

            row.Append(new WordTableCell(new WordParagraph(run)));
        }

        return row;
    }

    private static S.Row SheetLine(uint index, IReadOnlyList<string> cells)
    {
        var row = new S.Row { RowIndex = index };
        for (var i = 0; i < cells.Count; i++)
        {
            row.Append(SheetCell(i, index, cells[i]));
        }

        return row;
    }

    private static S.Cell SheetCell(int col, uint row, string? raw)
    {
        var text = raw ?? "";
        var cell = new S.Cell { CellReference = $"{TableDocument.ColumnName(col)}{row}" };
        if (text.StartsWith('=') && text.Length > 1)
        {
            cell.CellFormula = new S.CellFormula(text[1..]);
            return cell;
        }

        if (LooksLikeNumber(text, out var n))
        {
            cell.CellValue = new S.CellValue(n.ToString(CultureInfo.InvariantCulture));
            cell.DataType = S.CellValues.Number;
            return cell;
        }

        cell.DataType = S.CellValues.String;
        cell.CellValue = new S.CellValue(text);
        return cell;
    }

    private static string ExcelSheetName(string name, HashSet<string> used, string fallback, int index)
    {
        var raw = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        foreach (var c in raw.ToCharArray())
        {
            if (c is ':' or '\\' or '/' or '?' or '*' or '[' or ']')
            {
                raw = raw.Replace(c, ' ');
            }
        }

        raw = raw.Trim();
        if (raw.Length == 0)
        {
            raw = $"Sayfa{index}";
        }

        if (raw.Length > 31)
        {
            raw = raw[..31];
        }

        var unique = raw;
        var n = 2;
        while (!used.Add(unique))
        {
            var suffix = n.ToString();
            unique = (raw.Length + suffix.Length + 1 > 31 ? raw[..Math.Max(1, 30 - suffix.Length)] : raw) + suffix;
            n++;
        }

        return unique;
    }

    private static IReadOnlyList<IReadOnlyList<string>> UsedRows(TableSheet sheet)
        => UsedRange(sheet, evaluate: false).Rows;

    private static (List<string> Headers, List<List<string>> Rows) UsedRange(TableSheet sheet, bool evaluate)
    {
        var rows = evaluate
            ? SheetFormula.EvaluateGrid(sheet.Headers, sheet.Rows)
            : sheet.Rows.Select(r => r.ToList()).ToList();
        var lastR = -1;
        var lastC = -1;
        for (var r = 0; r < rows.Count; r++)
        {
            var cols = Math.Max(sheet.Headers.Count, rows[r].Count);
            for (var c = 0; c < cols; c++)
            {
                var shown = c < rows[r].Count ? rows[r][c] : "";
                var raw = r < sheet.Rows.Count && c < sheet.Rows[r].Count ? sheet.Rows[r][c] : "";
                if (!string.IsNullOrWhiteSpace(shown) || !string.IsNullOrWhiteSpace(raw))
                {
                    lastR = Math.Max(lastR, r);
                    lastC = Math.Max(lastC, c);
                }
            }
        }

        if (lastR < 0)
        {
            var letter = sheet.Headers.Count > 0 ? sheet.Headers[0] : "A";
            return ([letter], [new List<string> { "" }]);
        }

        var headers = sheet.Headers.Take(lastC + 1).ToList();
        while (headers.Count <= lastC)
        {
            headers.Add(TableDocument.ColumnName(headers.Count));
        }

        var used = rows.Take(lastR + 1)
            .Select(r =>
            {
                var line = r.Take(lastC + 1).ToList();
                while (line.Count <= lastC)
                {
                    line.Add("");
                }

                return line;
            })
            .ToList();
        return (headers, used);
    }

    private static bool LooksLikeNumber(string text, out double n)
    {
        n = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Length > 1 && text[0] == '0' && text.All(char.IsDigit))
        {
            return false;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out n);
    }

    private static IEnumerable<string> SplitLines(string text)
        => (text ?? "").Replace("\r\n", "\n").Split('\n');
}
