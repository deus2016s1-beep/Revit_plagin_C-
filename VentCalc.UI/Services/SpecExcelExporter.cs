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
        private sealed class ImageExportStats
        {
            public int TotalRows { get; set; }
            public bool ImageColumnEnabled { get; set; }
            public int ImagePathCount { get; set; }
            public int ExistingImageFileCount { get; set; }
            public int InsertedImageCount { get; set; }
        }

        public static string LastImageDiagnostics { get; private set; } = string.Empty;

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
            return Export(SpecGroupingService.Group(sourceRows), "Проектная спецификация", SpecCalcSettingsService.CreateDefaultColumns(), exportDirectory);
        }

        public static SpecExcelExportResult Export(IEnumerable<SpecGroupRow> sourceRows, string profile, IEnumerable<SpecColumnLayout> columnLayouts, string? exportDirectory = null)
        {
            List<SpecGroupRow> rows = sourceRows.ToList();
            if (rows.Count == 0)
            {
                throw new InvalidOperationException("Сначала соберите вентиляцию.");
            }

            string directory = ResolveExportDirectory(exportDirectory);
            DateTime createdAt = DateTime.Now;
            string path = Path.Combine(directory, $"spec_ventilation_{createdAt:yyyyMMdd_HHmmss}.xlsx");
            bool includeImages = profile == "Визуальная спецификация";
            var imageStats = new ImageExportStats { TotalRows = rows.Count, ImageColumnEnabled = includeImages };
            List<SheetData> sheets = new List<SheetData>
            {
                new SheetData(GetSheetName(profile), BuildSpecificationRows(rows, profile, columnLayouts, createdAt))
            };

            using (FileStream stream = File.Create(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                AddText(archive, "[Content_Types].xml", BuildContentTypes(sheets.Count, includeImages));
                AddText(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                AddText(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRels(sheets.Count));
                AddText(archive, "xl/styles.xml", BuildStyles());
                AddText(archive, "xl/workbook.xml", BuildWorkbook(sheets));
                for (int i = 0; i < sheets.Count; i++)
                {
                    AddText(archive, $"xl/worksheets/sheet{i + 1}.xml", BuildWorksheet(archive, i + 1, sheets[i].Rows, imageStats));
                }
            }

            LastImageDiagnostics = $"totalRows={imageStats.TotalRows}; imageColumnEnabled={imageStats.ImageColumnEnabled}; imagePathCount={imageStats.ImagePathCount}; existingImageFileCount={imageStats.ExistingImageFileCount}; insertedImageCount={imageStats.InsertedImageCount}";
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

        private static IReadOnlyList<IReadOnlyList<object?>> BuildSpecificationRows(IReadOnlyList<SpecGroupRow> rows, string profile, IEnumerable<SpecColumnLayout> columnLayouts, DateTime createdAt)
        {
            List<SpecColumnLayout> columns = ResolveColumns(profile, columnLayouts);
            var result = new List<IReadOnlyList<object?>>
            {
                Row(profile),
                Row($"Дата экспорта: {createdAt:yyyy-MM-dd HH:mm:ss}"),
                Row(new[] { "№" }.Concat(columns.Select(column => column.Header)).ToArray())
            };

            int number = 1;
            foreach (SpecGroupRow row in rows.OrderBy(row => row.Section).ThenBy(row => row.Group).ThenBy(row => row.Name).ThenBy(row => row.Size))
            {
                var values = new List<object?> { number++ };
                foreach (SpecColumnLayout column in columns)
                {
                    values.Add(GetValue(row, column.FieldName, profile));
                }
                result.Add(values);
            }

            return result;
        }

        private static List<SpecColumnLayout> ResolveColumns(string profile, IEnumerable<SpecColumnLayout> columnLayouts)
        {
            List<SpecColumnLayout> columns = columnLayouts
                .Where(column => column.VisibleInExcel)
                .OrderBy(column => column.Order)
                .Select(column => new SpecColumnLayout
                {
                    FieldName = column.FieldName,
                    Header = column.FieldName == "ImagePath" ? "Изображение" : column.Header,
                    Order = column.Order,
                    VisibleInExcel = column.VisibleInExcel,
                    VisibleInMain = column.VisibleInMain,
                    Format = column.Format,
                    IsNumeric = column.IsNumeric
                })
                .ToList();

            if (profile != "Визуальная спецификация")
            {
                columns.RemoveAll(column => column.FieldName == "ImagePath");
            }
            else if (!columns.Any(column => column.FieldName == "ImagePath"))
            {
                SpecColumnLayout imageColumn = SpecCalcSettingsService.CreateDefaultColumns().First(column => column.FieldName == "ImagePath");
                columns.Insert(Math.Min(4, columns.Count), new SpecColumnLayout
                {
                    FieldName = imageColumn.FieldName,
                    Header = "Изображение",
                    Order = columns.Count == 0 ? 1 : columns.Min(column => column.Order) - 1,
                    VisibleInExcel = true,
                    VisibleInMain = imageColumn.VisibleInMain,
                    Format = "Изображение",
                    IsNumeric = false
                });
            }

            return columns.Count == 0
                ? SpecCalcSettingsService.CreateDefaultColumns().Where(column => column.VisibleInExcel && column.FieldName != "ImagePath").OrderBy(column => column.Order).ToList()
                : columns;
        }

        private static string GetSheetName(string profile)
        {
            return profile == "Монтажная ведомость" ? "Монтажная ведомость" : profile == "Закупка" ? "Закупка" : "Спецификация";
        }

        private static object? GetValue(SpecGroupRow row, string field, string profile)
        {
            return field switch
            {
                "Section" => row.Section,
                "Group" => row.Group,
                "Name" => row.Name,
                "TypeMark" => row.TypeMark,
                "Size" => row.Size,
                "Unit" => row.Unit,
                "Quantity" => Math.Round(row.Quantity, row.Unit == "шт" ? 0 : 2),
                "LengthM" => row.LengthM > 0 ? (object)Math.Round(row.LengthM, 2) : null,
                "AreaM2" => row.AreaM2 > 0 ? (object)Math.Round(row.AreaM2, 2) : null,
                "System" => row.System,
                "Level" => row.Level,
                "Material" => row.Material,
                "ImagePath" => profile == "Визуальная спецификация" && !string.IsNullOrWhiteSpace(row.ImagePath) && File.Exists(row.ImagePath) ? $"__IMG:{row.ImagePath}" : string.Empty,
                "Note" => row.Note,
                _ => string.Empty
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

        private static string BuildContentTypes(int sheetCount, bool includeImages)
        {
            var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 1; i <= sheetCount; i++)
            {
                builder.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                if (includeImages)
                {
                    builder.Append($"<Override PartName=\"/xl/drawings/drawing{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>");
                }
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

        private static string BuildWorksheet(ZipArchive archive, int sheetIndex, IReadOnlyList<IReadOnlyList<object?>> rows, ImageExportStats imageStats)
        {
            int maxColumns = Math.Max(1, rows.Any() ? rows.Max(row => row.Count) : 1);
            var images = new List<(int Row, int Column, string Path)>();
            var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            builder.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"3\" topLeftCell=\"A4\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            HashSet<int> imageColumns = rows.SelectMany(row => row.Select((value, index) => new { value, index }))
                .Where(item => Convert.ToString(item.value, CultureInfo.InvariantCulture)?.StartsWith("__IMG:", StringComparison.Ordinal) == true)
                .Select(item => item.index + 1)
                .ToHashSet();
            builder.Append(BuildColumns(maxColumns, imageColumns));
            builder.Append("<sheetData>");
            for (int r = 0; r < rows.Count; r++)
            {
                int rowNumber = r + 1;
                bool hasImage = rows[r].Any(value => Convert.ToString(value, CultureInfo.InvariantCulture)?.StartsWith("__IMG:", StringComparison.Ordinal) == true);
                builder.Append($"<row r=\"{rowNumber}\"{(hasImage ? " ht=\"72\" customHeight=\"1\"" : string.Empty)}>");
                for (int c = 0; c < rows[r].Count; c++)
                {
                    object? value = rows[r][c];
                    int columnNumber = c + 1;
                    string? textValue = Convert.ToString(value, CultureInfo.InvariantCulture);
                    string cell = ColumnName(columnNumber) + rowNumber.ToString(CultureInfo.InvariantCulture);
                    string style = r == 0 || r == 2 ? " s=\"1\"" : " s=\"2\"";
                    if (textValue?.StartsWith("__IMG:", StringComparison.Ordinal) == true)
                    {
                        string imagePath = textValue.Substring("__IMG:".Length);
                        imageStats.ImagePathCount++;
                        if (File.Exists(imagePath)) imageStats.ExistingImageFileCount++;
                        images.Add((rowNumber, columnNumber, imagePath));
                        builder.Append($"<c r=\"{cell}\"{style}/>");
                    }
                    else if (value is int or long or double or float or decimal)
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
                builder.Append($"<autoFilter ref=\"A3:{ColumnName(maxColumns)}{rows.Count}\"/>");
            }
            if (images.Count > 0)
            {
                try
                {
                    imageStats.InsertedImageCount += AddWorksheetImages(archive, sheetIndex, images);
                    builder.Append($"<drawing r:id=\"rIdDrawing{sheetIndex}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"/>");
                }
                catch (Exception)
                {
                    // Image insertion is optional: keep the workbook exportable even if a PNG/drawing part fails.
                }
            }
            builder.Append("</worksheet>");
            return builder.ToString();
        }

        private static int AddWorksheetImages(ZipArchive archive, int sheetIndex, IReadOnlyList<(int Row, int Column, string Path)> images)
        {
            int inserted = 0;
            var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            rels.Append($"<Relationship Id=\"rIdDrawing{sheetIndex}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing{sheetIndex}.xml\"/>");
            rels.Append("</Relationships>");
            AddText(archive, $"xl/worksheets/_rels/sheet{sheetIndex}.xml.rels", rels.ToString());

            var drawingRels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            var drawing = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">");
            for (int i = 0; i < images.Count; i++)
            {
                if (!File.Exists(images[i].Path)) continue;
                string mediaName = $"spec_image_{sheetIndex}_{i + 1}.png";
                byte[] bytes = File.ReadAllBytes(images[i].Path);
                if (bytes.Length == 0) continue;
                ZipArchiveEntry imageEntry = archive.CreateEntry($"xl/media/{mediaName}", CompressionLevel.Optimal);
                using (Stream stream = imageEntry.Open()) stream.Write(bytes, 0, bytes.Length);
                inserted++;
                drawingRels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/{mediaName}\"/>");
                int col = images[i].Column - 1;
                int row = images[i].Row - 1;
                drawing.Append($"<xdr:oneCellAnchor><xdr:from><xdr:col>{col}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{row}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:ext cx=\"914400\" cy=\"914400\"/><xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"{i + 1}\" name=\"Spec image {i + 1}\"/><xdr:cNvPicPr/></xdr:nvPicPr><xdr:blipFill><a:blip xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" r:embed=\"rId{i + 1}\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill><xdr:spPr><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:oneCellAnchor>");
            }
            drawing.Append("</xdr:wsDr>");
            drawingRels.Append("</Relationships>");
            AddText(archive, $"xl/drawings/drawing{sheetIndex}.xml", drawing.ToString());
            AddText(archive, $"xl/drawings/_rels/drawing{sheetIndex}.xml.rels", drawingRels.ToString());
            return inserted;
        }

        private static string BuildColumns(int maxColumns, ISet<int> imageColumns)
        {
            var builder = new StringBuilder("<cols>");
            for (int c = 0; c < maxColumns; c++)
            {
                int oneBased = c + 1;
                double width = imageColumns.Contains(oneBased) ? 18 : c switch { 0 => 6, 1 => 18, 2 => 32, 3 => 18, 4 => 14, 5 => 10, 6 => 10, 7 => 10, 8 => 12, _ => 24 };
                builder.Append($"<col min=\"{oneBased}\" max=\"{oneBased}\" width=\"{width.ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
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
