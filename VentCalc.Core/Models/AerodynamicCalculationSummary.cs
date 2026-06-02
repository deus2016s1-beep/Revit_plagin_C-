using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace VentCalc.Core.Models
{
    public sealed class AerodynamicCalculationSummary
    {
        public AerodynamicCalculationSummary(IEnumerable<PathCalculationInfo> paths)
        {
            Paths = new ReadOnlyCollection<PathCalculationInfo>(paths.ToList());
        }

        public IReadOnlyList<PathCalculationInfo> Paths { get; }

        public PathCalculationInfo? CriticalPathByFriction => Paths
            .OrderByDescending(path => path.TotalFrictionPressureLossPa)
            .FirstOrDefault();

        public string ToReportText()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Предварительный аэродинамический расчёт трасс");
            builder.AppendLine(new string('=', 48));

            if (Paths.Count == 0)
            {
                builder.AppendLine("— Трассы для расчёта не найдены");
                return builder.ToString();
            }

            PathCalculationInfo? criticalPath = CriticalPathByFriction;
            if (criticalPath != null)
            {
                builder.AppendLine(
                    $"Предварительно критическая трасса по трению: трасса №{criticalPath.PathIndex}, " +
                    $"{criticalPath.TotalFrictionPressureLossPa:0.###} Па. Местные сопротивления пока не учитывались.");
                builder.AppendLine();
            }

            foreach (PathCalculationInfo path in Paths)
            {
                builder.AppendLine($"Трасса №{path.PathIndex}");
                builder.AppendLine($"Start: {path.StartElementId}");
                builder.AppendLine($"End: {path.EndElementId}");
                builder.AppendLine($"Длина воздуховодов: {path.TotalDuctLengthM:0.###} м");
                builder.AppendLine($"Максимальная скорость: {path.MaxVelocityMs:0.###} м/с");
                builder.AppendLine($"Минимальная скорость: {path.MinVelocityMs:0.###} м/с");
                builder.AppendLine($"Потери на трение: {path.TotalFrictionPressureLossPa:0.###} Па");
                builder.AppendLine("Местные сопротивления: не учитывались");
                builder.AppendLine($"Итого предварительно: {path.TotalPressureLossPa:0.###} Па");
                AppendWarnings(builder, path.Warnings);
                builder.AppendLine();
                builder.AppendLine("ElementId | Размер | Расход м³/ч | Длина м | Площадь м² | Dэкв м | Скорость м/с | Re | λ | Pv Па | R Па/м | R·l Па");

                if (path.Ducts.Count == 0)
                {
                    builder.AppendLine("— В трассе нет воздуховодов для предварительного расчёта");
                }
                else
                {
                    foreach (DuctCalculationInfo duct in path.Ducts)
                    {
                        builder.AppendLine(
                            $"{duct.ElementId} | {duct.Size} | {duct.FlowM3h:0.###} | {duct.LengthM:0.###} | " +
                            $"{duct.AreaM2:0.####} | {duct.EquivalentDiameterM:0.####} | {duct.VelocityMs:0.###} | " +
                            $"{duct.Reynolds:0.#} | {duct.Lambda:0.####} | {duct.DynamicPressurePa:0.###} | " +
                            $"{duct.SpecificPressureLossPaPerM:0.###} | {duct.FrictionPressureLossPa:0.###}");
                        AppendWarnings(builder, duct.Warnings, "  ");
                    }
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static void AppendWarnings(StringBuilder builder, IReadOnlyList<string> warnings, string prefix = "")
        {
            foreach (string warning in warnings)
            {
                builder.AppendLine($"{prefix}Warning: {warning}");
            }
        }
    }
}
