using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace VentCalc.Core.Models
{
    public sealed class VentNetworkInfo
    {
        public VentNetworkInfo(
            string selectedElementId,
            IEnumerable<VentNetworkNode> elements,
            IEnumerable<VentNetworkConnection> connections,
            int ductCount,
            int fittingCount,
            int accessoryCount,
            int terminalCount,
            int equipmentCount,
            int openConnectorCount,
            int startCandidateCount,
            int endCandidateCount)
        {
            SelectedElementId = selectedElementId;
            Elements = new ReadOnlyCollection<VentNetworkNode>(elements.ToList());
            Connections = new ReadOnlyCollection<VentNetworkConnection>(connections.ToList());
            DuctCount = ductCount;
            FittingCount = fittingCount;
            AccessoryCount = accessoryCount;
            TerminalCount = terminalCount;
            EquipmentCount = equipmentCount;
            OpenConnectorCount = openConnectorCount;
            StartCandidateCount = startCandidateCount;
            EndCandidateCount = endCandidateCount;
        }

        public string SelectedElementId { get; }

        public IReadOnlyList<VentNetworkNode> Elements { get; }

        public IReadOnlyList<VentNetworkConnection> Connections { get; }

        public int DuctCount { get; }

        public int FittingCount { get; }

        public int AccessoryCount { get; }

        public int TerminalCount { get; }

        public int EquipmentCount { get; }

        public int OpenConnectorCount { get; }

        public int StartCandidateCount { get; }

        public int EndCandidateCount { get; }

        public string ToReportText(VentPathSummary? pathSummary = null)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Связанная вентиляционная сеть");
            builder.AppendLine(new string('=', 32));
            builder.AppendLine($"Выбранный ElementId: {SelectedElementId}");
            builder.AppendLine($"Всего элементов: {Elements.Count}");
            builder.AppendLine($"Воздуховодов: {DuctCount}");
            builder.AppendLine($"Фитингов: {FittingCount}");
            builder.AppendLine($"Арматуры: {AccessoryCount}");
            builder.AppendLine($"Терминалов/решёток: {TerminalCount}");
            builder.AppendLine($"Оборудования: {EquipmentCount}");
            builder.AppendLine($"Открытых коннекторов: {OpenConnectorCount}");
            builder.AppendLine($"Найденных связей: {Connections.Count}");
            if (pathSummary != null)
            {
                builder.AppendLine($"Стартовых точек трассировки: {pathSummary.StartElementIds.Count}");
                builder.AppendLine($"Конечных точек трассировки: {pathSummary.EndElementIds.Count}");
                builder.AppendLine($"Игнорируемых заглушек: {pathSummary.IgnoredCapDetails.Count}");
            }
            builder.AppendLine();
            builder.AppendLine("Элементы сети");

            if (Elements.Count == 0)
            {
                builder.AppendLine("— Не найдены");
            }
            else
            {
                foreach (VentNetworkNode element in Elements)
                {
                    builder.AppendLine($"ElementId: {element.ElementId}");
                    builder.AppendLine($"  Категория: {element.CategoryName}");
                    builder.AppendLine($"  Категория API: {element.CategoryKey}");
                    builder.AppendLine($"  Имя: {element.Name}");
                    builder.AppendLine($"  Тип: {element.TypeName}");
                    builder.AppendLine($"  Семейство: {element.FamilyName}");
                    builder.AppendLine($"  Роль трассировки: {element.Role}");
                    builder.AppendLine($"  Причина роли: {element.PathRoleReason}");
                    builder.AppendLine($"  Стартовый кандидат: {element.IsStartCandidate}");
                    builder.AppendLine($"  Конечный кандидат: {element.IsEndCandidate}");
                    builder.AppendLine($"  Игнорируется в поиске трасс: {element.IsIgnoredForPathSearch}");
                    builder.AppendLine($"  Размер: {element.Size}");
                    builder.AppendLine($"  Расход: {element.FlowM3h}");
                    builder.AppendLine($"  Система: {element.SystemName}");
                    builder.AppendLine($"  Тип системы: {element.SystemType}");
                    builder.AppendLine($"  Уровень: {element.LevelName}");
                    builder.AppendLine($"  Коннекторов: {element.ConnectorCount}");
                    builder.AppendLine($"  Открытых коннекторов: {element.OpenConnectorCount}");
                    builder.AppendLine($"  Длина воздуховода: {element.DuctLengthMm / 1000.0:0.###} м");
                    builder.AppendLine($"  Подключенные элементы: {FormatConnectedIds(element.ConnectedElementIds)}");
                    builder.AppendLine();
                }
            }

            builder.AppendLine("Связи сети");
            if (Connections.Count == 0)
            {
                builder.AppendLine("— Не найдены");
            }
            else
            {
                foreach (VentNetworkConnection connection in Connections)
                {
                    builder.AppendLine(
                        $"{connection.FromElementId} [{connection.FromConnectorIndex}] -> " +
                        $"{connection.ToElementId} [{connection.ToConnectorIndex}] ({connection.ConnectionKind})");
                }
            }

            return builder.ToString();
        }

        private static string FormatConnectedIds(IReadOnlyList<string> connectedElementIds)
        {
            return connectedElementIds.Count == 0 ? "—" : string.Join(", ", connectedElementIds);
        }
    }
}
