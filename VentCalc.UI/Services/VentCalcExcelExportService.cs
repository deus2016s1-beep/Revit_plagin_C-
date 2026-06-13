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

            Directory.CreateDirectory(VentCalcDiagnosticReportService.GetReportsDirectory());
            DateTime createdAt = DateTime.Now;
            string path = Path.Combine(
                VentCalcDiagnosticReportService.GetReportsDirectory(),
                $"ventcalc_aero_{createdAt:yyyyMMdd_HHmmss}.xlsx");

            List<SheetData> sheets = BuildSheets(viewModel, viewModel.CriticalPath, createdAt);
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

        private static List<SheetData> BuildSheets(VentCalcCenterViewModel viewModel, PathCalculationInfo? criticalPath, DateTime createdAt)
        {
            if (criticalPath == null)
            {
                throw new InvalidOperationException("Критическая трасса не найдена. Проверьте трассировку системы.");
            }
            return new List<SheetData>
            {
                new SheetData("Итоги", BuildSummaryRows(viewModel, createdAt)),
                new SheetData("Аэродинамический расчёт", BuildAeroRows(viewModel, criticalPath)),
                new SheetData("Местные сопротивления", BuildLocalRows(criticalPath)),
                new SheetData("Трассы", BuildPathRows(viewModel)),
                new SheetData("Проверки", BuildIssueRows(viewModel)),
                new SheetData("Исходные данные", BuildInputRows(viewModel))
            };
        }

        private static IReadOnlyList<IReadOnlyList<object?>> BuildSummaryRows(VentCalcCenterViewModel viewModel, DateTime createdAt)
        {
            return new List<IReadOnlyList<object?>>
            {
                Row("Параметр", "Значение"),
                Row("VentCalc", "v2.0"),
                Row("Дата формирования", createdAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                Row("Файл Revit", viewModel.RevitFilePath),
                Row("Система", viewModel.SystemName),
                Row("Тип системы", viewModel.SystemType),
                Row("Направление", viewModel.Direction),
                Row("Количество элементов", viewModel.TotalElements),
                Row("Количество воздуховодов", viewModel.DuctCount),
                Row("Количество фитингов", viewModel.FittingCount),
                Row("Количество терминалов/решёток/зонтов", viewModel.TerminalCount),
                Row("Количество трасс", viewModel.Paths.Count),
                Row("Критическая трасса", viewModel.CriticalPathDisplay),
                Row("Потери на трение, Па", Round(viewModel.SelectedPathFrictionPressureLossPa, 3)),
                Row("Местные сопротивления, Па", Round(viewModel.SelectedPathLocalPressureLossPa, 3)),
                Row("Запас давления, %", Round(viewModel.Settings.PressureReservePercent, 3)),
                Row("Итоговые потери, Па", Round(viewModel.SelectedPathTotalWithReservePa, 3)),
                Row("selfCheck status", "См. JSON/TXT диагностику"),
                Row("Предупреждения", string.Join("; ", viewModel.Issues.Where(issue => string.Equals(issue.Severity, "Warning", StringComparison.OrdinalIgnoreCase)).Select(issue => issue.DisplayMessage)))
            };
        }

        private static IReadOnlyList<IReadOnlyList<object?>> BuildAeroRows(VentCalcCenterViewModel viewModel, PathCalculationInfo criticalPath)
        {
            var rows = new List<IReadOnlyList<object?>>
            {
                Row("Участок", "ElementIds", "Start", "End", "Размер", "Расход, м³/ч", "Длина, м", "Площадь, м²", "Dэкв, м", "Скорость, м/с", "Re", "λ", "Pv, Па", "R, Па/м", "R·l, Па", "Причина", "Предупреждение")
            };
            foreach (CalculationSectionInfo section in criticalPath.Sections)
            {
                rows.Add(Row(section.SectionDisplayName, section.ElementIdsText, section.StartElementId, section.EndElementId, section.Size, Round(section.FlowM3h, 3), Round(section.TotalLengthM, 3), Round(section.AreaM2, 4), Round(section.EquivalentDiameterM, 4), Round(section.VelocityMs, 3), Round(section.Reynolds, 1), Round(section.Lambda, 4), Round(section.DynamicPressurePa, 3), Round(section.SpecificPressureLossPaPerM, 3), Round(section.FrictionPressureLossPa, 3), section.SplitReasonShort, section.WarningText));
            }

            rows.Add(Row(string.Empty));
            rows.Add(Row("Итого", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, Round(criticalPath.Sections.Sum(section => section.TotalLengthM), 3), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, Round(criticalPath.TotalFrictionPressureLossPa, 3), "Местные сопротивления, Па", Round(criticalPath.TotalLocalPressureLossPa, 3)));
            rows.Add(Row("Итоговые потери, Па", Round(criticalPath.TotalPressureLossPa, 3)));
            rows.Add(Row("Итоговые потери с запасом, Па", Round(criticalPath.TotalPressureLossPa * (1.0 + viewModel.Settings.PressureReservePercent / 100.0), 3)));
            return rows;
        }

        private static IReadOnlyList<IReadOnlyList<object?>> BuildLocalRows(PathCalculationInfo criticalPath)
        {
            var rows = new List<IReadOnlyList<object?>>
            {
                Row("ElementId", "Тип элемента", "Семейство", "Размер", "Вид МС", "Роль", "Auto ζ", "Manual ζ", "Effective ζ", "Источник ζ", "Скорость, м/с", "Pv, Па", "Z, Па", "Комментарий / предупреждение")
            };
            rows.AddRange(criticalPath.LocalResistances.Select(local => Row(local.ElementId, local.TypeName, local.FamilyName, local.Size, local.LocalKind, local.PathRole, Round(local.AutoZeta, 3), local.ManualZeta.HasValue ? Round(local.ManualZeta.Value, 3) : "—", Round(local.EffectiveZeta, 3), local.ZetaSource, Round(local.VelocityMs, 3), Round(local.DynamicPressurePa, 3), Round(local.LocalPressureLossPa, 3), local.WarningText)));
            return rows;
        }

        private static IReadOnlyList<IReadOnlyList<object?>> BuildPathRows(VentCalcCenterViewModel viewModel)
        {
            var rows = new List<IReadOnlyList<object?>>
            {
                Row("№ трассы", "Статус", "Start", "End", "Элементов", "Воздуховодов", "Фитингов", "Терминалов/зонтов", "Длина, м", "Расход, м³/ч", "Потери, Па", "Δ к критической, Па", "Цепочка ElementId")
            };
            rows.AddRange(viewModel.Paths.Select(path => Row(path.PathIndex, path.CriticalStatus, path.StartElementId, path.EndElementId, path.TotalElementCount, path.DuctCount, path.FittingCount, path.TerminalCount, Round(path.TotalDuctLengthM, 3), path.FlowM3h, Round(path.TotalPressureLossPa, 3), Round(path.PressureLossDeltaFromCriticalPa, 3), string.Join(" → ", path.ElementIds))));
            return rows;
        }

        private static IReadOnlyList<IReadOnlyList<object?>> BuildIssueRows(VentCalcCenterViewModel viewModel)
        {
            var rows = new List<IReadOnlyList<object?>> { Row("Уровень", "ElementId", "Раздел", "Проблема", "Рекомендация") };
            rows.AddRange(viewModel.Issues.Select(issue => Row(issue.DisplaySeverity, issue.ElementId, issue.DisplaySection, issue.DisplayMessage, issue.Recommendation)));
            return rows;
        }

        private static IReadOnlyList<IReadOnlyList<object?>> BuildInputRows(VentCalcCenterViewModel viewModel)
        {
            return new List<IReadOnlyList<object?>>
            {
                Row("Параметр", "Значение"),
                Row("Плотность воздуха, кг/м³", viewModel.Settings.AirDensityKgM3),
                Row("Динамическая вязкость, Па·с", viewModel.Settings.AirDynamicViscosityPaS),
                Row("Шероховатость, мм", viewModel.Settings.RoughnessMm),
                Row("Шероховатость, м", viewModel.Settings.RoughnessM),
                Row("Запас давления, %", viewModel.Settings.PressureReservePercent),
                Row("Минимальная скорость, м/с", viewModel.Settings.MinVelocityMs),
                Row("Максимальная скорость, м/с", viewModel.Settings.MaxVelocityMs),
                Row("Критическая скорость, м/с", viewModel.Settings.CriticalVelocityMs),
                Row("settingsSchemaVersion", viewModel.Settings.SettingsSchemaVersion),
                Row("selectedElementId", viewModel.SelectedElementId),
                Row("systemName", viewModel.SystemName),
                Row("systemType", viewModel.SystemType),
                Row("direction", viewModel.Direction),
                Row("totalElements", viewModel.TotalElements),
                Row("ductCount", viewModel.DuctCount),
                Row("fittingCount", viewModel.FittingCount),
                Row("terminalCount", viewModel.TerminalCount),
                Row("connectionCount", viewModel.ConnectionCount),
                Row("openConnectorCount", viewModel.OpenConnectorCount)
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
            var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><sheetData>");
            for (int r = 0; r < rows.Count; r++)
            {
                builder.Append($"<row r=\"{r + 1}\">");
                for (int c = 0; c < rows[r].Count; c++)
                {
                    object? value = rows[r][c];
                    string cell = ColumnName(c + 1) + (r + 1).ToString(CultureInfo.InvariantCulture);
                    string style = r == 0 ? " s=\"1\"" : string.Empty;
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

        private static string BuildStyles()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills><borders count=\"1\"><border><left style=\"thin\"/><right style=\"thin\"/><top style=\"thin\"/><bottom style=\"thin\"/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyBorder=\"1\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\"/></cellXfs></styleSheet>";
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
