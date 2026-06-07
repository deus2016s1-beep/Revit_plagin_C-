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
            string directionReason,
            bool isDirectionApproximate,
            IEnumerable<string> startCandidateDetails,
            IEnumerable<string> endCandidateDetails,
            IEnumerable<string> ignoredCapDetails,
            IEnumerable<string> warnings,
            IEnumerable<string> startElementIds,
            IEnumerable<string> endElementIds,
            VentPathEndpointSelection endpointSelection,
            IEnumerable<VentPathInfo> paths,
            string noPathReason)
        {
            SystemType = systemType;
            Direction = direction;
            DirectionReason = directionReason;
            IsDirectionApproximate = isDirectionApproximate;
            StartCandidateDetails = new ReadOnlyCollection<string>(startCandidateDetails.ToList());
            EndCandidateDetails = new ReadOnlyCollection<string>(endCandidateDetails.ToList());
            IgnoredCapDetails = new ReadOnlyCollection<string>(ignoredCapDetails.ToList());
            Warnings = new ReadOnlyCollection<string>(warnings.ToList());
            StartElementIds = new ReadOnlyCollection<string>(startElementIds.ToList());
            EndElementIds = new ReadOnlyCollection<string>(endElementIds.ToList());
            EndpointSelection = endpointSelection;
            Paths = new ReadOnlyCollection<VentPathInfo>(paths.ToList());
            NoPathReason = noPathReason;
        }

        public string SystemType { get; }

        public string Direction { get; }

        public string DirectionReason { get; }

        public bool IsDirectionApproximate { get; }

        public IReadOnlyList<string> StartCandidateDetails { get; }

        public IReadOnlyList<string> EndCandidateDetails { get; }

        public IReadOnlyList<string> IgnoredCapDetails { get; }

        public IReadOnlyList<string> Warnings { get; }

        public IReadOnlyList<string> StartElementIds { get; }

        public IReadOnlyList<string> EndElementIds { get; }

        public VentPathEndpointSelection EndpointSelection { get; }

        public IReadOnlyList<VentPathInfo> Paths { get; }

        public string NoPathReason { get; }

        public string ToReportText()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Трассы сети");
            builder.AppendLine(new string('=', 32));
            builder.AppendLine($"Тип системы: {SystemType}");
            builder.AppendLine($"Направление: {Direction}");
            builder.AppendLine($"Причина: {DirectionReason}");
            if (IsDirectionApproximate)
            {
                builder.AppendLine("Направление системы определено приблизительно.");
            }

            AppendList(builder, "Стартовые кандидаты", StartCandidateDetails);
            AppendList(builder, "Конечные кандидаты", EndCandidateDetails);
            AppendList(builder, "Кандидаты-зонты", EndpointSelection.HoodCandidates.Select(candidate => FormatCandidate(candidate)).ToList());
            AppendList(builder, "Открытые концы", EndpointSelection.OpenEndCandidates.Select(candidate => FormatCandidate(candidate)).ToList());
            AppendList(builder, "Кандидаты-вентиляторы", EndpointSelection.FanCandidates.Select(candidate => FormatCandidate(candidate)).ToList());
            AppendList(builder, "Отклонённые кандидаты", EndpointSelection.RejectedCandidates.Select(candidate => FormatCandidate(candidate)).ToList());
            AppendList(builder, "Игнорируемые заглушки", IgnoredCapDetails);
            AppendList(builder, "Предупреждения", Warnings);

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
                builder.AppendLine($"  TotalElementCount: {path.TotalElementCount}");
                builder.AppendLine($"  DuctCount: {path.DuctCount}");
                builder.AppendLine($"  FittingCount: {path.FittingCount}");
                builder.AppendLine($"  AccessoryCount: {path.AccessoryCount}");
                builder.AppendLine($"  TerminalCount: {path.TerminalCount}");
                builder.AppendLine($"  EquipmentCount: {path.EquipmentCount}");
                builder.AppendLine($"  TotalDuctLengthMm: {path.TotalDuctLengthMm:0.###}");
                builder.AppendLine($"  TotalDuctLengthM: {path.TotalDuctLengthM:0.###}");
                builder.AppendLine($"  Расход: {path.MaxFlowM3h}");
                builder.AppendLine($"  Тип трассы: {path.PathKind}");
                builder.AppendLine($"  Цепочка: {string.Join(" → ", path.ElementIds)}");
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static string FormatCandidate(VentEndpointCandidateInfo candidate)
        {
            return $"{candidate.ElementId} | Role={candidate.Role} | Category={candidate.Category} | Family={candidate.FamilyName} | Type={candidate.TypeName} | Connectors={candidate.ConnectorCount} | Connected={candidate.ConnectedHvacConnectorCount} | Degree={candidate.GraphDegree} | {candidate.Reason}";
        }

        private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values)
        {
            builder.AppendLine($"{title}:");
            if (values.Count == 0)
            {
                builder.AppendLine("  —");
                return;
            }

            foreach (string value in values)
            {
                builder.AppendLine($"  - {value}");
            }
        }

        private static string FormatIds(IReadOnlyList<string> ids)
        {
            return ids.Count == 0 ? "—" : string.Join(", ", ids);
        }
    }
}
