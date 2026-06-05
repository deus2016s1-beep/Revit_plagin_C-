using System;
using System.Collections.Generic;
using System.Linq;
using VentCalc.Core.Models;

namespace VentCalc.Core.Services
{
    public sealed class CalculationSectionBuilder
    {
        public const double FlowToleranceM3h = 1.0;
        public const double FlowToleranceRelative = 0.005;
        public const double SizeToleranceM = 0.001;
        public const double ShortDuctLengthThresholdM = 0.2;

        public IReadOnlyList<CalculationSectionInfo> Build(PathCalculationInfo pathCalculation)
        {
            var result = new List<CalculationSectionInfo>();
            if (pathCalculation.Ducts.Count == 0)
            {
                return result;
            }

            CalculationSectionInfo? current = null;
            foreach (DuctCalculationInfo duct in pathCalculation.Ducts)
            {
                string splitReason = ResolveSplitReason(current, duct);
                bool shortDuct = duct.LengthM > 0 && duct.LengthM < ShortDuctLengthThresholdM;

                if (current == null)
                {
                    current = CreateSection(pathCalculation.PathIndex, result.Count + 1, duct, "Начало трассы");
                    result.Add(current);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(splitReason) || shortDuct)
                {
                    AppendDuct(current, duct, shortDuct);
                    if (shortDuct && !string.IsNullOrWhiteSpace(splitReason))
                    {
                        current.SplitReason = AppendReason(current.SplitReason, "Короткий участок присоединён");
                        current.Warnings.Add($"Короткий воздуховод {duct.ElementId} ({duct.LengthM:0.###} м) присоединён к участку; причина возможного разделения: {splitReason}.");
                    }
                    continue;
                }

                current = CreateSection(pathCalculation.PathIndex, result.Count + 1, duct, splitReason);
                result.Add(current);
            }

            return result;
        }

        private static CalculationSectionInfo CreateSection(int pathIndex, int sectionIndex, DuctCalculationInfo duct, string splitReason)
        {
            var section = new CalculationSectionInfo
            {
                SectionIndex = sectionIndex,
                PathIndex = pathIndex,
                Shape = duct.IsRound ? "Круглый" : duct.IsRectangular ? "Прямоугольный" : "Не определено",
                Size = duct.Size,
                FlowM3h = duct.FlowM3h,
                AreaM2 = duct.AreaM2,
                EquivalentDiameterM = duct.EquivalentDiameterM,
                VelocityMs = duct.VelocityMs,
                Reynolds = duct.Reynolds,
                Lambda = duct.Lambda,
                DynamicPressurePa = duct.DynamicPressurePa,
                SpecificPressureLossPaPerM = duct.SpecificPressureLossPaPerM,
                SplitReason = splitReason
            };
            AppendDuct(section, duct, duct.LengthM > 0 && duct.LengthM < ShortDuctLengthThresholdM);
            if (section.ContainsShortDucts)
            {
                section.Warnings.Add($"Короткий воздуховод {duct.ElementId} ({duct.LengthM:0.###} м) учтён в участке.");
            }

            return section;
        }

        private static void AppendDuct(CalculationSectionInfo section, DuctCalculationInfo duct, bool isShortDuct)
        {
            section.ElementIds.Add(duct.ElementId);
            section.StartElementId = section.ElementIds.First();
            section.EndElementId = duct.ElementId;
            section.TotalLengthM += duct.LengthM;
            section.FrictionPressureLossPa += duct.FrictionPressureLossPa;
            section.ContainsShortDucts |= isShortDuct;
            foreach (string warning in duct.Warnings)
            {
                section.Warnings.Add($"{duct.ElementId}: {warning}");
            }
        }

        private static string ResolveSplitReason(CalculationSectionInfo? current, DuctCalculationInfo duct)
        {
            if (current == null)
            {
                return "Начало трассы";
            }

            var reasons = new List<string>();
            string nextShape = duct.IsRound ? "Круглый" : duct.IsRectangular ? "Прямоугольный" : "Не определено";
            if (!string.Equals(current.Shape, nextShape, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("Изменилась форма");
            }

            if (!SameSize(current, duct))
            {
                reasons.Add("Изменился размер");
            }

            if (!SameFlow(current.FlowM3h, duct.FlowM3h))
            {
                reasons.Add("Изменился расход");
            }

            return string.Join(" и ", reasons);
        }

        private static string AppendReason(string existingReason, string additionalReason)
        {
            if (string.IsNullOrWhiteSpace(existingReason))
            {
                return additionalReason;
            }

            return existingReason.IndexOf(additionalReason, StringComparison.OrdinalIgnoreCase) >= 0
                ? existingReason
                : $"{existingReason}; {additionalReason}";
        }

        private static bool SameSize(CalculationSectionInfo section, DuctCalculationInfo duct)
        {
            if (section.Shape == "Круглый")
            {
                return Math.Abs(section.EquivalentDiameterM - duct.EquivalentDiameterM) <= SizeToleranceM;
            }

            return string.Equals(section.Size, duct.Size, StringComparison.OrdinalIgnoreCase)
                || Math.Abs(section.AreaM2 - duct.AreaM2) <= SizeToleranceM * SizeToleranceM;
        }

        private static bool SameFlow(double first, double second)
        {
            double absolute = Math.Abs(first - second);
            double relativeBase = Math.Max(Math.Abs(first), Math.Abs(second));
            return absolute <= FlowToleranceM3h || (relativeBase > 0 && absolute / relativeBase <= FlowToleranceRelative);
        }
    }
}
