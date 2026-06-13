using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using VentCalc.Core.Models;
using VentCalc.UI.ViewModels;

namespace VentCalc.UI.Services
{
    public sealed class VentCalcExcelExportResult
    {
        public string Path { get; set; } = string.Empty;
        public int SheetCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public static class VentCalcExcelExportService
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

        public static VentCalcExcelExportResult Export(VentCalcCenterViewModel viewModel)
        {
            if (viewModel.NetworkInfo == null || viewModel.AerodynamicSummary == null)
            {
                throw new InvalidOperationException("Сначала рассчитайте систему.");
            }

            if (viewModel.CriticalPath == null)
            {
                throw new InvalidOperationException("Критическая трасса не найдена. Проверьте трассировку системы.");
            }

            string exportDirectory = ResolveExportDirectory(viewModel.Settings.ExportFolderPath);
            DateTime createdAt = DateTime.Now;
            string path = Path.Combine(
                exportDirectory,
                $"ventcalc_aero_calc_{createdAt:yyyyMMdd_HHmmss}.xlsx");

            List<SheetData> sheets = BuildSheets(viewModel, viewModel.CriticalPath);
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

            return new VentCalcExcelExportResult
            {
                Path = path,
                SheetCount = sheets.Count,
                CreatedAt = createdAt
            };
        }


        public static string ResolveExportDirectory(string? configuredPath)
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

        private static List<SheetData> BuildSheets(VentCalcCenterViewModel viewModel, PathCalculationInfo? criticalPath)
        {
            if (criticalPath == null)
            {
                throw new InvalidOperationException("Критическая трасса не найдена. Проверьте трассировку системы.");
            }

            return new List<SheetData>
            {
                new SheetData("Аэродинамический расчёт", BuildAeroRows(viewModel, criticalPath)),
                new SheetData("Исходные данные", BuildInputRows(viewModel))
            };
        }

        private static IReadOnlyList<IReadOnlyList<object?>> BuildAeroRows(VentCalcCenterViewModel viewModel, PathCalculationInfo criticalPath)
        {
            Dictionary<CalculationSectionInfo, double> localLossBySection = AllocateLocalLossesBySection(criticalPath, out double unassignedLocalLossPa);
            double assignedLocalLossPa = localLossBySection.Values.Sum();
            var rows = new List<IReadOnlyList<object?>>
            {
                Row($"Аэродинамический расчёт системы {viewModel.SystemName}"),
                Row("№ п/п", "Участок", "Размер воздуховода, мм", "Расход L, м³/ч", "Длина l, м", "Площадь F, м²", "Экв. диаметр dэкв, м", "Скорость v, м/с", "Re", "λ", "Дин. давление Pv, Па", "Уд. потери R, Па/м", "Потери на трение R·l, Па", "Местные сопротивления Z, Па", "Потери участка ΔP, Па")
            };

            int rowNumber = 1;
            foreach (CalculationSectionInfo section in criticalPath.Sections)
            {
                localLossBySection.TryGetValue(section, out double localLossPa);
                rows.Add(Row(
                    rowNumber++,
                    section.SectionDisplayName,
                    section.Size,
                    Round(section.FlowM3h, 3),
                    Round(section.TotalLengthM, 3),
                    Round(section.AreaM2, 4),
                    Round(section.EquivalentDiameterM, 4),
                    Round(section.VelocityMs, 3),
                    Round(section.Reynolds, 0),
                    Round(section.Lambda, 4),
                    Round(section.DynamicPressurePa, 3),
                    Round(section.SpecificPressureLossPaPerM, 3),
                    Round(section.FrictionPressureLossPa, 3),
                    Round(localLossPa, 3),
                    Round(section.FrictionPressureLossPa + localLossPa, 3)));
            }

            rows.Add(Row(string.Empty));
            rows.Add(Row("Итоговый блок"));
            rows.Add(Row("Суммарная длина, м", Round(criticalPath.Sections.Sum(section => section.TotalLengthM), 3)));
            rows.Add(Row("Потери на трение, Па", Round(criticalPath.TotalFrictionPressureLossPa, 3)));
            if (Math.Abs(unassignedLocalLossPa) > 0.0005)
            {
                rows.Add(Row("Местные сопротивления привязанные, Па", Round(assignedLocalLossPa, 3)));
                rows.Add(Row("Местные сопротивления нераспределённые, Па", Round(unassignedLocalLossPa, 3)));
                rows.Add(Row("Местные сопротивления всего, Па", Round(criticalPath.TotalLocalPressureLossPa, 3)));
            }
            else
            {
                rows.Add(Row("Местные сопротивления, Па", Round(criticalPath.TotalLocalPressureLossPa, 3)));
            }
            rows.Add(Row("Итого, Па", Round(criticalPath.TotalPressureLossPa, 3)));
            return rows;
        }

