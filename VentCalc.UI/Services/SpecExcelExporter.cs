using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace VentCalc.UI.Services
{
    public sealed class SpecExcelExportResult
    {
        public string Path { get; set; } = string.Empty;
        public int SheetCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public static class SpecExcelExporter
    {
        private sealed class SheetData
        {
            public SheetData(string name, IReadOnlyList<IReadOnlyList<object?>> rows)
            {
                Name = name;
                Rows = rows;
            }

            public string Name { get; }
            public IReadOnlyList<IReadOnlyList<object?>> Rows { get; }
        }

        public static SpecExcelExportResult Export(IEnumerable<SpecItemRow> sourceRows, string? exportDirectory = null)
        {
            List<SpecItemRow> rows = sourceRows.ToList();
            if (rows.Count == 0)
            {
                throw new InvalidOperationException("Сначала соберите вентиляцию.");
            }

            string directory = ResolveExportDirectory(exportDirectory);
            DateTime createdAt = DateTime.Now;
            string path = Path.Combine(directory, $"spec_ventilation_{createdAt:yyyyMMdd_HHmmss}.xlsx");
            List<SheetData> sheets = new List<SheetData>
            {
                new SheetData("Вентиляция", BuildVentilationRows(rows)),
                new SheetData("Проблемы", BuildProblemRows(rows)),
                new SheetData("Сводка", BuildSummaryRows(rows, createdAt))
            };

            using (FileStream stream = File.Create(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                AddText(archive, "[Content_Types].xml", BuildContentTypes(sheets.Count));
                AddText(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                AddText(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRels(sheets.Count));
                AddText(archive, "xl/styles.xml", BuildStyles());
                AddText(archive, "xl/workbook.xml", BuildWorkbook(sheets));
                for (int i = 0; i < sheets.Count; i++)
                {
                    AddText(archive, $"xl/worksheets/sheet{i + 1}.xml", BuildWorksheet(sheets[i].Rows));
                }
            }

            return new SpecExcelExportResult { Path = path, SheetCount = sheets.Count, CreatedAt = createdAt };
        }

        private static string ResolveExportDirectory(string? configuredPath)
        {
            string defaultDirectory = VentCalcDiagnosticReportService.GetReportsDirectory();
            string directory = string.IsNullOrWhiteSpace(configuredPath) ? defaultDirectory : Environment.ExpandEnvironmentVariables(configuredPath.Trim());
            try
            {
                Directory.CreateDirectory(directory);
                return directory;
            }
            catch (Exception)
            {
                Directory.CreateDirectory(defaultDirectory);
                return defaultDirectory;
            }
        }

        private static IReadOnlyList<IReadOnlyList<object?>> BuildVentilationRows(IReadOnlyList<SpecItemRow> rows)
        {
            var result = new List<IReadOnlyList<object?>>
            {
                Row("№", "Наименование", "Тип / марка", "Размер", "Ед. изм.", "Кол-во", "Примечание")
            };

            var groups = rows
                .GroupBy(row => new { row.Name, row.TypeMark, row.Size, row.Unit, row.Note })
                .OrderBy(group => group.Key.Name)
                .ThenBy(group => group.Key.TypeMark)
                .ThenBy(group => group.Key.Size)
                .ToList();

            int number = 1;
            foreach (var group in groups)
            {
                result.Add(Row(
                    number++,
                    group.Key.Name,
                    group.Key.TypeMark,
                    group.Key.Size,
                    group.Key.Unit,
                    Math.Round(group.Sum(row => row.Quantity), 3),
                    group.Key.Note));
            }

            return result;
        }

        private static IReadOnlyList<IReadOnlyList<object?>> BuildProblemRows(IReadOnlyList<SpecItemRow> rows)
        {
            var result = new List<IReadOnlyList<object?>>
            {
                Row("Статус", "Проблема", "Группа", "Наименование", "Тип / марка", "Размер", "Система", "Уровень", "Рекомендация")
            };

            foreach (SpecItemRow row in rows.Where(row => !string.Equals(row.Status, "OK", StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(Row(row.Status, row.Problem, row.Group, row.Name, row.TypeMark, row.Size, row.System, row.Level, row.Recommendation));
            }

            return result;
        }

        private static IReadOnlyList<IReadOnlyList<object?>> BuildSummaryRows(IReadOnlyList<SpecItemRow> rows, DateTime createdAt)
        {
            return new List<IReadOnlyList<object?>>
            {
                Row("Показатель", "Значение"),
                Row("Всего элементов", rows.Count),
                Row("OK", rows.Count(row => row.Status == "OK")),
                Row("Warning", rows.Count(row => row.Status == "Warning")),
                Row("Error", rows.Count(row => row.Status == "Error")),
                Row("Неопознано", rows.Count(row => row.IsUnrecognized || row.Group == "Неопознано")),
                Row("Без размера", rows.Count(row => row.MissingSize)),
                Row("Без системы", rows.Count(row => row.MissingSystem)),
                Row("Без ADSK_Наименование", rows.Count(row => !row.HasAdskName)),
                Row("Дата экспорта", createdAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                Row("Имя документа Revit", "—")
            };
        }

        private static IReadOnlyList<object?> Row(params object?[] values) => values;

        private static void AddText(ZipArchive archive, string path, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using Stream stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(content);
        }

        private static string BuildContentTypes(int sheetCount)
        {
            var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 1; i <= sheetCount; i++)
            {
                builder.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }

            builder.Append("</Types>");
            return builder.ToString();
        }

        private static string BuildWorkbookRels(int sheetCount)
        {
            var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 1; i <= sheetCount; i++)
            {
                builder.Append($"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
            }

            builder.Append($"<Relationship Id=\"rId{sheetCount + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            return builder.ToString();
        }

        private static string BuildWorkbook(IReadOnlyList<SheetData> sheets)
        {
            var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            for (int i = 0; i < sheets.Count; i++)
            {
                builder.Append($"<sheet name=\"{Escape(sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
            }

            builder.Append("</sheets></workbook>");
            return builder.ToString();
        }

        private static string BuildWorksheet(IReadOnlyList<IReadOnlyList<object?>> rows)
        {
            int maxColumns = Math.Max(1, rows.Any() ? rows.Max(row => row.Count) : 1);
            var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            builder.Append(BuildColumns(maxColumns));
            builder.Append("<sheetData>");
            for (int r = 0; r < rows.Count; r++)
            {
                builder.Append($"<row r=\"{r + 1}\">");
                for (int c = 0; c < rows[r].Count; c++)
                {
                    object? value = rows[r][c];
                    string cell = ColumnName(c + 1) + (r + 1).ToString(CultureInfo.InvariantCulture);
                    string style = r == 0 ? " s=\"1\"" : " s=\"2\"";
                    if (value is int or long or double or float or decimal)
                    {
                        builder.Append($"<c r=\"{cell}\"{style}><v>{Convert.ToString(value, CultureInfo.InvariantCulture)}</v></c>");
                    }
                    else
                    {
                        builder.Append($"<c r=\"{cell}\" t=\"inlineStr\"{style}><is><t>{Escape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}</t></is></c>");
                    }
                }
                builder.Append("</row>");
            }

            builder.Append("</sheetData>");
            if (rows.Count > 0)
            {
                builder.Append($"<autoFilter ref=\"A1:{ColumnName(maxColumns)}{rows.Count}\"/>");
            }
            builder.Append("</worksheet>");
            return builder.ToString();
        }

        private static string BuildColumns(int maxColumns)
        {
            var builder = new StringBuilder("<cols>");
            for (int c = 0; c < maxColumns; c++)
            {
                builder.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{(c == maxColumns - 1 ? 28 : 16)}\" customWidth=\"1\"/>");
            }
            builder.Append("</cols>");
            return builder.ToString();
        }

        private static string BuildStyles()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><fonts count=""2""><font><sz val=""11""/><name val=""Calibri""/></font><font><b/><sz val=""11""/><name val=""Calibri""/></font></fonts><fills count=""3""><fill><patternFill patternType=""none""/></fill><fill><patternFill patternType=""gray125""/></fill><fill><patternFill patternType=""solid""><fgColor rgb=""FFE6E6E6""/><bgColor indexed=""64""/></patternFill></fill></fills><borders count=""1""><border><left style=""thin""/><right style=""thin""/><top style=""thin""/><bottom style=""thin""/><diagonal/></border></borders><cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs><cellXfs count=""3""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/><xf numFmtId=""0"" fontId=""1"" fillId=""2"" borderId=""0"" xfId=""0"" applyFont=""1"" applyFill=""1"" applyBorder=""1""/><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0"" applyBorder=""1""/></cellXfs></styleSheet>";
        }

        private static string ColumnName(int column)
        {
            var name = new StringBuilder();
            while (column > 0)
            {
                int modulo = (column - 1) % 26;
                name.Insert(0, (char)('A' + modulo));
                column = (column - modulo) / 26;
            }

            return name.ToString();
        }

        private static string Escape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
