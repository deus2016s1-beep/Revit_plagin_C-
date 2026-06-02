using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace VentCalc.Core.Models
{
    public sealed class VentPathSummary
    {
        public VentPathSummary(
            string systemType,
            string direction,
            bool isDirectionApproximate,
            string directionNote,
            IEnumerable<string> startElementIds,
            IEnumerable<string> endElementIds,
            IEnumerable<VentPathInfo> paths,
            string noPathReason)
        {
            SystemType = systemType;
            Direction = direction;
            IsDirectionApproximate = isDirectionApproximate;
            DirectionNote = directionNote;
            StartElementIds = new ReadOnlyCollection<string>(startElementIds.ToList());
            EndElementIds = new ReadOnlyCollection<string>(endElementIds.ToList());
            Paths = new ReadOnlyCollection<VentPathInfo>(paths.ToList());
            NoPathReason = noPathReason;
        }

        public string SystemType { get; }

        public string Direction { get; }

        public bool IsDirectionApproximate { get; }

        public string DirectionNote { get; }

        public IReadOnlyList<string> StartElementIds { get; }

        public IReadOnlyList<string> EndElementIds { get; }

        public IReadOnlyList<VentPathInfo> Paths { get; }

        public string NoPathReason { get; }

        public string ToReportText()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Трассы сети");
            builder.AppendLine(new string('=', 32));
            builder.AppendLine($"Тип системы: {SystemType}");
            builder.AppendLine($"Направление: {Direction}");
            if (IsDirectionApproximate)
            {
                builder.AppendLine("Направление системы определено приблизительно.");
            }

            if (!string.IsNullOrWhiteSpace(DirectionNote))
            {
                builder.AppendLine($"Примечание: {DirectionNote}");
            }

            builder.AppendLine($"Найдено стартовых точек: {StartElementIds.Count}");
            builder.AppendLine($"Найдено конечных точек: {EndElementIds.Count}");
            builder.AppendLine($"Всего трасс: {Paths.Count}");
            builder.AppendLine($"Стартовые ElementId: {FormatIds(StartElementIds)}");
            builder.AppendLine($"Конечные ElementId: {FormatIds(EndElementIds)}");
            builder.AppendLine();

            if (Paths.Count == 0)
            {
                builder.AppendLine(string.IsNullOrWhiteSpace(NoPathReason) ? "— Трассы не найдены" : NoPathReason);
                return builder.ToString();
            }

            foreach (VentPathInfo path in Paths)
            {
                builder.AppendLine($"Трасса №{path.PathIndex}");
                builder.AppendLine($"  StartElementId: {path.StartElementId}");
                builder.AppendLine($"  EndElementId: {path.EndElementId}");
                builder.AppendLine($"  Количество элементов: {path.TotalElementCount}");
                builder.AppendLine($"  Воздуховодов: {path.DuctCount}");
                builder.AppendLine($"  Фитингов: {path.FittingCount}");
                builder.AppendLine($"  Терминалов/решёток: {path.TerminalCount}");
                builder.AppendLine($"  Оборудования: {path.EquipmentCount}");
                builder.AppendLine($"  Длина воздуховодов: {path.TotalDuctLengthMm / 1000.0:0.###} м");
                builder.AppendLine($"  Расход: {path.MaxFlowM3h}");
                builder.AppendLine($"  Тип трассы: {path.PathKind}");
                builder.AppendLine($"  Цепочка: {string.Join(" → ", path.ElementIds)}");
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static string FormatIds(IReadOnlyList<string> ids)
        {
            return ids.Count == 0 ? "—" : string.Join(", ", ids);
        }
    }
}