        private static Dictionary<CalculationSectionInfo, double> AllocateLocalLossesBySection(PathCalculationInfo criticalPath, out double unassignedLocalLossPa)
        {
            var localLossBySection = new Dictionary<CalculationSectionInfo, double>();
            unassignedLocalLossPa = 0;
            foreach (LocalResistanceCalculationInfo local in criticalPath.LocalResistances)
            {
                CalculationSectionInfo? section = FindSectionForLocalResistance(criticalPath.Sections, local);
                if (section == null)
                {
                    unassignedLocalLossPa += local.LocalPressureLossPa;
                    continue;
                }

                localLossBySection.TryGetValue(section, out double currentLoss);
                localLossBySection[section] = currentLoss + local.LocalPressureLossPa;
            }

            return localLossBySection;
        }

        private static CalculationSectionInfo? FindSectionForLocalResistance(IReadOnlyList<CalculationSectionInfo> sections, LocalResistanceCalculationInfo local)
        {
            CalculationSectionInfo? nextSection = FindSectionContainingDuct(sections, local.NextDuctElementId);
            if (nextSection != null)
            {
                return nextSection;
            }

            CalculationSectionInfo? previousSection = FindSectionContainingDuct(sections, local.PreviousDuctElementId);
            if (previousSection != null)
            {
                return previousSection;
            }

            return FindSectionContainingDuct(sections, local.ElementId);
        }

        private static CalculationSectionInfo? FindSectionContainingDuct(IReadOnlyList<CalculationSectionInfo> sections, long? elementId)
        {
            if (!elementId.HasValue || elementId.Value <= 0)
            {
                return null;
            }

            return sections.FirstOrDefault(section => section.ElementIds.Contains(elementId.Value));
        }

        private static IReadOnlyList<IReadOnlyList<object?>> BuildInputRows(VentCalcCenterViewModel viewModel)
        {
            return new List<IReadOnlyList<object?>>
            {
                Row("Исходные данные"),
                Row("Параметр", "Значение", "Ед. изм."),
                Row("Система", viewModel.SystemName, string.Empty),
                Row("Тип системы", viewModel.SystemType, string.Empty),
                Row("Направление", viewModel.Direction, string.Empty),
                Row("Количество трасс", viewModel.Paths.Count, "шт."),
                Row("Критическая трасса", viewModel.CriticalPathDisplay, string.Empty),
                Row("Плотность воздуха", Round(viewModel.Settings.AirDensityKgM3, 3), "кг/м³"),
                Row("Динамическая вязкость", viewModel.Settings.AirDynamicViscosityPaS.ToString("0.#######E+0", CultureInfo.InvariantCulture), "Па·с"),
                Row("Шероховатость воздуховода", Round(viewModel.Settings.RoughnessMm, 3), "мм")
            };
        }

        private static IReadOnlyList<object?> Row(params object?[] values) => values;

        private static double Round(double value, int digits) => Math.Round(value, digits);

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
            var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"2\" topLeftCell=\"A3\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            builder.Append(BuildColumns(rows, maxColumns));
            builder.Append("<sheetData>");
            for (int r = 0; r < rows.Count; r++)
            {
                builder.Append($"<row r=\"{r + 1}\">");
                for (int c = 0; c < rows[r].Count; c++)
                {
                    object? value = rows[r][c];
                    string cell = ColumnName(c + 1) + (r + 1).ToString(CultureInfo.InvariantCulture);
                    string style = r == 0 ? " s=\"1\"" : r == 1 ? " s=\"2\"" : " s=\"3\"";
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
                int headerRow = rows.Count > 1 ? 2 : 1;
                builder.Append($"<autoFilter ref=\"A{headerRow}:{ColumnName(maxColumns)}{rows.Count}\"/>");
            }
            builder.Append("</worksheet>");
            return builder.ToString();
        }

        private static string BuildColumns(IReadOnlyList<IReadOnlyList<object?>> rows, int maxColumns)
        {
            var builder = new StringBuilder("<cols>");
            for (int c = 0; c < maxColumns; c++)
            {
                int maxLength = rows
                    .Where(row => row.Count > c)
                    .Select(row => Convert.ToString(row[c], CultureInfo.InvariantCulture)?.Length ?? 0)
                    .DefaultIfEmpty(8)
                    .Max();
                double width = Math.Max(10, Math.Min(42, maxLength + 2));
                string widthText = width.ToString("0.##", CultureInfo.InvariantCulture);
                builder.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{widthText}\" customWidth=\"1\"/>");
            }

            builder.Append("</cols>");
            return builder.ToString();
        }

        private static string BuildStyles()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><fonts count=""2""><font><sz val=""11""/><name val=""Calibri""/></font><font><b/><sz val=""11""/><name val=""Calibri""/></font></fonts><fills count=""3""><fill><patternFill patternType=""none""/></fill><fill><patternFill patternType=""gray125""/></fill><fill><patternFill patternType=""solid""><fgColor rgb=""FFE6E6E6""/><bgColor indexed=""64""/></patternFill></fill></fills><borders count=""1""><border><left style=""thin""/><right style=""thin""/><top style=""thin""/><bottom style=""thin""/><diagonal/></border></borders><cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs><cellXfs count=""4""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/><xf numFmtId=""0"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0"" applyFont=""1""/><xf numFmtId=""0"" fontId=""1"" fillId=""2"" borderId=""0"" xfId=""0"" applyFont=""1"" applyFill=""1"" applyBorder=""1""/><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0"" applyBorder=""1""/></cellXfs></styleSheet>";
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

        private static string Escape(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
